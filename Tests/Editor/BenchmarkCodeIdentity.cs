namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Security.Cryptography;
    using Mono.Cecil;
    using Mono.Cecil.Cil;
    using UnityEditor;
    using PackageInfo = UnityEditor.PackageManager.PackageInfo;

    // Test-only provenance: compiler symbols bind source bytes to the loaded module.
    internal sealed class BenchmarkCodeIdentity
    {
        public string SourceSha256 { get; private set; } = string.Empty;
        public string LoadedModuleId { get; }
        public string AssemblySha256 { get; private set; } = string.Empty;
        public string CompilerSourceHash { get; private set; } = string.Empty;
        public string CompilerHashAlgorithm { get; private set; } = string.Empty;
        public string Failure { get; private set; } = string.Empty;
        public bool Verified { get; private set; }

        private readonly Assembly _assembly;
        private readonly string _sourcePath;

        public BenchmarkCodeIdentity(Assembly assembly, string sourcePath)
        {
            _assembly = assembly;
            _sourcePath = ResolveSourcePath(sourcePath);
            LoadedModuleId = assembly.ManifestModule.ModuleVersionId.ToString("D");
        }

        internal static string HashFile(string path)
        {
            using SHA256 hash = SHA256.Create();
            return Hex(hash.ComputeHash(File.ReadAllBytes(path)));
        }

        internal static string MapPackageSourcePath(string sourcePath, string assetPath, string resolvedPath)
        {
            string path = sourcePath.Replace('\\', '/');
            if (path.StartsWith("./", StringComparison.Ordinal))
            {
                path = path.Substring(2);
            }
            if (!string.IsNullOrEmpty(assetPath) && path.StartsWith(assetPath + "/", StringComparison.Ordinal))
            {
                return Path.GetFullPath(Path.Combine(resolvedPath, path.Substring(assetPath.Length + 1)));
            }
            return Path.GetFullPath(sourcePath);
        }

        internal static void ValidateEvidence(bool compiling, bool updating, string loadedModuleId, string diskModuleId, string compilerHash, string sourceHash)
        {
            if (compiling || updating)
            {
                throw new InvalidOperationException("Benchmark requires settled compilation and asset import.");
            }
            if (!string.Equals(loadedModuleId, diskModuleId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Loaded benchmark module differs from the assembly on disk. Wait for domain reload.");
            }
            if (string.IsNullOrEmpty(compilerHash) || !string.Equals(compilerHash, sourceHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Benchmark source differs from its compiler checksum. Recompile before measuring.");
            }
        }

        private static string ResolveSourcePath(string path)
        {
            string assetPath = path.Replace('\\', '/');
            if (assetPath.StartsWith("./", StringComparison.Ordinal))
            {
                assetPath = assetPath.Substring(2);
            }
            PackageInfo package = assetPath.StartsWith("Packages/", StringComparison.Ordinal) ? PackageInfo.FindForAssetPath(assetPath) : null;
            return MapPackageSourcePath(path, package?.assetPath, package?.resolvedPath);
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        public void Verify()
        {
            try
            {
                if (!Verified)
                {
                    CaptureCompilerEvidence();
                }
                ValidateEvidence(EditorApplication.isCompiling, EditorApplication.isUpdating, LoadedModuleId, LoadedModuleId, SourceSha256, HashFile(_sourcePath));
                if (!string.Equals(AssemblySha256, HashFile(_assembly.Location), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Benchmark assembly changed during measurement.");
                }
                Verified = true;
            }
            catch (Exception exception)
            {
                Verified = false;
                Failure = exception.Message;
                throw;
            }
        }

        private void CaptureCompilerEvidence()
        {
            // Reject missing or mismatched symbols rather than treating disk source as executing code.
            string assemblyHash = HashFile(_assembly.Location);
            using ModuleDefinition module = ModuleDefinition.ReadModule(_assembly.Location, new ReaderParameters { ReadSymbols = true, InMemory = true });
            string sourcePath = Path.GetFullPath(_sourcePath);
            Document document = null;
            foreach (TypeDefinition type in module.GetTypes())
            {
                foreach (MethodDefinition method in type.Methods)
                {
                    foreach (SequencePoint point in method.DebugInformation.SequencePoints)
                    {
                        if (
                            string.Equals(
                                ResolveSourcePath(point.Document.Url),
                                sourcePath,
                                Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
                            )
                        )
                        {
                            document = point.Document;
                            break;
                        }
                    }
                    if (document != null)
                    {
                        break;
                    }
                }
                if (document != null)
                {
                    break;
                }
            }
            if (document == null)
            {
                throw new InvalidOperationException("Compiler symbols do not identify the benchmark source.");
            }
            using HashAlgorithm hash = document.HashAlgorithm switch
            {
                DocumentHashAlgorithm.SHA1 => SHA1.Create(),
                DocumentHashAlgorithm.SHA256 => SHA256.Create(),
                _ => throw new InvalidOperationException("Unsupported compiler source checksum algorithm."),
            };
            string compilerHash = Hex(document.Hash);
            byte[] sourceBytes = File.ReadAllBytes(sourcePath);
            ValidateEvidence(
                EditorApplication.isCompiling,
                EditorApplication.isUpdating,
                LoadedModuleId,
                module.Mvid.ToString("D"),
                compilerHash,
                Hex(hash.ComputeHash(sourceBytes))
            );
            using SHA256 sha256 = SHA256.Create();
            SourceSha256 = Hex(sha256.ComputeHash(sourceBytes));
            AssemblySha256 = assemblyHash;
            CompilerSourceHash = compilerHash;
            CompilerHashAlgorithm = document.HashAlgorithm.ToString();
        }
    }
}
