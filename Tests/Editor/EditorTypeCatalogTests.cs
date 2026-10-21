namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorTypeCatalogTests
    {
        [TestCase(null, false)]
        [TestCase(typeof(ScriptFolderProbeBehaviour), false)]
        [TestCase(typeof(EditorTypeCatalogTests), false)]
        [TestCase(typeof(DateTime), false)]
        [TestCase(typeof(ScriptableObject), false)]
        [TestCase(typeof(EditorWindow), false)]
        [TestCase(typeof(UnityEditor.Editor), false)]
        [TestCase(typeof(AbstractData), false)]
        [TestCase(typeof(GenericData<>), false)]
        [TestCase(typeof(PrivateData), false)]
        [TestCase(typeof(OperationLifecycleData), true)]
        [TestCase(typeof(OtherEditorOnlyCreationData), true)]
        [TestCase(typeof(LayoutTestWindow), false)]
        public void ShouldRequireSupportedScriptableObjectTypes(Type type, bool expected)
        {
            Assert.That(EditorTypeCatalog.IsLoadableType(type), Is.EqualTo(expected));
        }

        [Test]
        public void ShouldScanActualScriptsWithoutAddingBehavioursOrCreatingObjects()
        {
            string[] guids = AssetDatabase.FindAssets($"t:MonoScript {nameof(ScriptFolderProbeBehaviour)}");
            Assert.That(guids, Has.Length.EqualTo(1));
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            Assert.That(script.GetClass(), Is.EqualTo(typeof(ScriptFolderProbeBehaviour)));
            int before = Resources.FindObjectsOfTypeAll<OperationLifecycleData>().Length;
            int hooks = OperationLifecycleData.HookCount;
            List<Type> types = EditorTypeCatalog.FindTypesInFolder(Path.GetDirectoryName(path).Replace('\\', '/'), true);
            Assert.That(types, Has.Member(typeof(OperationLifecycleData)));
            Assert.That(types, Has.Member(typeof(OtherEditorOnlyCreationData)));
            Assert.That(types, Has.No.Member(typeof(ScriptFolderProbeBehaviour)));
            Assert.That(types, Has.No.Member(typeof(EditorTypeCatalogTests)));
            Assert.That(types, Is.Unique);
            foreach (Type type in types)
            {
                Assert.That(EditorTypeCatalog.IsLoadableType(type), Is.True);
            }
            Assert.That(Resources.FindObjectsOfTypeAll<OperationLifecycleData>().Length, Is.EqualTo(before));
            Assert.That(OperationLifecycleData.HookCount, Is.EqualTo(hooks));
        }

        [Test]
        public void ShouldDiscoverDistinctOrdinaryTypesFromActualAssetFolders()
        {
            using TestCleanupScope cleanup = new();
            string folder = "Assets/CatalogFolderTests-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            Assert.That(EditorTypeCatalog.FindTypesInFolder(folder, false), Is.Empty);
            foreach (string name in new[] { "First", "Second" })
            {
                OtherEditorOnlyCreationData asset = ScriptableObject.CreateInstance<OtherEditorOnlyCreationData>();
                cleanup.Defer(() =>
                {
                    if (asset != null && !EditorUtility.IsPersistent(asset))
                    {
                        UnityEngine.Object.DestroyImmediate(asset);
                    }
                });
                AssetDatabase.CreateAsset(asset, folder + "/" + name + ".asset");
            }
            Assert.That(EditorTypeCatalog.FindTypesInFolder(folder, false), Is.EqualTo(new[] { typeof(OtherEditorOnlyCreationData) }));
            Assert.That(EditorTypeCatalog.FindTypesInFolder(folder.ToUpperInvariant(), false), Is.EqualTo(new[] { typeof(OtherEditorOnlyCreationData) }));
            Assert.That(EditorTypeCatalog.FindTypesInFolder(folder, true), Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ShouldIgnoreUnimportedPickerFolders(bool scripts)
        {
            using TestCleanupScope cleanup = new();
            string folder = "Assets/UnimportedCatalogFolder-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() =>
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
                AssetDatabase.DeleteAsset(folder);
            });
            Directory.CreateDirectory(folder);
            Assert.That(AssetDatabase.IsValidFolder(folder), Is.False);
            Assert.That(EditorTypeCatalog.FindTypesInFolder(folder, scripts), Is.Empty);
            Assert.That(AssetDatabase.IsValidFolder(folder), Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ShouldIgnoreMissingScanFolders(bool scripts)
        {
            Assert.That(EditorTypeCatalog.FindTypesInFolder("Assets/DoesNotExist-" + Guid.NewGuid().ToString("N"), scripts), Is.Empty);
            Assert.That(EditorTypeCatalog.FindTypesInFolder(null, scripts), Is.Empty);
            Assert.That(EditorTypeCatalog.FindTypesInFolder(string.Empty, scripts), Is.Empty);
        }

        [Test]
        public void ShouldDiscoverExactTypesWithoutLoadingObjectsOrChangingHooks()
        {
            int before = Resources.FindObjectsOfTypeAll<OperationLifecycleData>().Length;
            int hooks = OperationLifecycleData.HookCount;
            EditorTypeCatalog catalog = new();
            Assert.That(catalog.Types, Has.Member(typeof(OperationLifecycleData)));
            Assert.That(catalog.Types, Has.No.Member(typeof(ScriptableObject)));
            Assert.That(catalog.Types, Has.No.Member(typeof(EditorWindow)));
            Assert.That(catalog.Types, Has.No.Member(typeof(AbstractData)));
            Assert.That(catalog.Types, Has.No.Member(typeof(GenericData<>)));
            Assert.That(catalog.Types, Has.No.Member(typeof(PrivateData)));
            HashSet<string> identities = new(StringComparer.Ordinal);
            for (int i = 0; i < catalog.Types.Count; i++)
            {
                Type type = catalog.Types[i];
                Assert.That(typeof(ScriptableObject).IsAssignableFrom(type), Is.True);
                Assert.That(identities.Add(type.AssemblyQualifiedName), Is.True);
                Assert.That(Type.GetType(type.AssemblyQualifiedName, false), Is.EqualTo(type));
                if (0 < i)
                {
                    Assert.That(StringComparer.Ordinal.Compare(catalog.Types[i - 1].AssemblyQualifiedName, type.AssemblyQualifiedName), Is.LessThan(0));
                }
            }
            Assert.That(Resources.FindObjectsOfTypeAll<OperationLifecycleData>().Length, Is.EqualTo(before));
            Assert.That(OperationLifecycleData.HookCount, Is.EqualTo(hooks));
        }

        [Test]
        public void ShouldPreserveImmutableSnapshotsAndIndependentPagesAcrossCultures()
        {
            using TestCleanupScope cleanup = new();
            CultureInfo prior = CultureInfo.CurrentCulture;
            cleanup.Defer(() => CultureInfo.CurrentCulture = prior);
            EditorTypeCatalog catalog = new();
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            EditorTypeCatalog second = new();
            Assert.That(second.Types, Is.EqualTo(catalog.Types));
            Assert.Throws<NotSupportedException>(() => ((IList<Type>)catalog.Types).Clear());
            Type[] page = catalog.GetPage(0, 1);
            Assert.That(page[0], Is.EqualTo(catalog.Types[0]));
            page[0] = typeof(string);
            Assert.That(catalog.GetPage(0, 1)[0], Is.EqualTo(catalog.Types[0]));
            Assert.That(catalog.GetPage(catalog.Types.Count - 1, 1000), Has.Length.EqualTo(1));
            Assert.That(catalog.GetPage(int.MaxValue, 1000), Is.Empty);
        }

        [TestCase(-1, 1)]
        [TestCase(0, 0)]
        [TestCase(0, 1001)]
        public void ShouldRejectInvalidPageBounds(int offset, int count)
        {
            EditorTypeCatalog catalog = new();
            Assert.Throws<ArgumentOutOfRangeException>(() => catalog.GetPage(offset, count));
        }

        [Test]
        public void ShouldReturnEquivalentJsonPagesAfterCompletionAndDisposal()
        {
            EditorTypeCatalog catalog = new();
            using EditorAutomationSession session = EditorAutomationSession.Start("{\"schemaVersion\":1,\"operation\":\"catalog\",\"offset\":1,\"pageSize\":2}");
            Assert.That(session.IsFinished, Is.True);
            session.Advance(0);
            session.Cancel();
            session.Dispose();
            EditorAutomationResponse response = JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson());
            Assert.That(response.status, Is.EqualTo("complete"));
            Assert.That(response.complete, Is.True);
            Assert.That(response.availableCount, Is.EqualTo(catalog.Types.Count));
            Assert.That(response.scopeCount, Is.EqualTo(catalog.Types.Count));
            Assert.That(response.processedCount, Is.EqualTo(catalog.Types.Count));
            Assert.That(response.offset, Is.EqualTo(1));
            Type[] page = catalog.GetPage(1, 2);
            Assert.That(response.items, Has.Length.EqualTo(page.Length));
            for (int i = 0; i < page.Length; i++)
            {
                Assert.That(response.items[i].typeIdentity, Is.EqualTo(page[i].AssemblyQualifiedName));
                Assert.That(response.items[i].name, Is.EqualTo(page[i].FullName));
                Assert.That(response.items[i].code, Is.EqualTo("ok"));
                Assert.That(string.IsNullOrEmpty(response.items[i].guid), Is.True);
                Assert.That(string.IsNullOrEmpty(response.items[i].path), Is.True);
            }
            EditorAutomationResponse empty = JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson(int.MaxValue, 1));
            Assert.That(empty.items, Is.Empty);
            Assert.That(empty.complete, Is.True);
        }

        [TestCase("typeIdentities", "[\"ignored\"]")]
        [TestCase("assetGuids", "[\"0123456789abcdef0123456789abcdef\"]")]
        [TestCase("processorIdentity", "\"ignored\"")]
        [TestCase("confirmPaths", "[\"Assets/ignored.asset\"]")]
        [TestCase("text", "\"ignored\"")]
        [TestCase("andLabels", "[\"ignored\"]")]
        [TestCase("orLabels", "[\"ignored\"]")]
        [TestCase("labelCombination", "\"and\"")]
        [TestCase("settingsGuid", "\"0123456789abcdef0123456789abcdef\"")]
        [TestCase("dataFolderPath", "\"Assets\"")]
        [TestCase("selectionMode", "\"select\"")]
        [TestCase("assetOperation", "\"delete\"")]
        [TestCase("destinationPaths", "[\"Assets/ignored.asset\"]")]
        public void ShouldRejectMixedCatalogFields(string field, string value)
        {
            string json = "{\"schemaVersion\":1,\"operation\":\"catalog\",\"" + field + "\":" + value + "}";
            using EditorAutomationSession session = EditorAutomationSession.Start(json);
            EditorAutomationResponse response = JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson());
            Assert.That(response.status, Is.EqualTo("rejected"));
            Assert.That(response.code, Is.EqualTo("invalid_scope"));
            Assert.That(response.items, Is.Empty);
        }

        public abstract class AbstractData : ScriptableObject { }

        public sealed class GenericData<T> : ScriptableObject { }

        private sealed class PrivateData : ScriptableObject { }
    }
}
