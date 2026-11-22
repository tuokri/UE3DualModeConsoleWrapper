using System.Runtime.Versioning;
using Xunit;

namespace UE3DualModeConsoleWrapper.UnitTests;

[SupportedOSPlatform("windows")]
public sealed class NativeProcessCommandLineTests
{
    [Fact]
    public void QuotesWindowsCommandLineTokens()
    {
        // TODO: Cover spaces, tabs, quotes, empty strings, and trailing backslashes.
        Assert.Fail("TODO: Verify Windows CRT command-line quoting.");
    }

    [Fact]
    public void RejectsEmbeddedNulCharacters()
    {
        // TODO: Exercise every executable-path and target-argument NUL path.
        Assert.Fail("TODO: Verify NUL rejection before native launch.");
    }

    [Fact]
    public void RoundTripsThroughCommandLineToArgvW()
    {
        // TODO: Add a Windows-only CommandLineToArgvW interop assertion.
        Assert.Fail("TODO: Verify generated command lines round-trip on Windows.");
    }
}
