namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.Profiling;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using Object = UnityEngine.Object;

    // Test-only prototype. Production discovery remains main-asset-only.
    public sealed class SubassetFeasibilityTests
    {
        public static List<SubassetMetadataRecord> ReadMetadata(string path)
        {
            List<SubassetMetadataRecord> records = new();
            foreach (Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (candidate is ScriptableObject asset && asset != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId))
                {
                    records.Add(
                        new SubassetMetadataRecord
                        {
                            guid = guid,
                            localId = localId,
                            name = asset.name,
                            typeIdentity = asset.GetType().AssemblyQualifiedName,
                            path = path,
                            isMainAsset = AssetDatabase.IsMainAsset(asset),
                        }
                    );
                }
            }
            return records;
        }

        public static ScriptableObject ResolveOrNull(SubassetMetadataRecord record)
        {
            string path = AssetDatabase.GUIDToAssetPath(record.guid);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }
            foreach (Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (
                    candidate is ScriptableObject asset
                    && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId)
                    && string.Equals(guid, record.guid, StringComparison.Ordinal)
                    && localId == record.localId
                    && string.Equals(asset.GetType().AssemblyQualifiedName, record.typeIdentity, StringComparison.Ordinal)
                )
                {
                    return asset;
                }
            }
            return null;
        }

        public static VisualElement CreateReadOnlyInspector(SerializedObject serialized)
        {
            VisualElement root = new();
            SerializedProperty property = serialized.GetIterator();
            bool enter = true;
            while (property.NextVisible(enter))
            {
                enter = false;
                root.Add(new PropertyField(property.Copy()));
            }
            root.Bind(serialized);
            root.SetEnabled(false);
            return root;
        }

        public static string RunExperiment(string outputPath, string revision, int parentCount = 20)
        {
            if (parentCount < 1 || 100 < parentCount)
            {
                throw new ArgumentOutOfRangeException(nameof(parentCount));
            }
            SubassetFeasibilityReport report = new()
            {
                measuredUtc = DateTime.UtcNow.ToString("O"),
                revision = revision,
                unity = Application.unityVersion,
                os = SystemInfo.operatingSystem,
                cpu = SystemInfo.processorType,
                graphics = SystemInfo.graphicsDeviceName,
                parentCount = parentCount,
                memoryMegabytes = SystemInfo.systemMemorySize,
                dpi = Screen.dpi,
                pixelsPerPoint = EditorGUIUtility.pixelsPerPoint,
                darkTheme = EditorGUIUtility.isProSkin,
                backgroundAssetCount = AssetDatabase.GetAllAssetPaths().Length,
            };
            using (TestCleanupScope cleanup = new())
            {
                string folder = CreateFolder(cleanup);
                report.fixtureRoot = folder;
                Stopwatch staging = Stopwatch.StartNew();
                List<string> paths = new();
                for (int i = 0; i < parentCount; i++)
                {
                    paths.Add(CreateFixture(folder + "/Parent" + i + ".asset"));
                }
                report.fixtureCreateMilliseconds = staging.Elapsed.TotalMilliseconds;
                Stopwatch clock = Stopwatch.StartNew();
                foreach (string path in paths)
                {
                    if (AssetDatabase.LoadMainAssetAtPath(path) is ScriptableObject)
                    {
                        report.baselineMainAssetCount++;
                    }
                }
                report.baselineMainReadMilliseconds = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                int count = ReadAll(paths);
                report.firstReadMilliseconds = clock.Elapsed.TotalMilliseconds;
                Assert.That(count, Is.EqualTo(parentCount * 4));
                report.scriptableObjectCount = count;
                foreach (SubassetMetadataRecord record in ReadMetadata(paths[0]))
                {
                    report.exampleLocalIds.Add(record.localId);
                }
                for (int i = 0; i < report.warmups; i++)
                {
                    Assert.That(ReadAll(paths), Is.EqualTo(count));
                }
                long calibrationStart = GC.GetAllocatedBytesForCurrentThread();
                byte[] calibration = new byte[1024];
                report.allocationCounterSupported = calibrationStart < GC.GetAllocatedBytesForCurrentThread();
                GC.KeepAlive(calibration);
                for (int i = 0; i < 30; i++)
                {
                    long heap = Profiler.GetMonoUsedSizeLong();
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    clock.Restart();
                    int actual = ReadAll(paths);
                    report.warmMilliseconds.Add(clock.Elapsed.TotalMilliseconds);
                    report.warmAllocatedBytes.Add(report.allocationCounterSupported ? GC.GetAllocatedBytesForCurrentThread() - allocated : -1);
                    report.warmManagedHeapDeltaBytes.Add(Profiler.GetMonoUsedSizeLong() - heap);
                    Assert.That(actual, Is.EqualTo(count));
                }
                // The result schema copies only value types and strings; it has no Unity object fields.
                foreach (FieldInfo field in typeof(SubassetMetadataRecord).GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (typeof(Object).IsAssignableFrom(field.FieldType))
                    {
                        report.metadataObjectReferenceFields++;
                    }
                }
                Assert.That(report.metadataObjectReferenceFields, Is.Zero);
            }
            report.cleanupComplete = !AssetDatabase.IsValidFolder(report.fixtureRoot);
            Assert.That(report.cleanupComplete, Is.True);
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
            return outputPath;
        }

        private static string CreateFolder(TestCleanupScope cleanup)
        {
            string folder = "Assets/SubassetFeasibility-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            return folder;
        }

        private static string CreateFixture(string path)
        {
            AssetValidationTestData main = ScriptableObject.CreateInstance<AssetValidationTestData>();
            main.name = "Duplicate";
            AssetDatabase.CreateAsset(main, path);
            for (int i = 0; i < 3; i++)
            {
                ScriptableObject child = i == 0 ? ScriptableObject.CreateInstance<AssetValidationTestData>() : ScriptableObject.CreateInstance<EditorOnlyCreationSubasset>();
                child.name = "Duplicate";
                AssetDatabase.AddObjectToAsset(child, main);
            }
            Texture2D texture = new(1, 1) { name = "NonScriptableObject" };
            AssetDatabase.AddObjectToAsset(texture, main);
            AssetDatabase.SaveAssets();
            return path;
        }

        private static int ReadAll(List<string> paths)
        {
            int count = 0;
            foreach (string path in paths)
            {
                count += ReadMetadata(path).Count;
            }
            return count;
        }

        private static List<string> Identities(List<SubassetMetadataRecord> records)
        {
            List<string> result = new();
            foreach (SubassetMetadataRecord record in records)
            {
                result.Add(record.guid + ":" + record.localId);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        [Test]
        public void ShouldPreserveCompositeIdentityAcrossParentMoveAndReimportAndRejectDeletedParent()
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFolder(cleanup);
            string path = CreateFixture(folder + "/Parent.asset");
            List<SubassetMetadataRecord> records = ReadMetadata(path);
            Assert.That(records.Count, Is.EqualTo(4));
            Assert.That(new HashSet<string>(Identities(records)).Count, Is.EqualTo(4));
            Assert.That(records.FindAll(record => record.isMainAsset).Count, Is.EqualTo(1));
            Assert.That(records.FindAll(record => record.name == "Duplicate").Count, Is.EqualTo(3));
            string guid = AssetDatabase.AssetPathToGUID(path);
            foreach (SubassetMetadataRecord record in records)
            {
                Assert.That(record.guid, Is.EqualTo(guid));
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Assert.That(Identities(ReadMetadata(path)), Is.EqualTo(Identities(records)));
            string moved = folder + "/Moved.asset";
            Assert.That(AssetDatabase.MoveAsset(path, moved), Is.Empty);
            Assert.That(Identities(ReadMetadata(moved)), Is.EqualTo(Identities(records)));
            foreach (SubassetMetadataRecord record in records)
            {
                Assert.That(ResolveOrNull(record) != null);
            }
            Assert.That(AssetDatabase.DeleteAsset(moved), Is.True);
            Assert.That(File.Exists(moved), Is.False);
            Assert.That(ReadMetadata(moved), Is.Empty);
            foreach (SubassetMetadataRecord record in records)
            {
                Assert.That(ResolveOrNull(record) == null);
            }
        }

        [Test]
        public void ShouldKeepAuthoredIdsAndAssetBytesUnchangedWhenInspectingSubassets()
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFolder(cleanup);
            string path = CreateFixture(folder + "/Parent.asset");
            foreach (Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (candidate is AssetValidationTestData data)
                {
                    using SerializedObject setup = new(data);
                    setup.FindProperty("_assetGuid").stringValue = "shared-authored-id";
                    setup.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            AssetDatabase.SaveAssets();
            byte[] before = File.ReadAllBytes(path);
            List<SubassetMetadataRecord> records = ReadMetadata(path);
            int inspected = 0;
            EditorSurfaceCaptureHostWindow host = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(host));
            EditorSurfaceCapture.ShowPopup(host);
            foreach (Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (candidate is ScriptableObject asset)
                {
                    using SerializedObject serialized = new(asset);
                    using TestCleanupScope bindingCleanup = new();
                    VisualElement inspector = CreateReadOnlyInspector(serialized);
                    bindingCleanup.Defer(inspector.Unbind);
                    host.rootVisualElement.Clear();
                    host.rootVisualElement.Add(inspector);
                    EditorSurfaceCapture.SettleLayout(host);
                    foreach (PropertyField field in inspector.Query<PropertyField>().ToList())
                    {
                        Assert.That(field.enabledInHierarchy, Is.False);
                    }
                    Assert.That(inspector.enabledSelf, Is.False);
                    Assert.That(serialized.hasModifiedProperties, Is.False);
                    Assert.That(EditorUtility.IsDirty(asset), Is.False);
                    if (asset is AssetValidationTestData data)
                    {
                        Assert.That(data.Id, Is.EqualTo("shared-authored-id"));
                    }
                    inspected++;
                }
            }
            Assert.That(inspected, Is.EqualTo(records.Count));
            Assert.That(Identities(records).Count, Is.EqualTo(4));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }
    }
}
