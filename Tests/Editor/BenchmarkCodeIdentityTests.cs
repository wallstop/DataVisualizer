namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using NUnit.Framework;

    internal sealed class BenchmarkCodeIdentityTests
    {
        private const string DriverPath = "Packages/com.wallstop-studios.data-visualizer/Tests/Editor/MetadataScaleBenchmarkTests.cs";

        [TestCase(true, false, "module", "module", "hash", "hash")]
        [TestCase(false, true, "module", "module", "hash", "hash")]
        [TestCase(false, false, "old module", "new module", "hash", "hash")]
        [TestCase(false, false, "module", "module", "old source", "new source")]
        [TestCase(false, false, "module", "module", "", "")]
        public void ShouldRejectUnsettledOrStaleEvidence(bool compiling, bool updating, string loadedModule, string diskModule, string compilerHash, string sourceHash)
        {
            Assert.Throws<InvalidOperationException>(() => BenchmarkCodeIdentity.ValidateEvidence(compiling, updating, loadedModule, diskModule, compilerHash, sourceHash));
        }

        [Test]
        public void ShouldVerifyCompilerSourceAndLoadedModuleOnSettledHost()
        {
            BenchmarkCodeIdentity identity = new(typeof(MetadataScaleBenchmarkTests).Assembly, DriverPath);
            identity.Verify();
            Assert.That(identity.Verified);
            Assert.That(identity.Failure, Is.Empty);
            Assert.That(identity.SourceSha256, Is.EqualTo(BenchmarkCodeIdentity.HashFile(DriverPath)));
            Assert.That(identity.LoadedModuleId, Is.EqualTo(typeof(MetadataScaleBenchmarkTests).Assembly.ManifestModule.ModuleVersionId.ToString("D")));
            Assert.That(identity.AssemblySha256.Length, Is.EqualTo(64));
            Assert.That(identity.CompilerSourceHash, Is.Not.Empty);
            identity.Verify();
            Assert.That(identity.Verified);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldRejectDriverEditsBeforeOrDuringMeasurement(bool verifyBeforeEdit)
        {
            BenchmarkCodeIdentity identity = new(typeof(MetadataScaleBenchmarkTests).Assembly, DriverPath);
            if (verifyBeforeEdit)
            {
                identity.Verify();
            }
            byte[] original = File.ReadAllBytes(DriverPath);
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => File.WriteAllBytes(DriverPath, original));
            File.AppendAllText(DriverPath, "\n// Uncompiled source change\n");
            Assert.Throws<InvalidOperationException>(identity.Verify);
            Assert.That(identity.Verified, Is.False);
            Assert.That(identity.Failure, Does.Contain("source"));
        }

        [Test]
        public void ShouldRejectSourceBytesNotUsedByLoadedCompiler()
        {
            string path = Path.Combine("Temp", "StaleBenchmark-" + Guid.NewGuid().ToString("N") + ".cs");
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => File.Delete(path));
            File.WriteAllText(path, "// Not a compiled source document");
            BenchmarkCodeIdentity identity = new(typeof(MetadataScaleBenchmarkTests).Assembly, path);
            Assert.Throws<InvalidOperationException>(identity.Verify);
            Assert.That(identity.Verified, Is.False);
            Assert.That(identity.Failure, Does.Contain("symbols"));
        }
    }
}
