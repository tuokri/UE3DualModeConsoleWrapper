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

internal sealed class DualModeOutputDecoder
{
    private const string ColorMarker = "\u0060~[~\u0060";
    private const string ResetCommand = "Reset";
    private const string RecordTerminator = "\r\n";
    private const int ForegroundAttributeMask = 0x0f;
    private const int BackgroundAttributeShift = 4;
    private const char InactiveComponent = '0';
    private const char ActiveComponent = '1';
    private static readonly ConsoleAttribute[] ConsoleAttributePayloadOrder =
    [
        ConsoleAttribute.ForegroundRed,
        ConsoleAttribute.ForegroundGreen,
        ConsoleAttribute.ForegroundBlue,
        ConsoleAttribute.ForegroundIntensity,
        ConsoleAttribute.BackgroundRed,
        ConsoleAttribute.BackgroundGreen,
        ConsoleAttribute.BackgroundBlue,
        ConsoleAttribute.BackgroundIntensity
    ];

    private readonly StringBuilder _pending = new();
    private readonly bool _useColor;

    public DualModeOutputDecoder(bool useColor)
    {
        _useColor = useColor;
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
                out var consoleAttributes);
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

            if (_useColor && !Console.IsOutputRedirected)
            {
                if (reset)
                {
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor =
                        (ConsoleColor)(consoleAttributes & ForegroundAttributeMask);
                    Console.BackgroundColor = (ConsoleColor)(
                        (consoleAttributes >> BackgroundAttributeShift) &
                        ForegroundAttributeMask);
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
        out int consoleAttributes)
    {
        recordLength = 0;
        reset = false;
        consoleAttributes = 0;

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

        var payloadLength = 0;
        while (payloadLength < command.Length &&
               payloadLength < ConsoleAttributePayloadOrder.Length &&
               command[payloadLength] is InactiveComponent or ActiveComponent)
        {
            payloadLength++;
        }

        if (payloadLength == 0)
        {
            return ControlRecordState.Invalid;
        }

        if (payloadLength == command.Length)
        {
            return final
                ? ControlRecordState.Invalid
                : ControlRecordState.Incomplete;
        }

        if (payloadLength == ConsoleAttributePayloadOrder.Length &&
            command[payloadLength] is InactiveComponent or ActiveComponent)
        {
            return ControlRecordState.Invalid;
        }

        var terminatorLength = TryParseTerminator(
            command[payloadLength..],
            final,
            out var terminatorState);
        if (terminatorState != ControlRecordState.Valid)
        {
            return terminatorState;
        }

        for (var index = 0; index < payloadLength; index++)
        {
            if (command[index] == ActiveComponent)
            {
                consoleAttributes |= (int)ConsoleAttributePayloadOrder[index];
            }
        }

        recordLength = payloadLength + terminatorLength;
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

    private static void WriteText(ReadOnlySpan<char> text)
    {
        if (!text.IsEmpty)
        {
            Console.Out.Write(text);
        }
    }

    private enum ControlRecordState
    {
        Invalid,
        Incomplete,
        Valid
    }

    [Flags]
    private enum ConsoleAttribute
    {
        ForegroundBlue = 0x01,
        ForegroundGreen = 0x02,
        ForegroundRed = 0x04,
        ForegroundIntensity = 0x08,
        BackgroundBlue = 0x10,
        BackgroundGreen = 0x20,
        BackgroundRed = 0x40,
        BackgroundIntensity = 0x80
    }
}
