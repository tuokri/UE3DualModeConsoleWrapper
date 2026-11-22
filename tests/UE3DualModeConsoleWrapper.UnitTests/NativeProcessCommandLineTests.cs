using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Xunit;

namespace UE3DualModeConsoleWrapper.UnitTests;

[SupportedOSPlatform("windows")]
public sealed partial class NativeProcessCommandLineTests
{
    public static TheoryData<string, string> QuotingCases => new()
    {
        { "ordinary", "ordinary" },
        { "", "\"\"" },
        { "with space", "\"with space\"" },
        { "with\ttab", "\"with\ttab\"" },
        { "with\"quote", "\"with\\\"quote\"" },
        { @"C:\without spaces", "\"C:\\without spaces\"" },
        { @"leading\", @"leading\" },
        { "before\\quote\"end", "\"before\\quote\\\"end\"" },
        { "backslash\\\\before \"quote", "\"backslash\\\\before \\\"quote\"" }
    };

    [Theory]
    [MemberData(nameof(QuotingCases))]
    public void QuotesWindowsCommandLineTokens(string argument, string expected)
    {
        var actual = NativeProcessCommandLine.QuoteArgument(argument);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    public void RejectsEmbeddedNulCharacters(bool inExecutable, int argumentIndex)
    {
        const string executable = "target.exe";
        var arguments = new[] { "first", "middle", "last" };
        if (inExecutable)
        {
            Assert.Throws<ArgumentException>(
                () => NativeProcessCommandLine.Build(
                    "target\0.exe",
                    arguments));
            return;
        }

        arguments[argumentIndex] = "value\0suffix";
        Assert.Throws<ArgumentException>(
            () => NativeProcessCommandLine.Build(
                executable,
                arguments));
    }

    [Fact]
    public void RoundTripsThroughCommandLineToArgvW()
    {
        const string executable = @"C:\Program Files\FakeUDK.exe";
        string[] arguments =
        [
            "",
            "ordinary",
            "with space",
            "with\ttab",
            "quote\"inside",
            @"leading\",
            @"trailing\\",
            "--",
            "-flag"
        ];

        var recovered = ParseCommandLine(
            NativeProcessCommandLine.Build(executable, arguments));

        Assert.Equal([executable, .. arguments], recovered);
    }

    private static string[] ParseCommandLine(string commandLine)
    {
        var argumentsPointer = CommandLineToArgvW(commandLine, out var argumentCount);
        if (argumentsPointer == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            var arguments = new string[argumentCount];
            for (var index = 0; index < argumentCount; index++)
            {
                var valuePointer = Marshal.ReadIntPtr(
                    argumentsPointer,
                    index * nint.Size);
                arguments[index] = Marshal.PtrToStringUni(valuePointer) ??
                                   throw new InvalidOperationException(
                                       "CommandLineToArgvW returned a null argument.");
            }

            return arguments;
        }
        finally
        {
            _ = LocalFree(argumentsPointer);
        }
    }

    [LibraryImport(
        "shell32.dll",
        EntryPoint = "CommandLineToArgvW",
        StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    private static partial nint CommandLineToArgvW(
        string commandLine,
        out int argumentCount);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint LocalFree(nint memory);
}
