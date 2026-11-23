using System.Runtime.Versioning;
using Xunit;

namespace UE3DualModeConsoleWrapper.UnitTests;

[SupportedOSPlatform("windows")]
public sealed class DualModeLauncherLifecycleTests
{
    [Fact]
    public void CreatesManualResetProtocolEvents()
    {
        // TODO: Verify dualmode_die_event<PID> and ComWrapperShutdown are manual reset.
        Assert.Fail("TODO: Verify protocol event reset modes.");
    }

    [Fact]
    public void FirstSignalUsesCooperativeShutdownAndKeepsDraining()
    {
        var gracefulCount = 0;
        var forcedCount = 0;
        var messages = new List<string>();
        var controller = new ConsoleInterruptController(
            () => gracefulCount++,
            () => forcedCount++,
            messages.Add);

        controller.HandleInterrupt();

        Assert.True(controller.ShutdownRequested);
        Assert.Equal(1, gracefulCount);
        Assert.Equal(0, forcedCount);
        Assert.Equal(
            ["Exiting... Press Ctrl+C again to force exit."],
            messages);
    }

    [Fact]
    public void SecondSignalTerminatesJobIdempotently()
    {
        var gracefulCount = 0;
        var forcedCount = 0;
        var messages = new List<string>();
        var controller = new ConsoleInterruptController(
            () => gracefulCount++,
            () => forcedCount++,
            messages.Add);

        controller.HandleInterrupt();
        controller.HandleInterrupt();
        controller.HandleInterrupt();

        Assert.Equal(1, gracefulCount);
        Assert.Equal(1, forcedCount);
        Assert.Equal(
            [
                "Exiting... Press Ctrl+C again to force exit.",
                "Forcing exit..."
            ],
            messages);
    }

    [Fact]
    public void CleansUpStartupAndRuntimeFailures()
    {
        // TODO: Cover start, job assignment, resume, pipe, and cleanup failures.
        Assert.Fail("TODO: Verify lifecycle failure cleanup.");
    }
}