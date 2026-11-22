using System.Text;
using Xunit;

namespace UE3DualModeConsoleWrapper.UnitTests;

public sealed class DualModeOutputDecoderTests
{
    [Fact]
    public async Task ForwardsUtf16LeTextUnchanged()
    {
        const string text = "Init: Version: 12791\r\nLog: Ready\r\n";
        var output = new FakeDualModeOutput();
        var decoder = new DualModeOutputDecoder(useColor: true, output);

        await decoder.DecodeAsync(CreateUtf16Stream(text), CancellationToken.None);

        Assert.Equal(text, output.Text);
        Assert.True(output.FlushCount >= 2);
    }

    [Fact]
    public async Task RejectsTruncatedFinalCodeUnit()
    {
        var output = new FakeDualModeOutput();
        var decoder = new DualModeOutputDecoder(useColor: true, output);
        await using var stream = new MemoryStream([0x41]);

        await Assert.ThrowsAsync<DecoderFallbackException>(() => decoder.DecodeAsync(stream, CancellationToken.None));

        Assert.Equal(1, output.ResetCount);

        var isolatedSurrogateOutput = new FakeDualModeOutput();
        var isolatedSurrogateDecoder = new DualModeOutputDecoder(
            useColor: true,
            isolatedSurrogateOutput);
        await using var isolatedSurrogateStream =
            new MemoryStream([0x00, 0xD8]);

        await Assert.ThrowsAsync<DecoderFallbackException>(() => isolatedSurrogateDecoder.DecodeAsync(
            isolatedSurrogateStream,
            CancellationToken.None));

        Assert.Equal(1, isolatedSurrogateOutput.ResetCount);
    }

    [Fact]
    public void HandlesControlRecordsAcrossEveryReadBoundary()
    {
        const string input =
            "before\r\n" +
            "`~[~`1001\r\n" +
            "error\r\n" +
            "`~[~`rEsEt\r\n" +
            "after\r\n";
        const string expected = "before\r\nerror\r\nafter\r\n";

        for (var splitPoint = 0; splitPoint <= input.Length; splitPoint++)
        {
            var output = new FakeDualModeOutput();
            var decoder = new DualModeOutputDecoder(useColor: true, output);

            decoder.Append(input.AsSpan(0, splitPoint));
            decoder.Append(input.AsSpan(splitPoint));
            decoder.Flush();

            Assert.Equal(expected, output.Text);
            Assert.Equal([ConsoleColor.Red], output.ForegroundColors);
            Assert.Equal(1, output.ResetCount);
        }
    }

    public static TheoryData<string, ConsoleColor> UdkPaletteCases => new()
    {
        { "0000", ConsoleColor.Black },
        { "0001", ConsoleColor.Black },
        { "0010", ConsoleColor.DarkBlue },
        { "0011", ConsoleColor.Blue },
        { "0100", ConsoleColor.DarkGreen },
        { "0101", ConsoleColor.Green },
        { "0110", ConsoleColor.DarkCyan },
        { "0111", ConsoleColor.Cyan },
        { "1000", ConsoleColor.DarkRed },
        { "1001", ConsoleColor.Red },
        { "1010", ConsoleColor.DarkMagenta },
        { "1011", ConsoleColor.Magenta },
        { "1100", ConsoleColor.DarkYellow },
        { "1101", ConsoleColor.Yellow },
        { "1110", ConsoleColor.Gray },
        { "1111", ConsoleColor.White }
    };

    [Theory]
    [MemberData(nameof(UdkPaletteCases))]
    public void MapsUdkBinaryPalettePayloadsToConsoleColors(
        string payload,
        ConsoleColor expectedColor)
    {
        var output = new FakeDualModeOutput();
        var decoder = new DualModeOutputDecoder(useColor: true, output);

        decoder.Append($"`~[~`{payload}\r\n");
        decoder.Flush();

        Assert.Empty(output.Text);
        Assert.Equal([expectedColor], output.ForegroundColors);
    }

    [Fact]
    public void ConsumesControlRecordTerminatingCrLf()
    {
        var output = new FakeDualModeOutput();
        var decoder = new DualModeOutputDecoder(useColor: true, output);

        decoder.Append(
            "before\r\n" +
            "`~[~`1001\r\n" +
            "error\r\n" +
            "`~[~`Reset\r\n" +
            "after\r\n");
        decoder.Flush();

        Assert.Equal("before\r\nerror\r\nafter\r\n", output.Text);
    }

    [Fact]
    public void IgnoresPayloadSuffixWithoutApplyingBackgroundColor()
    {
        var output = new FakeDualModeOutput();
        var decoder = new DualModeOutputDecoder(useColor: true, output);

        decoder.Append("`~[~`11111111\r\ntext");
        decoder.Flush();

        Assert.Equal("text", output.Text);
        Assert.Equal([ConsoleColor.White], output.ForegroundColors);
    }

