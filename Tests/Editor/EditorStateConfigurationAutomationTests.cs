namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Styles;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorStateConfigurationAutomationTests
    {
        private static (DataVisualizerSettings settings, string assetPath, string statePath) CreateContext(TestCleanupScope cleanup, bool projectMode)
        {
            string folder = "Assets/StateConfigurationJson-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() =>
            {
                if (settings != null && !AssetDatabase.Contains(settings))
                {
                    UnityEngine.Object.DestroyImmediate(settings);
                }
            });
            string assetPath = folder + "/Settings.asset";
            DataVisualizerUserState project = SettingsPersistenceTests.CreatePopulatedUserState();
            project.namespaceOrder.Add("Project");
            project.savedSearches.Add(new SavedAssetSearch { name = "Saved" });
            settings.HydrateFrom(project);
            settings.persistStateInSettingsAsset = projectMode;
            AssetDatabase.CreateAsset(settings, assetPath);
            AssetDatabase.SaveAssetIfDirty(settings);
            string statePath = Path.Combine(Path.GetTempPath(), "StateConfigurationJson-" + Guid.NewGuid().ToString("N") + ".json");
            cleanup.Defer(() => File.Delete(statePath));
            DataVisualizerUserState state = SettingsPersistenceTests.CreatePopulatedUserState();
            state.namespaceOrder.Add("User");
            state.savedSearches.Add(new SavedAssetSearch { name = "Saved" });
            File.WriteAllText(statePath, JsonUtility.ToJson(state, true));
            return (settings, assetPath, statePath);
        }

        private static EditorAutomationRequest Request(string assetPath, string statePath, bool projectMode)
        {
            return new EditorAutomationRequest
            {
                schemaVersion = 1,
                operation = "previewConfiguration",
                settingsGuid = AssetDatabase.AssetPathToGUID(assetPath),
                userStatePath = statePath,
                dataFolderPath = "Assets",
                selectionMode = "select",
                persistenceMode = projectMode ? "settings" : "user",
                themeGuid = string.Empty,
            };
        }

        private static EditorAutomationResponse Execute(EditorAutomationRequest request)
        {
            using EditorAutomationSession session = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            Assert.That(session.IsFinished, Is.True);
            return JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson());
        }

        private static string Snapshot(DataVisualizerSettings settings)
        {
            DataVisualizerUserState state = new();
            state.HydrateFrom(settings);
            return JsonUtility.ToJson(state);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldPreviewAndApplyBothMigrationDirectionsWithCSharpParity(bool projectMode)
        {
            using TestCleanupScope cleanup = new();
            var json = CreateContext(cleanup, projectMode);
            var direct = CreateContext(cleanup, projectMode);
            byte[] assetBytes = File.ReadAllBytes(json.assetPath);
            byte[] userBytes = File.ReadAllBytes(json.statePath);
            EditorAutomationResponse read = Execute(
                new EditorAutomationRequest
                {
                    schemaVersion = 1,
                    operation = "readConfiguration",
                    settingsGuid = AssetDatabase.AssetPathToGUID(json.assetPath),
                    userStatePath = json.statePath,
                }
            );
            Assert.That(read.items[0].persistenceMode, Is.EqualTo(projectMode ? "settings" : "user"));
            EditorAutomationRequest request = Request(json.assetPath, json.statePath, !projectMode);
            request.themeGuid = projectMode ? DataVisualizerThemeSelection.DefaultThemeGuid.ToUpperInvariant() : string.Empty;
            EditorAutomationResponse preview = Execute(request);
            Assert.That(preview.code, Is.EqualTo("preview"));
            Assert.That(preview.items[0].userStatePath, Is.EqualTo(Path.GetFullPath(json.statePath)));
            Assert.That(File.ReadAllBytes(json.assetPath), Is.EqualTo(assetBytes));
            Assert.That(File.ReadAllBytes(json.statePath), Is.EqualTo(userBytes));
            Assert.That(EditorUtility.IsDirty(json.settings), Is.False);
            request.operation = "applyConfiguration";
            request.confirmPaths = new[] { preview.items[0].path, preview.items[0].userStatePath };
            Assert.That(Execute(request).code, Is.EqualTo("applied"));
            EditorConfigurationService service = EditorConfigurationService.FromGuid(AssetDatabase.AssetPathToGUID(direct.assetPath), direct.statePath);
            Assert.That(service.Apply("Assets", true, !projectMode, request.themeGuid), Is.True);
            Assert.That(Snapshot(json.settings), Is.EqualTo(Snapshot(direct.settings)));
            Assert.That(File.ReadAllText(json.statePath), Is.EqualTo(File.ReadAllText(direct.statePath)));
            Assert.That(json.settings.persistStateInSettingsAsset, Is.EqualTo(!projectMode));
            byte[] savedAsset = File.ReadAllBytes(json.assetPath);
            byte[] savedUser = File.ReadAllBytes(json.statePath);
            using EditorAutomationSession repeated = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            repeated.Cancel();
            repeated.Dispose();
            EditorAutomationResponse unchanged = JsonUtility.FromJson<EditorAutomationResponse>(repeated.GetResponseJson());
            Assert.That(unchanged.code, Is.EqualTo("unchanged"));
            Assert.That(unchanged.items[0].persistenceMode, Is.EqualTo(request.persistenceMode));
            Assert.That(unchanged.items[0].themeGuid, Is.EqualTo(request.themeGuid.ToLowerInvariant()));
            Assert.That(File.ReadAllBytes(json.assetPath), Is.EqualTo(savedAsset));
            Assert.That(File.ReadAllBytes(json.statePath), Is.EqualTo(savedUser));
            Assert.That(JsonUtility.FromJson<EditorAutomationResponse>(repeated.GetResponseJson(1, 1)).items, Is.Empty);
        }

        [TestCase("mode")]
        [TestCase("theme")]
        [TestCase("missing-theme")]
        [TestCase("missing-path")]
        [TestCase("protected-path")]
        [TestCase("confirm")]
        [TestCase("order")]
        [TestCase("read-fields")]
        [TestCase("filter")]
        [TestCase("corrupt-state")]
        [TestCase("missing-parent")]
        public void ShouldRejectInvalidStateRequestsBeforeChangingEitherStore(string scenario)
        {
            using TestCleanupScope cleanup = new();
            var context = CreateContext(cleanup, false);
            if (scenario == "corrupt-state")
            {
                File.WriteAllText(context.statePath, "{");
            }
            byte[] asset = File.ReadAllBytes(context.assetPath);
            byte[] state = File.ReadAllBytes(context.statePath);
            EditorAutomationRequest request = Request(context.assetPath, context.statePath, true);
            if (scenario == "mode")
            {
                request.persistenceMode = "invalid";
            }
            if (scenario == "theme")
            {
                request.themeGuid = "invalid";
            }
            if (scenario == "missing-path")
            {
                request.userStatePath = string.Empty;
            }
            if (scenario == "missing-parent")
            {
                request.userStatePath = Path.Combine(context.statePath, "missing", "State.json");
            }
            if (scenario == "protected-path")
            {
                request.userStatePath = "Assets/State.json";
            }
            if (scenario == "read-fields")
            {
                request.operation = "readConfiguration";
            }
            if (scenario == "filter")
            {
                request.text = "invalid";
            }
            if (scenario == "confirm" || scenario == "order")
            {
                request.operation = "applyConfiguration";
                request.confirmPaths = scenario == "confirm" ? new[] { context.assetPath } : new[] { context.statePath, context.assetPath };
            }
            string payload = JsonUtility.ToJson(request);
            if (scenario == "missing-theme")
            {
                payload = payload.Replace(",\"themeGuid\":\"\"", string.Empty);
            }
            using EditorAutomationSession session = EditorAutomationSession.Start(payload);
            EditorAutomationResponse response = JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson());
            Assert.That(response.status, Is.EqualTo("rejected"));
            Assert.That(response.code, Is.EqualTo("invalid_scope"));
            Assert.That(File.ReadAllBytes(context.assetPath), Is.EqualTo(asset));
            Assert.That(File.ReadAllBytes(context.statePath), Is.EqualTo(state));
            Assert.That(EditorUtility.IsDirty(context.settings), Is.False);
        }

        [TestCase("query")]
        [TestCase("catalog")]
        [TestCase("previewAssets")]
        public void ShouldRejectStateFieldsInOtherOperations(string operation)
        {
            using EditorAutomationSession session = EditorAutomationSession.Start("{\"schemaVersion\":1,\"operation\":\"" + operation + "\",\"persistenceMode\":\"settings\"}");
            Assert.That(JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson()).code, Is.EqualTo("invalid_scope"));
        }

        [Test]
        public void ShouldReadAndPreviewMissingUserStateWithoutCreatingIt()
        {
            using TestCleanupScope cleanup = new();
            var context = CreateContext(cleanup, false);
            File.Delete(context.statePath);
            byte[] before = File.ReadAllBytes(context.assetPath);
            EditorAutomationRequest request = Request(context.assetPath, context.statePath, true);
            Assert.That(Execute(request).code, Is.EqualTo("preview"));
            request = new EditorAutomationRequest
            {
                schemaVersion = 1,
                operation = "readConfiguration",
                settingsGuid = request.settingsGuid,
                userStatePath = context.statePath,
            };
            Assert.That(Execute(request).items[0].themeGuid, Is.Empty);
            Assert.That(File.Exists(context.statePath), Is.False);
            Assert.That(File.ReadAllBytes(context.assetPath), Is.EqualTo(before));
        }

        [TestCase(true, 1, false, false)]
        [TestCase(false, 1, false, false)]
        [TestCase(true, 2, false, false)]
        [TestCase(false, 2, true, false)]
        [TestCase(true, 1, false, true)]
        public void ShouldRejectFileCollisionsBeforeEvenWritingRejectionResponses(bool inputCollision, int version, bool varyCase, bool invalidExtension)
        {
            using TestCleanupScope cleanup = new();
            var context = CreateContext(cleanup, false);
            string input =
                invalidExtension ? context.statePath + ".request.txt"
                : inputCollision ? context.statePath
                : context.statePath + ".request.json";
            string output =
                inputCollision ? context.statePath + ".response.json"
                : varyCase ? context.statePath.ToUpperInvariant()
                : context.statePath;
            cleanup.Defer(() => File.Delete(input));
            if (inputCollision)
            {
                cleanup.Defer(() => File.Delete(output));
            }
            EditorAutomationRequest request = Request(context.assetPath, context.statePath, true);
            request.schemaVersion = version;
            if (invalidExtension)
            {
                request.userStatePath = input;
            }
            File.WriteAllText(input, JsonUtility.ToJson(request));
            byte[] requestBytes = File.ReadAllBytes(input);
            byte[] before = File.ReadAllBytes(context.statePath);
            byte[] asset = File.ReadAllBytes(context.assetPath);
            Assert.Throws<ArgumentException>(() => EditorAutomation.RunFiles(input, output));
            Assert.That(File.ReadAllBytes(input), Is.EqualTo(requestBytes));
            Assert.That(File.ReadAllBytes(context.statePath), Is.EqualTo(before));
            Assert.That(File.ReadAllBytes(context.assetPath), Is.EqualTo(asset));
            if (inputCollision)
            {
                Assert.That(File.Exists(output), Is.False);
            }
        }

        [Test]
        public void ShouldReportUnknownOutcomeWhenAStoreWriteFailsAfterPreview()
        {
            using TestCleanupScope cleanup = new();
            var context = CreateContext(cleanup, true);
            EditorAutomationRequest request = Request(context.assetPath, context.statePath, false);
            EditorAutomationResponse preview = Execute(request);
            request.operation = "applyConfiguration";
            request.confirmPaths = new[] { preview.items[0].path, preview.items[0].userStatePath };
            byte[] settingsBytes = File.ReadAllBytes(context.assetPath);
            byte[] userBytes = File.ReadAllBytes(context.statePath);
            using FileStream readLease = new(context.statePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            EditorAutomationResponse response = Execute(request);
            Assert.That(response.status, Is.EqualTo("failed"));
            Assert.That(response.code, Is.EqualTo("configuration_failed"));
            Assert.That(response.complete, Is.False);
            Assert.That(response.items[0].code, Is.EqualTo("outcome_unknown"));
            Assert.That(string.IsNullOrEmpty(response.items[0].persistenceMode), Is.True);
            Assert.That(string.IsNullOrEmpty(response.items[0].themeGuid), Is.True);
            Assert.That(File.ReadAllBytes(context.assetPath), Is.EqualTo(settingsBytes));
            Assert.That(File.ReadAllBytes(context.statePath), Is.EqualTo(userBytes));
        }

        [Test]
        public void ShouldReturnReadonlyRejectionWithoutPartialMigration()
        {
            using TestCleanupScope cleanup = new();
            var context = CreateContext(cleanup, true);
            FileAttributes attributes = File.GetAttributes(context.statePath);
            cleanup.Defer(() => File.SetAttributes(context.statePath, attributes));
            File.SetAttributes(context.statePath, attributes | FileAttributes.ReadOnly);
            byte[] before = File.ReadAllBytes(context.assetPath);
            EditorAutomationRequest request = Request(context.assetPath, context.statePath, false);
            request.operation = "applyConfiguration";
            request.confirmPaths = new[] { context.assetPath, context.statePath };
            Assert.That(Execute(request).code, Is.EqualTo("read_only"));
            Assert.That(context.settings.persistStateInSettingsAsset, Is.True);
            Assert.That(File.ReadAllBytes(context.assetPath), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(context.settings), Is.False);
        }
    }
}
