namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorAutomationTests
    {
        [TestCase("{", "invalid_json")]
        [TestCase("{}", "unsupported_version")]
        [TestCase("{\"schemaVersion\":2}", "unsupported_version")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"apply\"}", "unsupported_operation")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"query\",\"pageSize\":1001}", "invalid_page")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"query\",\"typeIdentities\":[\"ScriptableObject\"],\"pageSize\":10}", "invalid_scope")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"validate\",\"assetGuids\":[\"invalid\"],\"pageSize\":10}", "invalid_scope")]
        public void ShouldRejectUnsupportedRequestsWithoutStartingWork(string json, string code)
        {
            using EditorAutomationSession session = EditorAutomationSession.Start(json);
            session.Advance();
            EditorAutomationResponse response = JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson());
            Assert.That(session.IsFinished, Is.True);
            Assert.That(response.status, Is.EqualTo("rejected"));
            Assert.That(response.code, Is.EqualTo(code));
            Assert.That(response.items, Is.Empty);
            Assert.That(response.processedCount, Is.Zero);
        }

        [Test]
        public void ShouldMatchDirectQueriesWithoutChangingTheAsset()
        {
            using TestCleanupScope cleanup = new();
            string path = "Assets/AutomationQuery-" + Guid.NewGuid().ToString("N") + ".asset";
            cleanup.Defer(() => AssetDatabase.DeleteAsset(path));
            TestDataObject asset = ScriptableObject.CreateInstance<TestDataObject>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            string guid = AssetDatabase.AssetPathToGUID(path);
            byte[] before = File.ReadAllBytes(path);
            var request = new EditorAutomationRequest
            {
                schemaVersion = 1,
                operation = "query",
                typeIdentities = new[] { typeof(TestDataObject).AssemblyQualifiedName },
                text = guid,
            };
            using EditorAutomationSession session = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            using EditorAssetQuery direct = new(new[] { typeof(TestDataObject) }, guid);
            for (int i = 0; i < 10000 && (!session.IsFinished || !direct.IsComplete); i++)
            {
                session.Advance(10);
                direct.Advance(10);
            }
            var response = JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson());
            Assert.That(response.complete, Is.True);
            Assert.That(response.items.Select(item => item.guid), Is.EqualTo(direct.GetPage(0, 100).Select(item => item.Guid)));
            Assert.That(response.items[0].typeIdentity, Is.EqualTo(typeof(TestDataObject).AssemblyQualifiedName));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(asset), Is.False);
        }

        [Test]
        public void ShouldExposeTheSameValidationFindingsInPages()
        {
            using TestCleanupScope cleanup = new();
            string prefix = "Assets/AutomationValidation-" + Guid.NewGuid().ToString("N");
            string[] paths = { prefix + "First.asset", prefix + "Second.asset" };
            foreach (string path in paths)
            {
                cleanup.Defer(() => AssetDatabase.DeleteAsset(path));
                TestDataObject asset = ScriptableObject.CreateInstance<TestDataObject>();
                AssetDatabase.CreateAsset(asset, path);
                using SerializedObject serialized = new(asset);
                serialized.FindProperty("_assetGuid").stringValue = "duplicate";
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            string[] guids = paths.Select(AssetDatabase.AssetPathToGUID).ToArray();
            var request = new EditorAutomationRequest
            {
                schemaVersion = 1,
                operation = "validate",
                assetGuids = guids,
            };
            using EditorAutomationSession session = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            using EditorAssetValidation direct = new(guids);
            session.Advance(1000);
            direct.Advance(1000);
            var response = JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson(1, 1));
            Assert.That(response.complete, Is.True);
            Assert.That(response.availableCount, Is.EqualTo(direct.Findings.Count));
            Assert.That(response.items.Length, Is.EqualTo(1));
            Assert.That(response.items[0].guid, Is.EqualTo(direct.Findings[1].Guid));
            Assert.That(response.items[0].code, Is.EqualTo(direct.Findings[1].Code));
            Assert.That(response.coverage, Does.Contain("Custom serialization is not checked"));
        }

        [Test]
        public void ShouldPreservePartialStateWhileBusyAndAfterCancellation()
        {
            using TestCleanupScope cleanup = new();
            Func<bool> previous = AssetGuidDiscovery.AssetDatabaseBusyOverride;
            cleanup.Defer(() => AssetGuidDiscovery.AssetDatabaseBusyOverride = previous);
            AssetGuidDiscovery.AssetDatabaseBusyOverride = () => true;
            using EditorAutomationSession session = EditorAutomationSession.Start(
                "{\"schemaVersion\":1,\"operation\":\"validate\",\"assetGuids\":[\"0123456789abcdef0123456789abcdef\"],\"pageSize\":10}"
            );
            session.Advance();
            var waiting = JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson());
            Assert.That(waiting.status, Is.EqualTo("waiting"));
            Assert.That(waiting.complete, Is.False);
            session.Cancel();
            session.Advance();
            var cancelled = JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson());
            Assert.That(cancelled.status, Is.EqualTo("cancelled"));
            Assert.That(cancelled.processedCount, Is.Zero);
        }

        [Test]
        public void ShouldWriteMachineReadableFileResultsAndRejectAssetOutputPaths()
        {
            using TestCleanupScope cleanup = new();
            string directory = Path.Combine(Path.GetTempPath(), "DxAutomation-" + Guid.NewGuid().ToString("N"));
            cleanup.Defer(() => Directory.Delete(directory, true));
            Directory.CreateDirectory(directory);
            string input = Path.Combine(directory, "request.json");
            string output = Path.Combine(directory, "response.json");
            File.WriteAllText(input, "{\"schemaVersion\":1,\"operation\":\"validate\",\"assetGuids\":[\"0123456789abcdef0123456789abcdef\"],\"pageSize\":10}");
            Assert.That(EditorAutomation.RunFiles(input, output, 0), Is.EqualTo(2));
            Assert.That(JsonUtility.FromJson<EditorAutomationResponse>(File.ReadAllText(output)).complete, Is.False);
            Assert.That(EditorAutomation.RunFiles(input, output), Is.EqualTo(0));
            var completed = JsonUtility.FromJson<EditorAutomationResponse>(File.ReadAllText(output));
            Assert.That(completed.complete, Is.True);
            Assert.That(completed.items[0].code, Is.EqualTo("asset_unavailable"));
            Assert.Throws<ArgumentException>(() => EditorAutomation.RunFiles(input, Path.Combine(Application.dataPath, "response.json")));
            Assert.Throws<ArgumentException>(() => EditorAutomation.RunFiles(input, input));
            File.Delete(input);
            Assert.That(EditorAutomation.RunFiles(input, output), Is.EqualTo(1));
            Assert.That(JsonUtility.FromJson<EditorAutomationResponse>(File.ReadAllText(output)).code, Is.EqualTo("request_read_failed"));
        }

        [Test]
        public void ShouldUseTheDefaultPageSizeWhenItIsOmitted()
        {
            using EditorAutomationSession session = EditorAutomationSession.Start(
                "{\"schemaVersion\":1,\"operation\":\"validate\",\"assetGuids\":[\"0123456789abcdef0123456789abcdef\"]}"
            );
            session.Advance(1000);
            var response = JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson());
            Assert.That(response.status, Is.EqualTo("complete"));
            Assert.That(session.DefaultPageSize, Is.EqualTo(100));
            Assert.That(response.scopeCount, Is.EqualTo(1));
        }
    }
}
