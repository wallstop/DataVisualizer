namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorAssetOperationTests
    {
        private static string CreateFolder(TestCleanupScope cleanup)
        {
            string leaf = "AssetOperations-" + Guid.NewGuid().ToString("N");
            string folder = "Assets/" + leaf;
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            cleanup.Defer(() =>
            {
                OperationLifecycleData.Callback = null;
                OperationLifecycleData.HookCount = 0;
                OperationLifecycleData.ThrowAfterCreate = false;
            });
            AssetDatabase.CreateFolder("Assets", leaf);
            return folder;
        }

        private static string CreateSource(string folder)
        {
            string path = folder + "/Source.asset";
            OperationLifecycleData source = ScriptableObject.CreateInstance<OperationLifecycleData>();
            AssetDatabase.CreateAsset(source, path);
            source.AfterCreate();
            EditorUtility.SetDirty(source);
            AssetDatabase.SaveAssetIfDirty(source);
            OperationLifecycleData.HookCount = 0;
            return AssetDatabase.AssetPathToGUID(path);
        }

        [TestCase(EditorAssetOperationKind.Create)]
        [TestCase(EditorAssetOperationKind.Clone)]
        [TestCase(EditorAssetOperationKind.Rename)]
        [TestCase(EditorAssetOperationKind.Move)]
        [TestCase(EditorAssetOperationKind.Delete)]
        public void ShouldPreviewWithoutMutationAndApplyLifecycleThroughTheExactGuid(EditorAssetOperationKind kind)
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFolder(cleanup);
            string guid = CreateSource(folder);
            string sourcePath = AssetDatabase.GUIDToAssetPath(guid);
            string destination = folder + "/Result.asset";
            byte[] before = File.ReadAllBytes(sourcePath);
            OperationLifecycleData source = AssetDatabase.LoadAssetAtPath<OperationLifecycleData>(sourcePath);
            string oldId = source.Id;
            OperationLifecycleData.HookCount = 0;
            using EditorAssetOperation operation = new(
                kind,
                typeof(OperationLifecycleData),
                kind == EditorAssetOperationKind.Create ? null : new[] { guid },
                kind == EditorAssetOperationKind.Delete ? null : new[] { destination }
            );
            Assert.That(operation.Status, Is.EqualTo("preview"));
            Assert.That(OperationLifecycleData.HookCount, Is.Zero);
            Assert.That(EditorUtility.IsDirty(source), Is.False);
            Assert.That(File.ReadAllBytes(sourcePath), Is.EqualTo(before));
            Assert.That(AssetDatabase.AssetPathToGUID(destination), Is.Empty);
            operation.Apply();
            Assert.That(operation.ProcessedCount, Is.Zero);
            operation.Advance(0);
            Assert.That(operation.Status, Is.EqualTo("complete"));
            Assert.That(operation.Targets[0].Code, Is.EqualTo("applied"));
            Assert.That(operation.SucceededCount, Is.EqualTo(1));
            if (kind == EditorAssetOperationKind.Delete)
            {
                Assert.That(File.Exists(sourcePath), Is.False);
                Assert.That(AssetDatabase.LoadAssetAtPath<ScriptableObject>(sourcePath) == null);
                Assert.That(OperationLifecycleData.HookCount, Is.Zero);
                return;
            }
            OperationLifecycleData result = AssetDatabase.LoadAssetAtPath<OperationLifecycleData>(destination);
            Assert.That(result != null);
            Assert.That(EditorUtility.IsDirty(result), Is.False);
            Assert.That(operation.Targets[0].ResultGuid, Is.EqualTo(AssetDatabase.AssetPathToGUID(destination)));
            Assert.That(OperationLifecycleData.HookCount, Is.EqualTo(kind == EditorAssetOperationKind.Move ? 0 : 2));
            if (kind == EditorAssetOperationKind.Clone || kind == EditorAssetOperationKind.Create)
            {
                Assert.That(result.Id, Is.Not.EqualTo(oldId));
                Assert.That(result.marker, Is.EqualTo(kind == EditorAssetOperationKind.Clone ? "cloned" : "created"));
            }
            else
            {
                Assert.That(result.Id, Is.EqualTo(oldId));
            }
            Assert.That(File.ReadAllText(destination), Does.Contain(result.Id));
            operation.Advance();
            Assert.That(operation.ProcessedCount, Is.EqualTo(1));
        }

        [Test]
        public void ShouldCreateOrdinaryScriptableObjectsAndRejectWrongExactTypes()
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFolder(cleanup);
            using EditorAssetOperation operation = new(EditorAssetOperationKind.Create, typeof(TestDataObject), null, new[] { folder + "/Plain.asset" });
            operation.Apply();
            operation.Advance(0);
            Assert.That(operation.Status, Is.EqualTo("complete"));
            Assert.That(AssetDatabase.GetMainAssetTypeAtPath(folder + "/Plain.asset"), Is.EqualTo(typeof(TestDataObject)));
            Assert.Throws<ArgumentException>(() =>
                new EditorAssetOperation(EditorAssetOperationKind.Delete, typeof(OperationLifecycleData), new[] { operation.Targets[0].ResultGuid })
            );
        }

        [Test]
        public void ShouldYieldBetweenTargetsAndCancelWithoutReplayingCompletedWork()
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFolder(cleanup);
            using EditorAssetOperation operation = new(EditorAssetOperationKind.Create, typeof(OperationLifecycleData), null, new[] { folder + "/A.asset", folder + "/B.asset" });
            operation.Apply();
            operation.Advance(0);
            Assert.That(operation.ProcessedCount, Is.EqualTo(1));
            operation.Cancel();
            operation.Advance();
            Assert.That(operation.Status, Is.EqualTo("cancelled"));
            Assert.That(AssetDatabase.AssetPathToGUID(folder + "/A.asset"), Is.Not.Empty);
            Assert.That(AssetDatabase.AssetPathToGUID(folder + "/B.asset"), Is.Empty);
            Assert.That(operation.Targets[0].Code, Is.EqualTo("applied"));
            Assert.That(operation.Targets[1].Code, Is.EqualTo("not_applied"));
        }

        [Test]
        public void ShouldRejectStaleSourcesAndDestinationCollisionsBeforeHooks()
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFolder(cleanup);
            string guid = CreateSource(folder);
            using EditorAssetOperation moved = new(EditorAssetOperationKind.Clone, typeof(OperationLifecycleData), new[] { guid }, new[] { folder + "/Clone.asset" });
            AssetDatabase.MoveAsset(folder + "/Source.asset", folder + "/Moved.asset");
            moved.Apply();
            moved.Advance(0);
            Assert.That(moved.Targets[0].Code, Is.EqualTo("stale_target"));
            Assert.That(OperationLifecycleData.HookCount, Is.Zero);
            using EditorAssetOperation collision = new(EditorAssetOperationKind.Create, typeof(OperationLifecycleData), null, new[] { folder + "/Occupied.asset" });
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), folder + "/Occupied.asset");
            collision.Apply();
            collision.Advance(0);
            Assert.That(collision.Targets[0].Code, Is.EqualTo("stale_target"));
            Assert.That(OperationLifecycleData.HookCount, Is.Zero);
        }

        [Test]
        public void ShouldReportUncertainLifecycleFailureAndContinueOtherSafeUnits()
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFolder(cleanup);
            using EditorAssetOperation operation = new(EditorAssetOperationKind.Create, typeof(OperationLifecycleData), null, new[] { folder + "/A.asset", folder + "/B.asset" });
            OperationLifecycleData.ThrowAfterCreate = true;
            operation.Apply();
            operation.Advance(0);
            Assert.That(operation.Targets[0].Code, Is.EqualTo("outcome_unknown"));
            Assert.That(operation.Targets[0].Detail, Does.Contain("Expected operation lifecycle failure"));
            OperationLifecycleData.ThrowAfterCreate = false;
            operation.Advance(0);
            Assert.That(operation.Status, Is.EqualTo("failed"));
            Assert.That(operation.Targets[1].Code, Is.EqualTo("applied"));
            Assert.That(operation.SucceededCount, Is.EqualTo(1));
        }

        [Test]
        public void ShouldCancelReentrantCallbacksOnlyBetweenSafeUnits()
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFolder(cleanup);
            using EditorAssetOperation operation = new(EditorAssetOperationKind.Create, typeof(OperationLifecycleData), null, new[] { folder + "/A.asset", folder + "/B.asset" });
            OperationLifecycleData.Callback = operation.Cancel;
            operation.Apply();
            operation.Advance();
            Assert.That(operation.Status, Is.EqualTo("cancelled"));
            Assert.That(operation.ProcessedCount, Is.EqualTo(1));
            Assert.That(operation.Targets[0].Code, Is.EqualTo("applied"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Packages/Result.asset")]
        [TestCase("Assets/../Result.asset")]
        [TestCase("Assets/Result.txt")]
        public void ShouldRejectInvalidDestinationsWithoutCreatingAnything(string destination)
        {
            Assert.Throws<ArgumentException>(() => new EditorAssetOperation(EditorAssetOperationKind.Create, typeof(OperationLifecycleData), null, new[] { destination }));
        }
    }
}
