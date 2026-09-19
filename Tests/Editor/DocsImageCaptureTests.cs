namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    internal sealed class DocsImageCaptureTests
    {
        private const int PngSignatureLength = 8;

        private static void AssertValidPngFile(EditorSurfaceCaptureResult result)
        {
            byte[] bytes = File.ReadAllBytes(result.OutputPath);
            Assert.That(
                bytes.Length,
                Is.GreaterThan(PngSignatureLength + 16),
                $"A captured PNG must carry its signature and IHDR header, got {bytes.Length} bytes."
            );
            byte[] signature = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
            for (int index = 0; index < PngSignatureLength; index++)
            {
                Assert.That(
                    bytes[index],
                    Is.EqualTo(signature[index]),
                    "Captured files must be real PNGs."
                );
            }

            int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            Assert.AreEqual(
                result.Width,
                width,
                "The PNG IHDR width must match the captured surface."
            );
            Assert.AreEqual(
                result.Height,
                height,
                "The PNG IHDR height must match the captured surface."
            );
            Assert.AreEqual(
                result.ByteCount,
                bytes.Length,
                "Every encoded byte must reach the file."
            );
            Assert.That(
                result.DistinctColorCount,
                Is.GreaterThan(1),
                "The captured window must be non-blank; a single distinct color means the "
                    + "panel never painted and the capture is useless for documentation."
            );
        }

        [Test]
        public void ShouldExposeManifestShotNames()
        {
            Assert.That(DocsImageCapture.ShotNames, Is.Not.Empty);
            for (int index = 0; index < DocsImageCapture.ShotNames.Count; index++)
            {
                Assert.That(
                    DocsImageCapture.ShotNames[index],
                    Is.Not.Empty.And.Not.Contains(' '),
                    "Shot names are file-name fragments and must not contain spaces."
                );
            }
        }

        [Test]
        public void ShouldFailClosedWhenShotIsUnknown(
            [Values(null, "", "hero", "Layout", "layout ")] string shotName
        )
        {
            Assert.Throws<ArgumentException>(() =>
                DocsImageCapture.CaptureShot(shotName, "Temp/DocsImageCaptures")
            );
        }

        [Test]
        public void ShouldFailClosedWhenOutputDirectoryIsInvalid(
            [Values(null, "", "   ")] string outputDirectory
        )
        {
            Assert.Throws<ArgumentException>(() =>
                DocsImageCapture.CaptureShot("layout", outputDirectory)
            );
            Assert.Throws<ArgumentException>(() => DocsImageCapture.CaptureAll(outputDirectory));
        }

        [TestCase(
            null,
            new string[0],
            "Temp/DocsImageCaptures",
            TestName = "Defaults without argument and environment"
        )]
        [TestCase(
            "env-value",
            new string[0],
            "env-value",
            TestName = "Environment value wins when no argument is present"
        )]
        [TestCase(
            "env-value",
            new[] { "-docsImageOutputDir", "Temp/FromArg" },
            "Temp/FromArg",
            TestName = "Argument wins over environment value"
        )]
        [TestCase(
            null,
            new[] { "-docsImageOutputDir", "Temp/FromArg" },
            "Temp/FromArg",
            TestName = "Argument wins without environment value"
        )]
        [TestCase(
            "env-value",
            new[] { "-docsImageOutputDir", "" },
            "env-value",
            TestName = "Empty argument value falls back to the environment value"
        )]
        [TestCase(
            null,
            new[] { "-docsImageOutputDir", "  " },
            "Temp/DocsImageCaptures",
            TestName = "Whitespace argument value falls back to the default"
        )]
        [TestCase(
            "env-value",
            new[] { "-docsImageOutputDir" },
            "env-value",
            TestName = "Argument without a value falls back to the environment value"
        )]
        public void ShouldResolveOutputDirectoryFromArgumentOverEnvironment(
            string environmentValue,
            string[] arguments,
            string expected
        )
        {
            Assert.That(
                DocsImageCapture.ResolveOutputDirectory(arguments, environmentValue),
                Is.EqualTo(expected)
            );
        }

        [Test]
        public void ShouldCaptureEveryManifestShotToValidNonBlankPng()
        {
            if (!EditorSurfaceCapture.IsSupported)
            {
                Assert.Ignore("Offscreen capture needs a graphics device.");
            }

            string outputDirectory = Path.Combine("Temp", "DocsImageCaptures");
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }

            /*
                CaptureAll owns the fixture assets, window preferences, and window lifecycle,
                so the test only restores the window Instance field that predates the run,
                like EditorSurfaceCaptureTests does around its real-window capture.
            */
            FieldInfo instanceField = typeof(DataVisualizerWindow).GetField(
                "Instance",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.That(instanceField != null, "The window Instance field must exist.");
            object previousInstance = instanceField.GetValue(null);

            try
            {
                IReadOnlyList<EditorSurfaceCaptureResult> results = DocsImageCapture.CaptureAll(
                    outputDirectory
                );
                Assert.That(
                    results.Count,
                    Is.EqualTo(DocsImageCapture.ShotNames.Count),
                    "One result per manifest shot."
                );
                foreach (EditorSurfaceCaptureResult result in results)
                {
                    AssertValidPngFile(result);
                }
            }
            finally
            {
                instanceField.SetValue(null, previousInstance);
            }
        }
    }
}
