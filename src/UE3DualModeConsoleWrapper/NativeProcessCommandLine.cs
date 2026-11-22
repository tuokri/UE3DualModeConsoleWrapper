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

internal static class NativeProcessCommandLine
{
    public static string Build(string executablePath, IEnumerable<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        RejectEmbeddedNul(executablePath, nameof(executablePath));

        var commandLine = new StringBuilder(QuoteArgument(executablePath));
        foreach (var argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            RejectEmbeddedNul(argument, nameof(arguments));
            commandLine.Append(' ').Append(QuoteArgument(argument));
        }

        return commandLine.ToString();
    }

    internal static string QuoteArgument(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        RejectEmbeddedNul(argument, nameof(argument));

        if (argument.Length > 0 &&
            argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return argument;
        }

        var quoted = new StringBuilder().Append('"');
        var backslashCount = 0;
        foreach (var character in argument)
        {
            switch (character)
            {
                case '\\':
                    backslashCount++;
                    continue;
                case '"':
                    quoted.Append('\\', (backslashCount * 2) + 1).Append('"');
                    backslashCount = 0;
                    continue;
                default:
                    quoted.Append('\\', backslashCount).Append(character);
                    backslashCount = 0;
                    break;
            }
        }

        quoted.Append('\\', backslashCount * 2).Append('"');
        return quoted.ToString();
    }

    private static void RejectEmbeddedNul(string value, string parameterName)
    {
        if (value.Contains('\0'))
        {
            throw new ArgumentException(
                "Native command-line values cannot contain an embedded NUL character.",
                parameterName);
        }
    }
}