    [Fact]
    public async Task ResetsColorOnEveryExitPath()
    {
        await VerifyResetAsync(
            CreateUtf16Stream("plain text"),
            configure: null,
            cancellationToken: CancellationToken.None);

        await VerifyResetAsync(
            CreateUtf16Stream("unreached"),
            configure: null,
            expectedCancellation: true,
            new CancellationToken(canceled: true));

        var invalidUtf16 = new FakeDualModeOutput();
        var invalidDecoder = new DualModeOutputDecoder(useColor: true, invalidUtf16);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => invalidDecoder.DecodeAsync(
            new MemoryStream([0x41]),
            CancellationToken.None));
        Assert.Equal(1, invalidUtf16.ResetCount);

        var outputFailure = new FakeDualModeOutput
        {
            ThrowOnWrite = true
        };
        var outputFailureDecoder = new DualModeOutputDecoder(
            useColor: true,
            outputFailure);
        await Assert.ThrowsAsync<IOException>(() => outputFailureDecoder.DecodeAsync(
            CreateUtf16Stream("text"),
            CancellationToken.None));
        Assert.Equal(1, outputFailure.ResetCount);

        var colorFailure = new FakeDualModeOutput
        {
            ThrowOnColor = true
        };
        var colorFailureDecoder = new DualModeOutputDecoder(
            useColor: true,
            colorFailure);
        await Assert.ThrowsAsync<InvalidOperationException>(() => colorFailureDecoder.DecodeAsync(
            CreateUtf16Stream("`~[~`1001\r\n"),
            CancellationToken.None));
        Assert.Equal(1, colorFailure.ResetCount);
    }

    [Fact]
    public void RedirectedOutputConsumesControlsWithoutChangingColor()
    {
        var output = new FakeDualModeOutput
        {
            IsRedirected = true
        };
        var decoder = new DualModeOutputDecoder(useColor: true, output);

        decoder.Append("`~[~`1001\r\nError\r\n`~[~`Reset\r\n");
        decoder.Flush();

        Assert.Equal("Error\r\n", output.Text);
        Assert.Empty(output.ForegroundColors);
        Assert.Equal(0, output.ResetCount);
    }

    [Theory]
    [MemberData(nameof(MalformedControlRecords))]
    public void LeavesMalformedControlRecordsDeterministic(string input)
    {
        var output = new FakeDualModeOutput();
        var decoder = new DualModeOutputDecoder(useColor: true, output);

        decoder.Append(input);
        decoder.Flush();

        Assert.Equal(input, output.Text);
        Assert.Empty(output.ForegroundColors);
    }

    public static TheoryData<string> MalformedControlRecords =>
    [
        "`~[~`12xx\r\n",
        "`~[~`100",
        "`~[~`1001\n",
        "`~[~`Res"
    ];

    private static async Task VerifyResetAsync(
        Stream stream,
        Action<FakeDualModeOutput>? configure,
        bool expectedCancellation = false,
        CancellationToken cancellationToken = default)
    {
        var output = new FakeDualModeOutput();
        configure?.Invoke(output);
        var decoder = new DualModeOutputDecoder(useColor: true, output);

        if (expectedCancellation)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                decoder.DecodeAsync(stream, cancellationToken));
        }
        else
        {
            await decoder.DecodeAsync(stream, cancellationToken);
        }

        Assert.Equal(1, output.ResetCount);
    }

    private static MemoryStream CreateUtf16Stream(string text)
    {
        var encoding = new UnicodeEncoding(
            bigEndian: false,
            byteOrderMark: false,
            throwOnInvalidBytes: true);
        return new MemoryStream(encoding.GetBytes(text));
    }

    private sealed class FakeDualModeOutput : IDualModeOutput
    {
        private readonly StringBuilder _text = new();

        public bool IsRedirected { get; init; }

        public string Text => _text.ToString();

        public List<ConsoleColor> ForegroundColors { get; } = [];

        public int ResetCount { get; private set; }

        public int FlushCount { get; private set; }

        public bool ThrowOnWrite { get; init; }

        public bool ThrowOnColor { get; init; }

        public void Write(ReadOnlySpan<char> text)
        {
            if (ThrowOnWrite)
            {
                throw new IOException("Injected output failure.");
            }

            _text.Append(text);
        }

        public void SetForegroundColor(ConsoleColor color)
        {
            if (ThrowOnColor)
            {
                throw new InvalidOperationException("Injected color failure.");
            }

            ForegroundColors.Add(color);
        }

        public void ResetColor() => ResetCount++;

        public void Flush() => FlushCount++;
    }
}