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
        // TODO: Verify ComWrapperShutdown signaling and continued output draining.
        Assert.Fail("TODO: Verify first-signal graceful shutdown.");
    }

    [Fact]
    public void SecondSignalTerminatesJobIdempotently()
    {
        // TODO: Verify repeated force requests terminate the job only once.
        Assert.Fail("TODO: Verify second-signal forced shutdown.");
    }

    [Fact]
    public void CleansUpStartupAndRuntimeFailures()
    {
        // TODO: Cover start, job assignment, resume, pipe, and cleanup failures.
        Assert.Fail("TODO: Verify lifecycle failure cleanup.");
    }
}
