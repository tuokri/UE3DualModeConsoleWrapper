// Copyright (C) 2026 Tuomo Kriikkula
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Text;

namespace UE3DualModeConsoleWrapper;

internal sealed class DualModeOutputDecoder(
    bool useColor,
    IDualModeOutput? output = null)
{
    private const string ColorMarker = "\u0060~[~\u0060";
    private const string ResetCommand = "Reset";
    private const string RecordTerminator = "\r\n";
    private const int PalettePayloadLength = 4;
    private const char InactiveComponent = '0';
    private const char ActiveComponent = '1';

    private static readonly ConsoleColor[] ColorPalette =
    [
        ConsoleColor.Black,
        ConsoleColor.Black,
        ConsoleColor.DarkBlue,
        ConsoleColor.Blue,
        ConsoleColor.DarkGreen,
        ConsoleColor.Green,
        ConsoleColor.DarkCyan,
        ConsoleColor.Cyan,
        ConsoleColor.DarkRed,
        ConsoleColor.Red,
        ConsoleColor.DarkMagenta,
        ConsoleColor.Magenta,
        ConsoleColor.DarkYellow,
        ConsoleColor.Yellow,
        ConsoleColor.Gray,
        ConsoleColor.White
    ];

    private readonly StringBuilder _pending = new();
    private readonly IDualModeOutput _output = output ?? ConsoleDualModeOutput.Instance;

    public async Task DecodeAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            stream,
            Utf16LeEncoding.Instance,
            detectEncodingFromByteOrderMarks: false);
        var buffer = new char[512];

        try
        {
            int charactersRead;
            while ((charactersRead = await reader
                       .ReadAsync(buffer.AsMemory(), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                Append(buffer.AsSpan(0, charactersRead));
                _output.Flush();
            }

            Flush();
            _output.Flush();
        }
        finally
        {
            if (useColor && !_output.IsRedirected)
            {
                _output.ResetColor();
            }
        }
    }

    public void Append(ReadOnlySpan<char> characters)
    {
        _pending.Append(characters);
        ProcessPending(final: false);
    }

    public void Flush() => ProcessPending(final: true);

    private void ProcessPending(bool final)
    {
        var text = _pending.ToString();
        var position = 0;
        while (position < text.Length)
        {
            var markerIndex = text.IndexOf(
                ColorMarker,
                position,
                StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                var safeLength = final
                    ? text.Length - position
                    : Math.Max(0, text.Length - position - ColorMarker.Length + 1);
                WriteText(text.AsSpan(position, safeLength));
                position += safeLength;
                break;
            }

            WriteText(text.AsSpan(position, markerIndex - position));
            var commandStart = markerIndex + ColorMarker.Length;
            var command = text.AsSpan(commandStart);
            var recordState = TryParseControlRecord(
                command,
                final,
                out var recordLength,
                out var reset,
                out var color);
            if (recordState == ControlRecordState.Incomplete)
            {
                position = markerIndex;
                break;
            }

            if (recordState == ControlRecordState.Invalid)
            {
                WriteText(ColorMarker.AsSpan());
                position = commandStart;
                continue;
            }

            if (useColor && !_output.IsRedirected)
            {
                if (reset)
                {
                    _output.ResetColor();
                }
                else
                {
                    _output.SetForegroundColor(color);
                }
            }

            position = commandStart + recordLength;
        }

        _pending.Clear();
        if (position < text.Length)
        {
            _pending.Append(text.AsSpan(position));
        }
    }

    private static ControlRecordState TryParseControlRecord(
        ReadOnlySpan<char> command,
        bool final,
        out int recordLength,
        out bool reset,
        out ConsoleColor color)
    {
        recordLength = 0;
        reset = false;
        color = ConsoleColor.Black;

        var resetPrefixLength = Math.Min(ResetCommand.Length, command.Length);
        if (command[..resetPrefixLength].Equals(
                ResetCommand.AsSpan(0, resetPrefixLength),
                StringComparison.OrdinalIgnoreCase))
        {
            if (command.Length < ResetCommand.Length)
            {
                return final
                    ? ControlRecordState.Invalid
                    : ControlRecordState.Incomplete;
            }

            var resetTerminatorLength = TryParseTerminator(
                command[ResetCommand.Length..],
                final,
                out var resetTerminatorState);
            if (resetTerminatorState == ControlRecordState.Valid)
            {
                recordLength = ResetCommand.Length + resetTerminatorLength;
                reset = true;
                return ControlRecordState.Valid;
            }

            return resetTerminatorState;
        }

        var terminatorIndex = command.IndexOf(RecordTerminator, StringComparison.Ordinal);
        if (terminatorIndex < 0)
        {
            return final
                ? ControlRecordState.Invalid
                : ControlRecordState.Incomplete;
        }

        var payload = command[..terminatorIndex];
        if (payload.Length < PalettePayloadLength)
        {
            return ControlRecordState.Invalid;
        }

        var paletteIndex = 0;
        foreach (var component in payload[..PalettePayloadLength])
        {
            paletteIndex <<= 1;
            if (component == ActiveComponent)
            {
                paletteIndex |= 1;
            }
            else if (component != InactiveComponent)
            {
                return ControlRecordState.Invalid;
            }
        }

        color = ColorPalette[paletteIndex];
        recordLength = terminatorIndex + RecordTerminator.Length;
        return ControlRecordState.Valid;
    }

    private static int TryParseTerminator(
        ReadOnlySpan<char> remaining,
        bool final,
        out ControlRecordState state)
    {
        var terminatorPrefixLength = Math.Min(
            RecordTerminator.Length,
            remaining.Length);
        if (!remaining[..terminatorPrefixLength].Equals(
                RecordTerminator.AsSpan(0, terminatorPrefixLength),
                StringComparison.Ordinal))
        {
            state = ControlRecordState.Invalid;
            return 0;
        }

        if (remaining.Length < RecordTerminator.Length)
        {
            state = final
                ? ControlRecordState.Invalid
                : ControlRecordState.Incomplete;
            return 0;
        }

        state = ControlRecordState.Valid;
        return RecordTerminator.Length;
    }

    private void WriteText(ReadOnlySpan<char> text)
    {
        if (!text.IsEmpty)
        {
            _output.Write(text);
        }
    }

    private enum ControlRecordState
    {
        Invalid,
        Incomplete,
        Valid
    }
}

internal interface IDualModeOutput
{
    bool IsRedirected { get; }

    void Write(ReadOnlySpan<char> text);

    void SetForegroundColor(ConsoleColor color);

    void ResetColor();

    void Flush();
}

internal sealed class ConsoleDualModeOutput : IDualModeOutput
{
    public static ConsoleDualModeOutput Instance { get; } = new();

    public bool IsRedirected => Console.IsOutputRedirected;

    public void Write(ReadOnlySpan<char> text) => Console.Out.Write(text);

    public void SetForegroundColor(ConsoleColor color) =>
        Console.ForegroundColor = color;

    public void ResetColor() => Console.ResetColor();

    public void Flush() => Console.Out.Flush();
}

internal static class Utf16LeEncoding
{
    public static readonly Encoding Instance = new UnicodeEncoding(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);
}