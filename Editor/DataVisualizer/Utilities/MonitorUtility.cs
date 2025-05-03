namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using System;
    using System.Runtime.InteropServices;
    using UnityEngine;

    // For IntPtr, Exception

    // For DllImport, Marshal, StructLayout etc.

    public static class MonitorUtility
    {
        public static Rect GetPrimaryMonitorRect()
        {
            Rect rect = TryGetPrimaryMonitorRect();
            if ((rect.width <= 0 || rect.height <= 0) && Display.displays.Length != 0)
            {
                rect = new Rect(0, 0, rect.width, rect.height);
            }

            return rect;
        }

        private static Rect TryGetPrimaryMonitorRect()
        {
            try
            {
#if UNITY_EDITOR_WIN
                // --- Windows Implementation (using P/Invoke) ---
                return GetPrimaryMonitorRect_Windows_PInvoke();

#elif UNITY_EDITOR_OSX
                // --- macOS Implementation (using P/Invoke) ---
                return GetPrimaryMonitorRect_Mac_PInvoke(); // Renamed for clarity
#elif UNITY_EDITOR_LINUX
                // --- Linux Implementation (Placeholder) ---
                return Rect.zero;
#else
                return Rect.zero; // Fallback for other platforms
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"Error getting primary monitor rect via platform code: {ex.Message}\n{ex.StackTrace}"
                );
                return Rect.zero; // Return invalid rect on error to trigger fallback
            }
        }

        // --- Windows P/Invoke Definitions and Helper ---
#if UNITY_EDITOR_WIN

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)] // Important: Win32 BOOL is not C# bool directly
        private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

        private static Rect GetPrimaryMonitorRect_Windows_PInvoke()
        {
            // Point (0,0) should be on the primary monitor in virtual screen coords
            POINT zeroPoint = new POINT { X = 0, Y = 0 };

            // Get the handle to the primary monitor
            IntPtr hMonitor = MonitorFromPoint(zeroPoint, MONITOR_DEFAULTTOPRIMARY);

            if (hMonitor == IntPtr.Zero)
            {
                Debug.LogError(
                    "PInvoke Error: Could not get primary monitor handle via MonitorFromPoint."
                );
                return Rect.zero;
            }

            MONITORINFO monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFO)); // Crucial: Set the size field

            // Get monitor information
            if (!GetMonitorInfoW(hMonitor, ref monitorInfo))
            {
                Debug.LogError("PInvoke Error: GetMonitorInfoW failed.");
                return Rect.zero;
            }

            // Extract the monitor rectangle (full area, not just working area)
            RECT monitorRectWin32 = monitorInfo.rcMonitor;

            // Convert Win32 RECT (Left, Top, Right, Bottom) to Unity Rect (X, Y, Width, Height)
            int width = monitorRectWin32.Right - monitorRectWin32.Left;
            int height = monitorRectWin32.Bottom - monitorRectWin32.Top;

            return new Rect(monitorRectWin32.Left, monitorRectWin32.Top, width, height);
        }

#endif // UNITY_EDITOR_WIN

        // --- macOS P/Invoke Definitions and Helper ---
#if UNITY_EDITOR_OSX

        // Define necessary Objective-C runtime functions via P/Invoke
        [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
        private static extern IntPtr objc_getClass(string className);

        [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
        private static extern IntPtr sel_registerName(string selectorName);

        // Use IntPtr version for most messages, specialized versions for struct returns if needed
        [DllImport(
            "/System/Library/Frameworks/AppKit.framework/AppKit",
            EntryPoint = "objc_msgSend"
        )]
        private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

        [DllImport(
            "/System/Library/Frameworks/AppKit.framework/AppKit",
            EntryPoint = "objc_msgSend"
        )]
        private static extern IntPtr objc_msgSend_IntPtr_UInt(
            IntPtr receiver,
            IntPtr selector,
            uint index
        );

        // Need specific signature for returning CGRect (a struct)
        [DllImport(
            "/System/Library/Frameworks/AppKit.framework/AppKit",
            EntryPoint = "objc_msgSend_stret"
        )]
        private static extern void objc_msgSend_stret_CGRect(
            out CGRect stret,
            IntPtr receiver,
            IntPtr selector
        );

        // Define the CGRect struct matching macOS's definition (usually contains CGPoint origin, CGSize size)
        [StructLayout(LayoutKind.Sequential)]
        private struct CGPoint
        {
            public double x;
            public double y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CGSize
        {
            public double width;
            public double height;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CGRect
        {
            public CGPoint origin;
            public CGSize size;
        }

        private static Rect GetPrimaryMonitorRect_Mac_PInvoke() // Renamed function
        {
            IntPtr NSScreenClass = objc_getClass("NSScreen");
            if (NSScreenClass == IntPtr.Zero)
            {
                Debug.LogError("PInvoke Error: Failed to get NSScreen class.");
                return Rect.zero;
            }

            IntPtr screensSelector = sel_registerName("screens");
            if (screensSelector == IntPtr.Zero)
            {
                Debug.LogError("PInvoke Error: Failed to get 'screens' selector.");
                return Rect.zero;
            }

            IntPtr screensArray = objc_msgSend_IntPtr(NSScreenClass, screensSelector);
            if (screensArray == IntPtr.Zero)
            {
                Debug.LogError("PInvoke Error: Failed to get screens array.");
                return Rect.zero;
            }

            IntPtr objectAtIndexSelector = sel_registerName("objectAtIndex:");
            if (objectAtIndexSelector == IntPtr.Zero)
            {
                Debug.LogError("PInvoke Error: Failed to get 'objectAtIndex:' selector.");
                return Rect.zero;
            }
            IntPtr primaryScreen = objc_msgSend_IntPtr_UInt(screensArray, objectAtIndexSelector, 0); // Index 0 is primary
            if (primaryScreen == IntPtr.Zero)
            {
                Debug.LogError("PInvoke Error: Failed to get primary screen object from array.");
                return Rect.zero;
            }

            IntPtr frameSelector = sel_registerName("frame");
            if (frameSelector == IntPtr.Zero)
            {
                Debug.LogError("PInvoke Error: Failed to get 'frame' selector.");
                return Rect.zero;
            }

            CGRect screenFrame;
            objc_msgSend_stret_CGRect(out screenFrame, primaryScreen, frameSelector);

            // Simplified mapping: Use dimensions, assume top-left origin is (0,0) for primary screen space.
            // A more robust conversion might be needed if primary screen isn't at global (0,0)
            // or if EditorWindow.position behaves unexpectedly on Mac regarding Y-coordinates.
            // See previous answer's notes on coordinate system complexities.
            Debug.Log(
                $"macOS primary screen frame: O=({screenFrame.origin.x},{screenFrame.origin.y}) S=({screenFrame.size.width},{screenFrame.size.height})"
            );
            return new Rect(0, 0, (float)screenFrame.size.width, (float)screenFrame.size.height);
        }
#endif // UNITY_EDITOR_OSX
    }
#endif
}
