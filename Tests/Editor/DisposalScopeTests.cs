namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class DisposalScopeTests
    {
        [Test]
        public void ShouldDisposeReusableLeaseOnlyOnceAcrossCopiesAndSlotReuse()
        {
            int cleanupCount = 0;
            ReusableDisposalScope<int> scopes = new(_ => cleanupCount++);

            ReusableDisposalLease<int> first = scopes.Acquire(1);
            ReusableDisposalLease<int> firstCopy = first;
            first.Dispose();
            firstCopy.Dispose();

            ReusableDisposalLease<int> second = scopes.Acquire(2);
            firstCopy.Dispose();
            second.Dispose();

            Assert.That(cleanupCount, Is.EqualTo(2));
        }

        [Test]
        public void ShouldAllowReusableCleanupToAcquireAnotherLease()
        {
            List<int> cleanedStates = new();
            ReusableDisposalScope<int> scopes = null;
            scopes = new ReusableDisposalScope<int>(state =>
            {
                cleanedStates.Add(state);
                if (state == 1)
                {
                    using ReusableDisposalLease<int> nested = scopes.Acquire(2);
                }
            });

            using (scopes.Acquire(1)) { }

            Assert.That(cleanedStates, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void ShouldMakeDefaultReusableLeaseSafeToDispose()
        {
            Assert.DoesNotThrow(() => default(ReusableDisposalLease<int>).Dispose());
        }

        [Test]
        public void ShouldReportReusableCleanupFailureWithoutThrowingFromDispose()
        {
            ReusableDisposalScope<int> scopes = new(_ =>
                throw new InvalidOperationException("Expected cleanup failure.")
            );
            ReusableDisposalLease<int> lease = scopes.Acquire(1);
            LogAssert.Expect(
                LogType.Exception,
                "InvalidOperationException: Expected cleanup failure."
            );

            Assert.DoesNotThrow(lease.Dispose);
        }

        [Test]
        public void ShouldRunEveryTestCleanupInReverseOrderOnlyOnce()
        {
            List<int> cleanupOrder = new();
            TestCleanupScope cleanup = new();
            cleanup.Defer(() => cleanupOrder.Add(1));
            cleanup.Defer(() => cleanupOrder.Add(2));

            cleanup.Dispose();
            cleanup.Dispose();

            Assert.That(cleanupOrder, Is.EqualTo(new[] { 2, 1 }));
        }

        [Test]
        public void ShouldContinueTestCleanupAfterOneActionFails()
        {
            bool finalCleanupRan = false;
            TestCleanupScope cleanup = new();
            cleanup.Defer(() => finalCleanupRan = true);
            cleanup.Defer(() =>
                throw new InvalidOperationException("Expected test cleanup failure.")
            );
            LogAssert.Expect(
                LogType.Exception,
                "InvalidOperationException: Expected test cleanup failure."
            );

            cleanup.Dispose();

            Assert.That(finalCleanupRan, Is.True);
        }
    }
}
