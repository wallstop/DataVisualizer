namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.Profiling;
    using UnityEngine.SceneManagement;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    // Test-only source adapter; never saves prefab contents or enables production browsing.
    public sealed class PrefabFeasibilityTests
    {
        public static List<PrefabComponentMetadataRecord> ReadMetadata(string path)
        {
            List<PrefabComponentMetadataRecord> result = new();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                return result;
            }
            PrefabAssetType kind = PrefabUtility.GetPrefabAssetType(prefab);
            if (kind != PrefabAssetType.Regular && kind != PrefabAssetType.Variant)
            {
                return result;
            }
            string guid = AssetDatabase.AssetPathToGUID(path);
            foreach (Transform transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                foreach (Component component in transform.GetComponents<Component>())
                {
                    result.Add(
                        new PrefabComponentMetadataRecord
                        {
                            assetGuid = guid,
                            globalId = component == null ? string.Empty : GlobalObjectId.GetGlobalObjectIdSlow(component).ToString(),
                            typeIdentity = component == null ? string.Empty : component.GetType().AssemblyQualifiedName,
                            hierarchyPath = prefab.name + "/" + AnimationUtility.CalculateTransformPath(transform, prefab.transform),
                            status = component == null ? "missing_script" : "available",
                        }
                    );
                }
            }
            return result;
        }

        public static Component ResolveOrNull(PrefabComponentMetadataRecord record)
        {
            string path = AssetDatabase.GUIDToAssetPath(record.assetGuid);
            if (
                string.IsNullOrEmpty(path)
                || !File.Exists(path)
                || !GlobalObjectId.TryParse(record.globalId, out GlobalObjectId id)
                || !string.Equals(id.assetGUID.ToString(), record.assetGuid, StringComparison.Ordinal)
            )
            {
                return null;
            }
            Component component = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Component;
            return component != null && string.Equals(component.GetType().AssemblyQualifiedName, record.typeIdentity, StringComparison.Ordinal) ? component : null;
        }

        public static string CreateFixtures(string folder)
        {
            using TestCleanupScope cleanup = new();
            Scene owned = EditorSceneManager.NewPreviewScene();
            cleanup.Defer(() => EditorSceneManager.ClosePreviewScene(owned));
            GameObject root = new("Repeated");
            cleanup.Defer(() => Object.DestroyImmediate(root));
            SceneManager.MoveGameObjectToScene(root, owned);
            root.AddComponent<BoxCollider>();
            root.AddComponent<BoxCollider>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Regular.prefab");
            GameObject variant = (GameObject)PrefabUtility.InstantiatePrefab(prefab, owned);
            cleanup.Defer(() => Object.DestroyImmediate(variant));
            variant.GetComponent<BoxCollider>().size = new Vector3(2, 3, 4);
            PrefabUtility.RecordPrefabInstancePropertyModifications(variant.GetComponent<BoxCollider>());
            PrefabUtility.SaveAsPrefabAsset(variant, folder + "/Variant.prefab");
            GameObject nested = new("Container");
            cleanup.Defer(() => Object.DestroyImmediate(nested));
            SceneManager.MoveGameObjectToScene(nested, owned);
            for (int i = 0; i < 2; i++)
            {
                GameObject child = (GameObject)PrefabUtility.InstantiatePrefab(prefab, owned);
                child.transform.SetParent(nested.transform);
            }
            PrefabUtility.SaveAsPrefabAsset(nested, folder + "/Nested.prefab");
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab, out string _, out long rootId);
            string text = File.ReadAllText(folder + "/Regular.prefab").Replace("\r\n", "\n");
            text = text.Replace("  m_Component:\n", "  m_Component:\n  - component: {fileID: 114999999}\n");
            text +=
                "\n--- !u!114 &114999999\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: "
                + rootId
                + "}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}\n  m_Name:\n  m_EditorClassIdentifier:\n";
            File.WriteAllText(folder + "/Missing.prefab", text);
            File.WriteAllText(folder + "/Model.obj", "o Triangle\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return folder;
        }

        public static string RunExperiment(string outputPath, string revision)
        {
            PrefabFeasibilityReport report = NewReport(revision, "Warm editor; isolated contents loads are separate from discovery");
            using (TestCleanupScope cleanup = new())
            {
                string folder = CreateFolder(cleanup);
                report.fixtureRoot = CreateFixtures(folder);
                Stopwatch clock = Stopwatch.StartNew();
                List<PrefabComponentMetadataRecord> records = ReadAll(folder);
                report.firstDiscoveryMilliseconds = clock.Elapsed.TotalMilliseconds;
                Assert.That(records.Count, Is.EqualTo(17));
                report.recordCount = records.Count;
                report.missingScriptCount = records.FindAll(record => record.status == "missing_script").Count;
                for (int i = 0; i < 5; i++)
                {
                    ReadAll(folder);
                }
                for (int i = 0; i < 30; i++)
                {
                    long heap = Profiler.GetMonoUsedSizeLong();
                    clock.Restart();
                    int actualCount = ReadAll(folder).Count;
                    report.warmMilliseconds.Add(clock.Elapsed.TotalMilliseconds);
                    Assert.That(actualCount, Is.EqualTo(records.Count));
                    report.heapDeltaBytes.Add(Profiler.GetMonoUsedSizeLong() - heap);
                    clock.Restart();
                    Component selected = ResolveOrNull(records[0]);
                    using SerializedObject serialized = new(selected);
                    serialized.Update();
                    report.selectedComponentMilliseconds.Add(clock.Elapsed.TotalMilliseconds);
                }
                report.contentsScenesBefore = EditorSceneManager.previewSceneCount;
                report.contentsGameObjectsBefore = Resources.FindObjectsOfTypeAll<GameObject>().Length;
                for (int i = 0; i < 5; i++)
                {
                    clock.Restart();
                    using ReadOnlyPrefabContentsScope contents = new(folder + "/Nested.prefab");
                    Assert.That(contents.Root.GetComponentsInChildren<BoxCollider>(true).Length, Is.EqualTo(4));
                    report.isolatedContentsMilliseconds.Add(clock.Elapsed.TotalMilliseconds);
                }
                report.contentsScenesAfter = EditorSceneManager.previewSceneCount;
                report.contentsGameObjectsAfter = Resources.FindObjectsOfTypeAll<GameObject>().Length;
                foreach (FieldInfo field in typeof(PrefabComponentMetadataRecord).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (typeof(Object).IsAssignableFrom(field.FieldType))
                    {
                        report.retainedObjectReferenceFields++;
                    }
                }
                string regular = folder + "/Regular.prefab";
                string source = File.ReadAllText(regular);
                Assert.That(source.Contains("m_IsTrigger: 0", StringComparison.Ordinal), Is.True);
                for (int i = 0; i < 30; i++)
                {
                    File.WriteAllText(regular, i % 2 == 0 ? source.Replace("m_IsTrigger: 0", "m_IsTrigger: 1") : source);
                    clock.Restart();
                    AssetDatabase.ImportAsset(regular, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    report.changedPrefabImportMilliseconds.Add(clock.Elapsed.TotalMilliseconds);
                    clock.Restart();
                    int changedCount = ReadMetadata(regular).Count;
                    report.changedPrefabMilliseconds.Add(clock.Elapsed.TotalMilliseconds);
                    Assert.That(changedCount, Is.EqualTo(3));
                }
            }
            report.cleanupComplete = !AssetDatabase.IsValidFolder(report.fixtureRoot);
            Assert.That(report.cleanupComplete, Is.True);
            Assert.That(report.retainedObjectReferenceFields, Is.Zero);
            Assert.That(report.contentsScenesAfter, Is.EqualTo(report.contentsScenesBefore));
            Assert.That(report.contentsGameObjectsAfter, Is.EqualTo(report.contentsGameObjectsBefore));
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
            return outputPath;
        }

        public static void RunColdFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            int folderIndex = Array.IndexOf(args, "-probeFolder");
            int outputIndex = Array.IndexOf(args, "-probeOutput");
            int revisionIndex = Array.IndexOf(args, "-probeRevision");
            if (folderIndex < 0 || outputIndex < 0 || revisionIndex < 0 || args.Length <= Math.Max(revisionIndex, Math.Max(folderIndex, outputIndex)) + 1)
            {
                throw new ArgumentException("Specify -probeFolder, -probeOutput and -probeRevision.");
            }
            PrefabFeasibilityReport report = NewReport(args[revisionIndex + 1], "Fresh editor process; OS filesystem cache and Library remain warm after the first process");
            report.fixtureRoot = args[folderIndex + 1];
            Stopwatch clock = Stopwatch.StartNew();
            var records = ReadAll(report.fixtureRoot);
            report.firstDiscoveryMilliseconds = clock.Elapsed.TotalMilliseconds;
            Assert.That(records.Count, Is.EqualTo(17));
            report.recordCount = records.Count;
            report.missingScriptCount = records.FindAll(record => record.status == "missing_script").Count;
            File.WriteAllText(args[outputIndex + 1], JsonUtility.ToJson(report, true));
        }

        private static PrefabFeasibilityReport NewReport(string revision, string mode)
        {
            return new PrefabFeasibilityReport
            {
                revision = revision,
                measuredUtc = DateTime.UtcNow.ToString("O"),
                unity = Application.unityVersion,
                os = SystemInfo.operatingSystem,
                cpu = SystemInfo.processorType,
                graphics = SystemInfo.graphicsDeviceName,
                dpi = Screen.dpi,
                pixelsPerPoint = EditorGUIUtility.pixelsPerPoint,
                mode = mode,
            };
        }

        private static string CreateFolder(TestCleanupScope cleanup)
        {
            string folder = "Assets/PrefabFeasibility-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            return folder;
        }

        private static List<PrefabComponentMetadataRecord> ReadAll(string folder)
        {
            List<PrefabComponentMetadataRecord> result = new();
            foreach (string name in new[] { "Regular", "Variant", "Nested", "Missing" })
            {
                result.AddRange(ReadMetadata(folder + "/" + name + ".prefab"));
            }
            return result;
        }

        [Test]
        public void ShouldResolveDistinctPrefabComponentIdentitiesAndKeepReadOnlyInspectionUnchanged()
        {
            using TestCleanupScope cleanup = new();
            Scene original = SceneManager.GetActiveScene();
            bool dirty = original.isDirty;
            int roots = original.rootCount;
            string folder = CreateFixtures(CreateFolder(cleanup));
            Assert.That(original.isDirty, Is.EqualTo(dirty));
            Assert.That(original.rootCount, Is.EqualTo(roots));
            EditorSurfaceCaptureHostWindow host = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(host));
            EditorSurfaceCapture.ShowPopup(host);
            foreach (string name in new[] { "Regular", "Variant", "Nested" })
            {
                string path = folder + "/" + name + ".prefab";
                byte[] before = File.ReadAllBytes(path);
                var records = ReadMetadata(path);
                Assert.That(records.Count, Is.EqualTo(name == "Nested" ? 7 : 3));
                HashSet<string> identities = new(StringComparer.Ordinal);
                foreach (PrefabComponentMetadataRecord entry in records)
                {
                    Assert.That(identities.Add(entry.globalId), Is.True, name + " identity collision");
                    Component component = ResolveOrNull(entry);
                    Assert.That(component != null);
                    using SerializedObject serialized = new(component);
                    using TestCleanupScope bindingCleanup = new();
                    VisualElement inspector = SubassetFeasibilityTests.CreateReadOnlyInspector(serialized);
                    bindingCleanup.Defer(inspector.Unbind);
                    host.rootVisualElement.Clear();
                    host.rootVisualElement.Add(inspector);
                    EditorSurfaceCapture.SettleLayout(host);
                    foreach (PropertyField field in inspector.Query<PropertyField>().ToList())
                    {
                        Assert.That(field.enabledInHierarchy, Is.False);
                    }
                    Assert.That(inspector.enabledSelf, Is.False);
                    Assert.That(EditorUtility.IsDirty(component), Is.False);
                }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                List<PrefabComponentMetadataRecord> refreshed = ReadMetadata(path);
                Assert.That(refreshed.Count, Is.EqualTo(records.Count));
                foreach (PrefabComponentMetadataRecord entry in refreshed)
                {
                    Assert.That(identities.Contains(entry.globalId), Is.True);
                }
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            }
            List<PrefabComponentMetadataRecord> selected = ReadMetadata(folder + "/Nested.prefab");
            string moved = folder + "/Moved.prefab";
            Assert.That(AssetDatabase.MoveAsset(folder + "/Nested.prefab", moved), Is.Empty);
            foreach (PrefabComponentMetadataRecord entry in selected)
            {
                Assert.That(ResolveOrNull(entry) != null);
            }
            Assert.That(AssetDatabase.DeleteAsset(moved), Is.True);
            foreach (PrefabComponentMetadataRecord entry in selected)
            {
                Assert.That(ResolveOrNull(entry) == null);
            }
            Assert.That(ReadMetadata(folder + "/Missing.prefab").FindAll(record => record.status == "missing_script").Count, Is.EqualTo(1));
            Assert.That(PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/Model.obj")), Is.EqualTo(PrefabAssetType.Model));
            Assert.That(ReadMetadata(folder + "/Model.obj"), Is.Empty);
        }

        [Test]
        public void ShouldUnloadContentsWithoutSavingAndAllowRepeatedDisposal()
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFixtures(CreateFolder(cleanup));
            string path = folder + "/Variant.prefab";
            byte[] before = File.ReadAllBytes(path);
            int scenes = EditorSceneManager.previewSceneCount;
            ReadOnlyPrefabContentsScope scope = new(path);
            GameObject loadedRoot = scope.Root;
            using (scope)
            {
                Assert.That(scope.Root.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(2, 3, 4)));
                scope.Root.name = "Unsaved preview change";
            }
            scope.Dispose();
            Assert.That(scope.Root == null);
            Assert.That(loadedRoot == null);
            Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(scenes));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }
    }
}
