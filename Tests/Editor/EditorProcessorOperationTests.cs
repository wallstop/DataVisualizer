namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class EditorProcessorOperationTests
    {
        private static string[] CreateAssets(TestCleanupScope cleanup)
        {
            string root = "Assets/ProcessorOperation-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => AssetDatabase.DeleteAsset(root));
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(root));
            string[] guids = new string[2];
            for (int i = 0; i < guids.Length; i++)
            {
                string path = root + "/" + i + ".asset";
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), path);
                guids[i] = AssetDatabase.AssetPathToGUID(path);
            }
            AssetDatabase.SaveAssets();
            return guids;
        }

        private static void Finish(EditorProcessorOperation operation)
        {
            for (int i = 0; i < 10 && !operation.IsFinished; i++)
            {
                operation.Advance(0);
            }
            Assert.That(operation.IsFinished, Is.True);
        }

        [Test]
        public void ShouldPreviewWithoutInvokingHooksAndProcessTheExactBatchOnce()
        {
            using TestCleanupScope cleanup = new();
            string[] guids = CreateAssets(cleanup);
            string firstPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            byte[] before = File.ReadAllBytes(firstPath);
            ProbeProcessor<TestDataObject> processor = new(false);
            using EditorProcessorOperation operation = new(processor, typeof(TestDataObject), guids);
            operation.Advance();
            Assert.That(operation.Status, Is.EqualTo("preview"));
            Assert.That(processor.AcceptsReads, Is.Zero);
            Assert.That(processor.Calls, Is.Zero);
            Assert.That(operation.PreparedCount, Is.Zero);
            Assert.That(File.ReadAllBytes(firstPath), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(AssetDatabase.LoadMainAssetAtPath(firstPath)), Is.False);
            Assert.That(operation.Targets.Select(target => target.Guid), Is.EqualTo(guids));
            operation.Apply();
            operation.Advance(0);
            Assert.That(operation.PreparedCount, Is.EqualTo(1));
            Assert.That(processor.Calls, Is.Zero);
            Finish(operation);
            Assert.That(operation.Status, Is.EqualTo("complete"));
            Assert.That(processor.Calls, Is.EqualTo(1));
            Assert.That(processor.Guids, Is.EqualTo(guids));
            Assert.That(operation.Targets.All(target => target.Code == "applied"), Is.True);
            operation.Apply();
            operation.Advance();
            Assert.That(processor.Calls, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldCancelBeforeInvocationAndRemainDisposed(bool prepare)
        {
            using TestCleanupScope cleanup = new();
            ProbeProcessor<TestDataObject> processor = new(false);
            using EditorProcessorOperation operation = new(processor, typeof(TestDataObject), CreateAssets(cleanup));
            if (prepare)
            {
                operation.Apply();
                operation.Advance(0);
            }
            operation.Dispose();
            operation.Dispose();
            operation.Apply();
            operation.Advance();
            Assert.That(operation.Status, Is.EqualTo("cancelled"));
            Assert.That(processor.Calls, Is.Zero);
            Assert.That(operation.Targets.All(target => target.Code == "not_applied"), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldRejectAChangedPreviouslyPreparedTargetWithoutInvokingTheBatch(bool delete)
        {
            using TestCleanupScope cleanup = new();
            string[] guids = CreateAssets(cleanup);
            ProbeProcessor<TestDataObject> processor = new(false);
            using EditorProcessorOperation operation = new(processor, typeof(TestDataObject), guids);
            operation.Apply();
            operation.Advance(0);
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            if (delete)
            {
                AssetDatabase.DeleteAsset(path);
            }
            else
            {
                Assert.That(AssetDatabase.RenameAsset(path, "Moved"), Is.Empty);
            }
            Finish(operation);
            Assert.That(operation.Code, Is.EqualTo("stale_target"));
            Assert.That(operation.Status, Is.EqualTo("rejected"));
            Assert.That(processor.Calls, Is.Zero);
        }

        [Test]
        public void ShouldReportUnknownOutcomesAfterAPartialProcessorFailure()
        {
            using TestCleanupScope cleanup = new();
            string[] guids = CreateAssets(cleanup);
            ProbeProcessor<TestDataObject> processor = new(true);
            using EditorProcessorOperation operation = new(processor, typeof(TestDataObject), guids);
            operation.Apply();
            Finish(operation);
            Assert.That(operation.Status, Is.EqualTo("failed"));
            Assert.That(operation.Code, Is.EqualTo("processor_failed"));
            Assert.That(operation.Targets.All(target => target.Code == "outcome_unknown"), Is.True);
            Assert.That(operation.Detail, Does.Contain("partial or unsaved"));
            Assert.That(processor.Calls, Is.EqualTo(1));
        }

        [Test]
        public void ShouldRejectInvalidDuplicateAndWrongTypeScopesWithoutCallingTheProcessor()
        {
            using TestCleanupScope cleanup = new();
            string[] guids = CreateAssets(cleanup);
            ProbeProcessor<TestDataObject> processor = new(false);
            Assert.Throws<ArgumentException>(() => new EditorProcessorOperation(processor, typeof(TestDataObject), new[] { guids[0], guids[0].ToUpperInvariant() }));
            Assert.Throws<ArgumentException>(() => new EditorProcessorOperation(processor, typeof(TestDataObject), new[] { "bad" }));
            Assert.Throws<ArgumentException>(() => new EditorProcessorOperation(processor, typeof(ScriptableObject), guids));
            Assert.Throws<ArgumentException>(() => new EditorProcessorOperation(processor, typeof(TestDataObject), Array.Empty<string>()));
            Assert.That(processor.Calls, Is.Zero);
        }

        private sealed class ProbeProcessor<T> : IDataProcessor
        {
            public string Name => "Probe";
            public string Description => "Explicit operation regression";
            public IEnumerable<Type> Accepts
            {
                get
                {
                    AcceptsReads++;
                    return new[] { typeof(T) };
                }
            }
            public int Calls { get; private set; }
            public int AcceptsReads { get; private set; }
            public string[] Guids { get; private set; }

            private readonly bool _fail;

            public ProbeProcessor(bool fail)
            {
                _fail = fail;
            }

            public void Process(Type type, IEnumerable<ScriptableObject> objects)
            {
                Calls++;
                Guids = objects.Select(asset => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset))).ToArray();
                if (_fail)
                {
                    ScriptableObject first = objects.First();
                    first.name = "Partial change";
                    EditorUtility.SetDirty(first);
                    throw new InvalidOperationException("Expected processor failure");
                }
            }
        }
    }
}
