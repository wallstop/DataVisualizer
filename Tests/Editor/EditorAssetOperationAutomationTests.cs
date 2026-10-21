namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorAssetOperationAutomationTests
    {
        private static string Folder(TestCleanupScope cleanup)
        {
            string leaf = "AssetOperationJson-" + Guid.NewGuid().ToString("N");
            string folder = "Assets/" + leaf;
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            cleanup.Defer(() =>
            {
                OperationLifecycleData.Callback = null;
                OperationLifecycleData.ThrowAfterCreate = false;
                OperationLifecycleData.HookCount = 0;
            });
            AssetDatabase.CreateFolder("Assets", leaf);
            return folder;
        }

        private static string Source(string folder, string name)
        {
            OperationLifecycleData asset = ScriptableObject.CreateInstance<OperationLifecycleData>();
            string path = folder + "/" + name + ".asset";
            AssetDatabase.CreateAsset(asset, path);
            asset.AfterCreate();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return AssetDatabase.AssetPathToGUID(path);
        }

        private static EditorAutomationResponse Response(EditorAutomationSession session)
        {
            return JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson());
        }

        private static EditorAutomationRequest CreateRequest(string folder, int count = 1)
        {
            string[] paths = new string[count];
            for (int i = 0; i < count; i++)
            {
                paths[i] = folder + "/Result-" + i + ".asset";
            }
            return new EditorAutomationRequest
            {
                schemaVersion = 1,
                operation = "applyAssets",
                assetOperation = "create",
                typeIdentities = new[] { typeof(OperationLifecycleData).AssemblyQualifiedName },
                destinationPaths = paths,
                confirmPaths = paths,
            };
        }

        [TestCase(EditorAssetOperationKind.Create)]
        [TestCase(EditorAssetOperationKind.Clone)]
        [TestCase(EditorAssetOperationKind.Rename)]
        [TestCase(EditorAssetOperationKind.Move)]
        [TestCase(EditorAssetOperationKind.Delete)]
        public void ShouldMatchCSharpOutcomesAndKeepPreviewSideEffectFree(EditorAssetOperationKind kind)
        {
            using TestCleanupScope cleanup = new();
            string folder = Folder(cleanup);
            string directGuid = kind == EditorAssetOperationKind.Create ? null : Source(folder, "Direct");
            string jsonGuid = kind == EditorAssetOperationKind.Create ? null : Source(folder, "Json");
            string directDestination = folder + "/DirectResult.asset";
            string jsonDestination = folder + "/JsonResult.asset";
            string jsonSource = jsonGuid == null ? string.Empty : AssetDatabase.GUIDToAssetPath(jsonGuid);
            byte[] before = jsonGuid == null ? null : File.ReadAllBytes(jsonSource);
            OperationLifecycleData.HookCount = 0;
            var request = new EditorAutomationRequest
            {
                schemaVersion = 1,
                operation = "previewAssets",
                assetOperation = kind.ToString().ToLowerInvariant(),
                typeIdentities = new[] { typeof(OperationLifecycleData).AssemblyQualifiedName },
                assetGuids = jsonGuid == null ? null : new[] { jsonGuid },
                destinationPaths = kind == EditorAssetOperationKind.Delete ? null : new[] { jsonDestination },
            };
            List<string> confirmation = new();
            using (EditorAutomationSession preview = EditorAutomationSession.Start(JsonUtility.ToJson(request)))
            {
                EditorAutomationResponse response = Response(preview);
                Assert.That(response.code, Is.EqualTo("preview"));
                Assert.That(response.processedCount, Is.Zero);
                foreach (EditorAutomationItem item in response.items)
                {
                    if (!string.IsNullOrEmpty(item.path))
                    {
                        confirmation.Add(item.path);
                    }
                    if (!string.IsNullOrEmpty(item.destinationPath))
                    {
                        confirmation.Add(item.destinationPath);
                    }
                }
            }
            Assert.That(OperationLifecycleData.HookCount, Is.Zero);
            if (before != null)
            {
                Assert.That(File.ReadAllBytes(jsonSource), Is.EqualTo(before));
            }
            using EditorAssetOperation direct = new(
                kind,
                typeof(OperationLifecycleData),
                directGuid == null ? null : new[] { directGuid },
                kind == EditorAssetOperationKind.Delete ? null : new[] { directDestination }
            );
            direct.Apply();
            direct.Advance(0);
            request.operation = "applyAssets";
            request.confirmPaths = confirmation.ToArray();
            using EditorAutomationSession apply = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            Assert.That(apply.IsFinished, Is.False);
            apply.Advance(0);
            EditorAutomationResponse result = Response(apply);
            Assert.That(result.status, Is.EqualTo(direct.Status));
            Assert.That(result.items[0].code, Is.EqualTo(direct.Targets[0].Code));
            Assert.That(result.processedCount, Is.EqualTo(1));
            Assert.That(result.items[0].sourceGuid, Is.EqualTo(jsonGuid ?? string.Empty));
            Assert.That(result.items[0].path, Is.EqualTo(jsonSource));
            Assert.That(OperationLifecycleData.HookCount, Is.EqualTo(kind == EditorAssetOperationKind.Move || kind == EditorAssetOperationKind.Delete ? 0 : 4));
            if (kind == EditorAssetOperationKind.Delete)
            {
                Assert.That(File.Exists(jsonSource), Is.False);
            }
            else
            {
                OperationLifecycleData a = AssetDatabase.LoadMainAssetAtPath(directDestination) as OperationLifecycleData;
                OperationLifecycleData b = AssetDatabase.LoadMainAssetAtPath(jsonDestination) as OperationLifecycleData;
                Assert.That(a != null && b != null);
                if (kind == EditorAssetOperationKind.Rename)
                {
                    Assert.That(a.marker, Is.EqualTo("DirectResult"));
                    Assert.That(b.marker, Is.EqualTo("JsonResult"));
                }
                else
                {
                    Assert.That(b.marker, Is.EqualTo(a.marker));
                }
                Assert.That(result.items[0].guid, Is.EqualTo(AssetDatabase.AssetPathToGUID(jsonDestination)));
                Assert.That(EditorUtility.IsDirty(b), Is.False);
            }
        }

        [TestCase("confirmation")]
        [TestCase("changed-path")]
        [TestCase("filter")]
        [TestCase("kind")]
        [TestCase("type")]
        [TestCase("configuration")]
        public void ShouldRejectInvalidApplyWithoutCreatingAnAsset(string scenario)
        {
            using TestCleanupScope cleanup = new();
            string folder = Folder(cleanup);
            EditorAutomationRequest request = CreateRequest(folder);
            switch (scenario)
            {
                case "confirmation":
                    request.confirmPaths = null;
                    break;
                case "changed-path":
                    request.confirmPaths = new[] { "Assets/Wrong.asset" };
                    break;
                case "filter":
                    request.text = "ignored";
                    break;
                case "kind":
                    request.assetOperation = "touch";
                    break;
                case "type":
                    request.typeIdentities = new[] { "OperationLifecycleData" };
                    break;
                case "configuration":
                    request.selectionMode = "select";
                    break;
                default:
                    throw new ArgumentException(scenario);
            }
            using EditorAutomationSession session = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            session.Advance();
            Assert.That(Response(session).code, Is.EqualTo("invalid_scope"));
            Assert.That(File.Exists(request.destinationPaths[0]), Is.False);
        }

        [Test]
        public void ShouldPreserveCompletedOutcomesWhenCancelledBetweenUnits()
        {
            using TestCleanupScope cleanup = new();
            EditorAutomationRequest request = CreateRequest(Folder(cleanup), 2);
            using EditorAutomationSession session = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            session.Advance(0);
            session.Cancel("test_stop");
            EditorAutomationResponse response = Response(session);
            Assert.That(response.status, Is.EqualTo("cancelled"));
            Assert.That(response.detail, Is.EqualTo("test_stop"));
            Assert.That(response.items[0].code, Is.EqualTo("applied"));
            Assert.That(response.items[1].code, Is.EqualTo("not_applied"));
            Assert.That(File.Exists(request.destinationPaths[1]), Is.False);
        }

        [Test]
        public void ShouldReportPartialLifecycleFailureWithoutLosingOtherOutcomes()
        {
            using TestCleanupScope cleanup = new();
            EditorAutomationRequest request = CreateRequest(Folder(cleanup), 2);
            using EditorAutomationSession session = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            OperationLifecycleData.ThrowAfterCreate = true;
            session.Advance(0);
            OperationLifecycleData.ThrowAfterCreate = false;
            session.Advance(0);
            EditorAutomationResponse response = Response(session);
            Assert.That(response.status, Is.EqualTo("failed"));
            Assert.That(response.complete, Is.False);
            Assert.That(response.items[0].code, Is.EqualTo("outcome_unknown"));
            Assert.That(response.items[1].code, Is.EqualTo("applied"));
            Assert.That(response.processedCount, Is.EqualTo(2));
        }

        [Test]
        public void ShouldRequireCompleteFileOutcomePagesBeforeApplying()
        {
            using TestCleanupScope cleanup = new();
            EditorAutomationRequest request = CreateRequest(Folder(cleanup), 2);
            request.pageSize = 1;
            using (EditorAutomationSession rejected = EditorAutomationSession.Start(JsonUtility.ToJson(request), requireSinglePage: true))
            {
                Assert.That(Response(rejected).code, Is.EqualTo("invalid_page"));
            }
            Assert.That(File.Exists(request.destinationPaths[0]), Is.False);
            string directory = Path.Combine("Temp", "AssetOperationJson-" + Guid.NewGuid().ToString("N"));
            cleanup.Defer(() => Directory.Delete(directory, true));
            Directory.CreateDirectory(directory);
            request.pageSize = 2;
            string input = Path.Combine(directory, "request.json");
            string output = Path.Combine(directory, "response.json");
            File.WriteAllText(input, JsonUtility.ToJson(request));
            Assert.That(EditorAutomation.RunFiles(input, output), Is.Zero);
            EditorAutomationResponse response = JsonUtility.FromJson<EditorAutomationResponse>(File.ReadAllText(output));
            Assert.That(response.items.Length, Is.EqualTo(2));
            Assert.That(response.items[0].code, Is.EqualTo("applied"));
            Assert.That(response.items[1].code, Is.EqualTo("applied"));
        }
    }
}
