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

using System.Globalization;
using System.Text;

namespace UE3DualModeConsoleWrapper;

internal sealed class DualModeOutputDecoder
{
    private const string ColorMarker = "\u0060~[~\u0060";
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
            var remaining = text.Length - commandStart;
            var commandPrefix = text.Substring(commandStart, Math.Min(5, remaining));
            if (commandPrefix.StartsWith("Reset", StringComparison.Ordinal))
            {
                if (_useColor && !Console.IsOutputRedirected)
                {
                    Console.ResetColor();
                }

                position = commandStart + 5;
                continue;
            }

            if (remaining < 4 ||
                (!final && "Reset".StartsWith(commandPrefix, StringComparison.Ordinal)))
            {
                if (final)
                {
                    WriteText(text.AsSpan(markerIndex));
                    position = text.Length;
                }
                else
                {
                    position = markerIndex;
                }

                break;
            }

            var colorValue = text.AsSpan(commandStart, 4);
            if (Int32.TryParse(
                    colorValue,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var colorIndex))
            {
                if (_useColor &&
                    (uint)colorIndex < (uint)ColorPalette.Length &&
                    !Console.IsOutputRedirected)
                {
                    Console.ForegroundColor = ColorPalette[colorIndex];
                }

                position = commandStart + 4;
                continue;
            }

            WriteText(ColorMarker.AsSpan());
            position = commandStart;
        }

        _pending.Clear();
        if (position < text.Length)
        {
            _pending.Append(text.AsSpan(position));
        }
    }

    private static void WriteText(ReadOnlySpan<char> text)
    {
        if (!text.IsEmpty)
        {
            Console.Out.Write(text);
        }
    }
}
