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
    public void MapsUdkBinaryPalettePayloadsToConsoleColors()
    {
        // TODO: Cover all 0000 through 1111 palette entries, including 0001 as Black.
        Assert.Fail("TODO: Verify UDK foreground palette mapping.");
    }

    [Fact]
    public void ConsumesControlRecordTerminatingCrLf()
    {
        // TODO: Assert marker, payload, Reset, and CRLF produce no extra blank lines.
        Assert.Fail("TODO: Verify control-record CRLF is consumed.");
    }

    [Fact]
    public void IgnoresPayloadSuffixWithoutApplyingBackgroundColor()
    {
        // TODO: Assert 11111111 consumes the full record and still selects palette entry 1111.
        Assert.Fail("TODO: Verify longer palette payload suffix handling.");
    }

    [Fact]
    public void ResetsColorOnEveryExitPath()
    {
        // TODO: Cover EOF, decoder failure, cancellation, and output failure.
        Assert.Fail("TODO: Verify terminal color restoration.");
    }
}
