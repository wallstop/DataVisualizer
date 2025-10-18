namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System;
    using NUnit.Framework;
    using State;

    [TestFixture]
    public sealed class DiagnosticsStateTests
    {
        [Test]
        public void RecordProcessorExecutionWhenTelemetryDisabledSkipsSamples()
        {
            DiagnosticsState diagnostics = new DiagnosticsState();
            ProcessorExecutionTelemetry telemetry = new ProcessorExecutionTelemetry(
                "ProcessorA",
                "TypeA",
                5,
                true,
                0.25d,
                1024L,
                DateTime.UtcNow
            );

            diagnostics.RecordProcessorExecution(telemetry);

            Assert.IsEmpty(diagnostics.ProcessorExecutions);
        }

        [Test]
        public void RecordProcessorExecutionTrimsToMaximumSampleCount()
        {
            DiagnosticsState diagnostics = new DiagnosticsState();
            diagnostics.SetProcessorTelemetryEnabled(true);
            diagnostics.MaxProcessorExecutionSamples = 2;

            diagnostics.RecordProcessorExecution(
                new ProcessorExecutionTelemetry(
                    "First",
                    "Type",
                    1,
                    true,
                    0.10d,
                    200L,
                    DateTime.UtcNow
                )
            );
            diagnostics.RecordProcessorExecution(
                new ProcessorExecutionTelemetry(
                    "Second",
                    "Type",
                    1,
                    true,
                    0.10d,
                    200L,
                    DateTime.UtcNow
                )
            );
            diagnostics.RecordProcessorExecution(
                new ProcessorExecutionTelemetry(
                    "Third",
                    "Type",
                    1,
                    true,
                    0.10d,
                    200L,
                    DateTime.UtcNow
                )
            );

            Assert.AreEqual(2, diagnostics.ProcessorExecutions.Count);
            Assert.AreEqual("Second", diagnostics.ProcessorExecutions[0].ProcessorName);
            Assert.AreEqual("Third", diagnostics.ProcessorExecutions[1].ProcessorName);
        }

        [Test]
        public void SetProcessorTelemetryEnabledFalseClearsRecordedSamples()
        {
            DiagnosticsState diagnostics = new DiagnosticsState();
            diagnostics.SetProcessorTelemetryEnabled(true);
            diagnostics.RecordProcessorExecution(
                new ProcessorExecutionTelemetry(
                    "Processor",
                    "Type",
                    10,
                    true,
                    0.5d,
                    4096L,
                    DateTime.UtcNow
                )
            );

            diagnostics.SetProcessorTelemetryEnabled(false);

            Assert.IsEmpty(diagnostics.ProcessorExecutions);
            Assert.IsFalse(diagnostics.ProcessorTelemetryEnabled);
        }
    }
}
