namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /*
        Offscreen editor-window capture for documentation automation (#114, T10).

        macOS TCC denies `screencapture` to the Unity editor process (recorded on #114),
        so this helper never reads the desktop. It hosts the target window as a popup (a
        panel exists only once shown, and popup mode keeps the dock tab out of the render),
        settles the window's UI Toolkit panel layout, renders the panel into an offscreen
        linear RenderTexture through repaint/render cycles, and reads back only that target.

        The panel's ValidateLayout, Repaint, and Render members are internal with no public
        equivalent, so they are invoked reflectively with inherited lookup. A Unity upgrade
        that removes one fails closed here with an exception instead of silently writing a
        wrong image.

        Every graphics global the render touches (active render target, GL.sRGBWrite) is
        restored inside Capture, and every texture it creates is destroyed there too, on
        the failure path included.
    */
    internal static class EditorSurfaceCapture
    {
        private const int MaxLayoutPasses = 8;
        private const int RenderPassCount = 3;
        private const int PngSignatureLength = 8;

        /*
            Offscreen rendering needs a real graphics device. An editor launched with
            -nographics cannot rasterize anything, so capture throws fail-closed and tests
            skip rather than assert against that device.
        */
        internal static bool IsSupported =>
            GraphicsDeviceType.Null != SystemInfo.graphicsDeviceType;

        private static readonly BindingFlags InheritedInstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /*
            Renders the window's root visual tree into an offscreen render target sized from
            the settled root layout, and writes a 24-bit PNG of the root's laid-out rect to
            outputPath. The window must already be shown through ShowPopup so it owns a
            panel; its lifecycle stays with the caller (close it through CloseWindow).
        */
        internal static EditorSurfaceCaptureResult Capture(EditorWindow window, string outputPath)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Capture needs an output path.", nameof(outputPath));
            }

            if (!IsSupported)
            {
                throw new InvalidOperationException(
                    "EditorSurfaceCapture needs a graphics device; this editor is running "
                        + "without one, so capture refuses to write a blank image."
                );
            }

            IPanel panel = window.rootVisualElement.panel;
            if (panel == null)
            {
                throw new InvalidOperationException(
                    $"Window '{window.name}' has no panel; show it with {nameof(ShowPopup)} "
                        + "before capturing."
                );
            }

            RenderTexture previousTarget = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture target = null;
            Texture2D readback = null;
            try
            {
                SettleLayout(window);
                Rect rootBounds = window.rootVisualElement.worldBound;
                int width = Mathf.RoundToInt(rootBounds.width);
                int height = Mathf.RoundToInt(rootBounds.height);
                if (width < 1 || height < 1)
                {
                    throw new InvalidOperationException(
                        $"The window root laid out to {rootBounds.width}x{rootBounds.height}; "
                            + "there is nothing to capture."
                    );
                }

                target = new RenderTexture(
                    width,
                    height,
                    24,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear
                );
                if (!target.Create())
                {
                    throw new InvalidOperationException(
                        $"Could not create a {width}x{height} capture canvas."
                    );
                }

                GL.sRGBWrite = false;
                RenderTexture.active = target;
                GL.Clear(true, true, Color.clear);

                /*
                    Three cycles, in this order. The settled layout is what Repaint walks; the
                    first cycle realizes lazy content and grows dynamic font atlases, and the
                    later cycles draw the settled sets. Stopping early can yield a valid PNG
                    with blank scroll bodies or missing labels.
                */
                Event repaintEvent = new() { type = EventType.Repaint };
                for (int pass = 0; pass < RenderPassCount; pass++)
                {
                    InvokePanelMethod(panel, "Repaint", new object[] { repaintEvent });
                    InvokePanelMethod(panel, "Render", Array.Empty<object>());
                }

                RectInt crop = new(
                    Mathf.RoundToInt(rootBounds.x),
                    Mathf.RoundToInt(height - rootBounds.yMax),
                    width,
                    height
                );
                readback = new Texture2D(crop.width, crop.height, TextureFormat.RGB24, false, true);
                readback.ReadPixels(new Rect(crop.x, crop.y, crop.width, crop.height), 0, 0, false);
                readback.Apply(false, false);

                byte[] png = readback.EncodeToPNG();
                if (png == null || png.Length <= PngSignatureLength)
                {
                    throw new InvalidOperationException("PNG encoding produced no usable bytes.");
                }

                string directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(outputPath, png);
                return new EditorSurfaceCaptureResult(
                    outputPath,
                    crop.width,
                    crop.height,
                    png.Length,
                    CountDistinctColors(readback)
                );
            }
            finally
            {
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousSrgbWrite;

                if (readback != null)
                {
                    Object.DestroyImmediate(readback);
                }

                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }
            }
        }

        /*
            Shows the window in popup mode so a panel exists without painting a dock tab into
            the panel render target on macOS. Showing it does not make capture read the
            desktop: pixels come from the offscreen render target only.
        */
        internal static void ShowPopup(EditorWindow window)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            window.ShowPopup();
        }

        /*
            Closes a window this test suite showed or created, guaranteeing it is destroyed.
            EditorWindow.Close on a popup-hosted window does not reliably remove it from the
            editor's window registry, and a leftover capture window of a type another test
            looks up by name gets reused or close-stomped by later tests; DestroyImmediate
            removes it unconditionally. Callers that need production teardown run it (for
            example the window Cleanup method) before calling this.
        */
        internal static void CloseWindow(EditorWindow window)
        {
            if (window == null)
            {
                return;
            }

            window.rootVisualElement.Clear();
            Object.DestroyImmediate(window);
        }

        /*
            Runs the panel's layout until it stops changing, so the render walks the geometry
            a reader settles on rather than an intermediate frame. One pass is never enough:
            text height is only final once its width is, and realizing content changes what
            neighboring elements measure.
        */
        internal static void SettleLayout(EditorWindow window)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            string previous = null;
            for (int pass = 0; pass < MaxLayoutPasses; pass++)
            {
                InvokePanelMethod(
                    window.rootVisualElement.panel,
                    "ValidateLayout",
                    Array.Empty<object>()
                );
                string current = DescribeLayout(window.rootVisualElement);
                if (string.Equals(current, previous, StringComparison.Ordinal))
                {
                    return;
                }

                previous = current;
            }
        }

        internal static void InvokePanelMethod(IPanel panel, string methodName, object[] arguments)
        {
            Type panelType = panel.GetType();
            Type[] argumentTypes = new Type[arguments.Length];
            for (int index = 0; index < arguments.Length; index++)
            {
                argumentTypes[index] = arguments[index].GetType();
            }

            MethodInfo method = panelType.GetMethod(
                methodName,
                InheritedInstanceMembers,
                binder: null,
                types: argumentTypes,
                modifiers: null
            );
            if (method == null)
            {
                throw new InvalidOperationException(
                    $"Panel type {panelType.FullName} exposes no '{methodName}' method taking "
                        + $"{argumentTypes.Length} argument(s); the offscreen capture cannot drive "
                        + "this editor version's panel."
                );
            }

            method.Invoke(panel, arguments);
        }

        private static string DescribeLayout(VisualElement root)
        {
            StringBuilder description = new();
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
            {
                Rect layout = element.layout;
                description
                    .Append(layout.x)
                    .Append(',')
                    .Append(layout.y)
                    .Append(',')
                    .Append(layout.width)
                    .Append(',')
                    .Append(layout.height)
                    .Append(';');
            }

            return description.ToString();
        }

        /*
            A cleared target and a rendered frame are both valid PNGs, so callers need a cheap
            way to prove the panel actually drew. Distinct-color count is that proof: a blank
            frame has exactly one.
        */
        private static int CountDistinctColors(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            HashSet<int> distinct = new();
            foreach (Color32 pixel in pixels)
            {
                distinct.Add((pixel.r << 16) | (pixel.g << 8) | pixel.b);
            }

            return distinct.Count;
        }
    }
}
