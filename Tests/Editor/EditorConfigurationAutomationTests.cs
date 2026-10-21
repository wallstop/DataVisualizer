namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorConfigurationAutomationTests
    {
        private static DataVisualizerSettings CreateSettings(TestCleanupScope cleanup)
        {
            string folder = "Assets/ConfigurationJson-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            AssetDatabase.CreateAsset(settings, folder + "/Settings.asset");
            AssetDatabase.SaveAssetIfDirty(settings);
            return settings;
        }

        private static EditorAutomationRequest Request(DataVisualizerSettings settings, string operation)
        {
            return new EditorAutomationRequest
            {
                schemaVersion = 1,
                operation = operation,
                settingsGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(settings)),
                dataFolderPath = "Assets",
                selectionMode = "select",
            };
        }

        private static EditorAutomationResponse Execute(EditorAutomationRequest request)
        {
            using EditorAutomationSession session = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            Assert.That(session.IsFinished, Is.True, "Configuration is a single synchronous unit.");
            session.Cancel();
            return JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson());
        }

        [Test]
        public void ShouldPreviewWithoutMutationAndApplyTheSamePreferencesAsCSharp()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerSettings settings = CreateSettings(cleanup);
            string path = AssetDatabase.GetAssetPath(settings);
            byte[] before = File.ReadAllBytes(path);
            EditorAutomationRequest request = Request(settings, "previewConfiguration");
            EditorAutomationResponse preview = Execute(request);
            Assert.That(preview.code, Is.EqualTo("preview"));
            Assert.That(preview.items[0].guid, Is.EqualTo(request.settingsGuid));
            Assert.That(preview.items[0].path, Is.EqualTo(path));
            Assert.That(preview.items[0].dataFolderPath, Is.EqualTo("Assets"));
            Assert.That(preview.items[0].selectionMode, Is.EqualTo("select"));
            Assert.That(preview.processedCount, Is.Zero);
            Assert.That(settings.selectActiveObject, Is.False);
            Assert.That(EditorUtility.IsDirty(settings), Is.False);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));

            request.operation = "applyConfiguration";
            request.confirmPaths = new[] { preview.items[0].path };
            EditorAutomationResponse applied = Execute(request);
            EditorConfigurationService direct = EditorConfigurationService.FromGuid(request.settingsGuid);
            Assert.That(applied.code, Is.EqualTo("applied"));
            Assert.That(applied.complete, Is.True);
            Assert.That(applied.processedCount, Is.EqualTo(1));
            Assert.That(applied.items[0].dataFolderPath, Is.EqualTo(direct.DataFolderPath));
            Assert.That(applied.items[0].selectionMode, Is.EqualTo(direct.SelectActiveObject ? "select" : "keep"));
            Assert.That(settings.selectActiveObject, Is.True);
            Assert.That(File.ReadAllBytes(path), Is.Not.EqualTo(before));

            byte[] saved = File.ReadAllBytes(path);
            settings.persistStateInSettingsAsset = true;
            EditorUtility.SetDirty(settings);
            Assert.That(Execute(request).code, Is.EqualTo("unchanged"));
            Assert.That(EditorUtility.IsDirty(settings), Is.True);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(saved));
            request = new EditorAutomationRequest
            {
                schemaVersion = 1,
                operation = "readConfiguration",
                settingsGuid = request.settingsGuid,
            };
            EditorAutomationResponse read = Execute(request);
            Assert.That(read.code, Is.EqualTo("ok"));
            Assert.That(read.items[0].dataFolderPath, Is.EqualTo(direct.DataFolderPath));
            Assert.That(read.items[0].selectionMode, Is.EqualTo("select"));
        }

        [TestCase("missing-guid", "invalid_scope")]
        [TestCase("wrong-kind", "invalid_scope")]
        [TestCase("missing-selection", "invalid_scope")]
        [TestCase("invalid-selection", "invalid_scope")]
        [TestCase("traversal-folder", "invalid_scope")]
        [TestCase("unconfirmed", "invalid_scope")]
        [TestCase("stale-path", "invalid_scope")]
        [TestCase("mixed-query", "invalid_scope")]
        [TestCase("preview-confirmation", "invalid_scope")]
        [TestCase("read-write-fields", "invalid_scope")]
        [TestCase("other-operation-config", "invalid_scope")]
        [TestCase("offset", "invalid_page")]
        public void ShouldRejectInvalidConfigurationBeforeChangingPreferences(string scenario, string expectedCode)
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerSettings settings = CreateSettings(cleanup);
            string path = AssetDatabase.GetAssetPath(settings);
            EditorAutomationRequest request = Request(settings, "applyConfiguration");
            request.confirmPaths = new[] { path };
            switch (scenario)
            {
                case "missing-guid":
                    request.settingsGuid = null;
                    break;
                case "wrong-kind":
                    request.settingsGuid = AssetDatabase.AssetPathToGUID(path.Substring(0, path.LastIndexOf('/')));
                    break;
                case "missing-selection":
                    request.selectionMode = null;
                    break;
                case "invalid-selection":
                    request.selectionMode = "false";
                    break;
                case "traversal-folder":
                    request.dataFolderPath = "Assets/../Assets";
                    break;
                case "unconfirmed":
                    request.confirmPaths = null;
                    break;
                case "stale-path":
                    request.confirmPaths = new[] { "Assets/OldSettings.asset" };
                    break;
                case "mixed-query":
                    request.text = "ignored filter";
                    break;
                case "preview-confirmation":
                    request.operation = "previewConfiguration";
                    break;
                case "read-write-fields":
                    request.operation = "readConfiguration";
                    break;
                case "other-operation-config":
                    request.operation = "query";
                    break;
                case "offset":
                    request.offset = 1;
                    break;
                default:
                    throw new ArgumentException(scenario);
            }
            byte[] before = File.ReadAllBytes(path);
            EditorAutomationResponse rejected = Execute(request);
            Assert.That(rejected.status, Is.EqualTo("rejected"));
            Assert.That(rejected.code, Is.EqualTo(expectedCode));
            Assert.That(rejected.items, Is.Empty);
            Assert.That(settings.selectActiveObject, Is.False);
            Assert.That(EditorUtility.IsDirty(settings), Is.False);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void ShouldPageCompletedConfigurationWithoutRepeatingTheApply()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerSettings settings = CreateSettings(cleanup);
            EditorAutomationRequest request = Request(settings, "applyConfiguration");
            request.confirmPaths = new[] { AssetDatabase.GetAssetPath(settings) };
            using EditorAutomationSession session = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            settings.persistStateInSettingsAsset = true;
            EditorUtility.SetDirty(settings);
            EditorAutomationResponse empty = JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson(1, 1));
            Assert.That(empty.offset, Is.EqualTo(1));
            Assert.That(empty.items, Is.Empty);
            Assert.That(empty.availableCount, Is.EqualTo(1));
            session.Advance();
            Assert.That(EditorUtility.IsDirty(settings), Is.True);
            Assert.That(JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson()).items.Length, Is.EqualTo(1));
        }

        [Test]
        public void ShouldApplyThroughFilesAndRejectUnsafeOutputBeforeMutation()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerSettings settings = CreateSettings(cleanup);
            string folder = Path.Combine("Temp", "ConfigurationJson-" + Guid.NewGuid().ToString("N"));
            cleanup.Defer(() => Directory.Delete(folder, true));
            Directory.CreateDirectory(folder);
            EditorAutomationRequest request = Request(settings, "applyConfiguration");
            request.confirmPaths = new[] { AssetDatabase.GetAssetPath(settings) };
            string input = Path.Combine(folder, "request.json");
            string output = Path.Combine(folder, "response.json");
            File.WriteAllText(input, JsonUtility.ToJson(request));
            Assert.Throws<ArgumentException>(() => EditorAutomation.RunFiles(input, "Assets/UnsafeConfig.json"));
            Assert.That(settings.selectActiveObject, Is.False);
            Assert.That(EditorAutomation.RunFiles(input, output), Is.Zero);
            Assert.That(JsonUtility.FromJson<EditorAutomationResponse>(File.ReadAllText(output)).code, Is.EqualTo("applied"));
            Assert.That(EditorAutomation.RunFiles(input, output), Is.Zero);
            Assert.That(JsonUtility.FromJson<EditorAutomationResponse>(File.ReadAllText(output)).code, Is.EqualTo("unchanged"));
        }
    }
}
