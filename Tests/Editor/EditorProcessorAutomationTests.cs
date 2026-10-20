namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorProcessorAutomationTests
    {
        private static EditorAutomationRequest Request(string guid)
        {
            return new EditorAutomationRequest
            {
                schemaVersion = 1,
                operation = "previewProcessor",
                typeIdentities = new[] { typeof(TestDataObject).AssemblyQualifiedName },
                assetGuids = new[] { guid },
                processorIdentity = typeof(JsonProbeProcessor<int>).AssemblyQualifiedName,
            };
        }

        private static EditorAutomationResponse Response(EditorAutomationSession session)
        {
            return JsonUtility.FromJson<EditorAutomationResponse>(session.GetResponseJson());
        }

        [Test]
        public void ShouldReportPartialFailureThroughFilesAndReturnANonzeroExitCode()
        {
            using TestCleanupScope cleanup = new();
            string path = "Assets/ProcessorJsonFailure-" + Guid.NewGuid().ToString("N") + ".asset";
            cleanup.Defer(() => AssetDatabase.DeleteAsset(path));
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), path);
            string folder = Path.Combine(Path.GetTempPath(), "ProcessorJson-" + Guid.NewGuid().ToString("N"));
            cleanup.Defer(() => Directory.Delete(folder, true));
            Directory.CreateDirectory(folder);
            EditorAutomationRequest request = Request(AssetDatabase.AssetPathToGUID(path));
            request.processorIdentity = typeof(JsonProbeProcessor<string>).AssemblyQualifiedName;
            request.operation = "applyProcessor";
            request.confirmPaths = new[] { path };
            string input = Path.Combine(folder, "request.json");
            string output = Path.Combine(folder, "response.json");
            File.WriteAllText(input, JsonUtility.ToJson(request));
            Assert.That(EditorAutomation.RunFiles(input, output), Is.EqualTo(1));
            EditorAutomationResponse response = JsonUtility.FromJson<EditorAutomationResponse>(File.ReadAllText(output));
            Assert.That(response.status, Is.EqualTo("failed"));
            Assert.That(response.code, Is.EqualTo("processor_failed"));
            Assert.That(response.items[0].code, Is.EqualTo("outcome_unknown"));
        }

        [Test]
        public void ShouldAcceptValueTypeProcessorsLikeTheBrowser()
        {
            using TestCleanupScope cleanup = new();
            string path = "Assets/ProcessorStruct-" + Guid.NewGuid().ToString("N") + ".asset";
            cleanup.Defer(() => AssetDatabase.DeleteAsset(path));
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), path);
            EditorAutomationRequest request = Request(AssetDatabase.AssetPathToGUID(path));
            request.processorIdentity = typeof(StructProbeProcessor<TestDataObject>).AssemblyQualifiedName;
            request.operation = "applyProcessor";
            request.confirmPaths = new[] { path };
            using EditorAutomationSession session = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            session.Advance();
            Assert.That(Response(session).items[0].code, Is.EqualTo("applied"));
        }

        [Test]
        public void ShouldPreviewWithoutConstructionAndMatchDirectBatchOutcomes()
        {
            using TestCleanupScope cleanup = new();
            string path = "Assets/ProcessorJson-" + Guid.NewGuid().ToString("N") + ".asset";
            cleanup.Defer(() => AssetDatabase.DeleteAsset(path));
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), path);
            AssetDatabase.SaveAssets();
            byte[] before = File.ReadAllBytes(path);
            EditorAutomationRequest request = Request(AssetDatabase.AssetPathToGUID(path));
            using EditorAutomationSession preview = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            EditorAutomationResponse result = Response(preview);
            Assert.That(result.complete, Is.True);
            Assert.That(result.code, Is.EqualTo("preview"));
            Assert.That(result.items[0].path, Is.EqualTo(path));
            Assert.That(result.items[0].code, Is.EqualTo("not_applied"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(AssetDatabase.LoadMainAssetAtPath(path)), Is.False);
            request.operation = "applyProcessor";
            using (EditorAutomationSession unconfirmed = EditorAutomationSession.Start(JsonUtility.ToJson(request)))
            {
                Assert.That(Response(unconfirmed).code, Is.EqualTo("invalid_scope"));
            }
            request.confirmPaths = new[] { result.items[0].path };
            using EditorAutomationSession apply = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            apply.Advance();
            EditorAutomationResponse applied = Response(apply);
            Assert.That(applied.status, Is.EqualTo("complete"));
            Assert.That(applied.items[0].code, Is.EqualTo("applied"));
            using EditorProcessorOperation direct = new(typeof(JsonProbeProcessor<int>), typeof(TestDataObject), request.assetGuids);
            direct.Apply();
            direct.Advance();
            Assert.That(applied.items[0].code, Is.EqualTo(direct.Targets[0].Code));
            Assert.That(applied.items[0].guid, Is.EqualTo(direct.Targets[0].Guid));
        }

        [Test]
        public void ShouldNotConstructAThrowingProcessorDuringPreview()
        {
            using TestCleanupScope cleanup = new();
            string path = "Assets/ProcessorJsonThrow-" + Guid.NewGuid().ToString("N") + ".asset";
            cleanup.Defer(() => AssetDatabase.DeleteAsset(path));
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), path);
            EditorAutomationRequest request = Request(AssetDatabase.AssetPathToGUID(path));
            request.processorIdentity = typeof(ThrowingConstructorProcessor<int>).AssemblyQualifiedName;
            using EditorAutomationSession preview = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            Assert.That(Response(preview).code, Is.EqualTo("preview"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldRejectChangedConfirmationOrCancelBeforeProcessing(bool cancel)
        {
            using TestCleanupScope cleanup = new();
            string path = "Assets/ProcessorJsonCancel-" + Guid.NewGuid().ToString("N") + ".asset";
            cleanup.Defer(() => AssetDatabase.DeleteAsset(path));
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), path);
            EditorAutomationRequest request = Request(AssetDatabase.AssetPathToGUID(path));
            request.operation = "applyProcessor";
            request.confirmPaths = new[] { cancel ? path : path + "wrong" };
            using EditorAutomationSession apply = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            apply.Cancel("test_caller_cancelled");
            apply.Advance();
            Assert.That(Response(apply).status, Is.EqualTo(cancel ? "cancelled" : "rejected"));
            if (cancel)
            {
                Assert.That(Response(apply).detail, Is.EqualTo("test_caller_cancelled"));
            }
            Assert.That(AssetDatabase.LoadMainAssetAtPath(path).name, Is.EqualTo(Path.GetFileNameWithoutExtension(path)));
        }

        public sealed class JsonProbeProcessor<T> : IDataProcessor
        {
            public string Name => "JSON probe";
            public string Description => "Explicit JSON regression";
            public IEnumerable<Type> Accepts => new[] { typeof(TestDataObject) };

            public void Process(Type type, IEnumerable<ScriptableObject> objects)
            {
                foreach (ScriptableObject asset in objects)
                {
                    asset.name = "Processed";
                    EditorUtility.SetDirty(asset);
                    if (typeof(T) == typeof(string))
                    {
                        throw new InvalidOperationException("Expected partial failure");
                    }
                }
            }
        }

        public struct StructProbeProcessor<T> : IDataProcessor
        {
            public string Name => "Struct probe";
            public string Description => "Value-type processor compatibility";
            public IEnumerable<Type> Accepts => new[] { typeof(T) };

            public void Process(Type type, IEnumerable<ScriptableObject> objects) { }
        }

        public sealed class ThrowingConstructorProcessor<T> : IDataProcessor
        {
            public string Name => "Throwing probe";
            public string Description => "Preview must not construct this processor";
            public IEnumerable<Type> Accepts => Array.Empty<Type>();

            public ThrowingConstructorProcessor()
            {
                throw new InvalidOperationException("Construction must be deferred until apply");
            }

            public void Process(Type type, IEnumerable<ScriptableObject> objects) { }
        }
    }
}
