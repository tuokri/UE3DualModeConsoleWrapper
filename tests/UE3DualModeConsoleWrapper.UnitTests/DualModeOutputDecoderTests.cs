using Xunit;

namespace UE3DualModeConsoleWrapper.UnitTests;

public sealed class DualModeOutputDecoderTests
{
    [Fact]
    public void ForwardsUtf16LeTextUnchanged()
    {
        // TODO: Cover ordinary text, CRLF, and category prefixes.
        Assert.Fail("TODO: Verify ordinary UTF-16LE output forwarding.");
    }

    [Fact]
    public void RejectsTruncatedFinalCodeUnit()
    {
        // TODO: Configure strict UTF-16LE fallback and assert a concise failure.
        Assert.Fail("TODO: Verify truncated UTF-16LE reporting.");
    }

    [Fact]
    public void HandlesControlRecordsAcrossEveryReadBoundary()
    {
        // TODO: Split the marker, payload, Reset, and terminating CRLF at every boundary.
        Assert.Fail("TODO: Verify split control-record decoding.");
    }

    [Fact]
    public void MapsBinaryRgbiPayloadsToConsoleAttributes()
    {
        // TODO: Cover 0000, 0001, 0010, 0100, 1000, 1111, and 11111111.
        Assert.Fail("TODO: Verify binary RGBI color mapping.");
    }

    [Fact]
    public void ConsumesControlRecordTerminatingCrLf()
    {
        // TODO: Assert marker, payload, Reset, and CRLF produce no extra blank lines.
        Assert.Fail("TODO: Verify control-record CRLF is consumed.");
    }

    [Fact]
    public void AppliesForegroundAndBackgroundRgbIAttributes()
    {
        // TODO: Assert binary RGBI positions map to foreground and background attributes.
        Assert.Fail("TODO: Verify foreground and background RGBI attributes.");
    }

    [Fact]
    public void ResetsColorOnEveryExitPath()
    {
        // TODO: Cover EOF, decoder failure, cancellation, and output failure.
        Assert.Fail("TODO: Verify terminal color restoration.");
    }
}
