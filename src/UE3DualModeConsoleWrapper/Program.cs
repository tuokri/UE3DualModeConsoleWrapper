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

using System.CommandLine;
using System.ComponentModel;

var executableOption = new Option<string>("--executable")
{
    Description = "Path to the UE3 dual-mode target executable.",
    HelpName = "path",
    Required = true
};
var workingDirectoryOption = new Option<string?>("--working-directory")
{
    Description = "Working directory for the target process. Defaults to the executable directory.",
    HelpName = "path"
};
var noColorOption = new Option<bool>("--no-color")
{
    Description = "Disable console color control records."
};
var targetArgumentsArgument = new Argument<string[]>("target-arguments")
{
    Arity = ArgumentArity.ZeroOrMore,
    Description = "Arguments forwarded to the target after '--'."
};

var rootCommand = new RootCommand(
    "Runs a UE3 dual-mode console target and forwards its output.")
{
    TreatUnmatchedTokensAsErrors = true
};
rootCommand.Options.Add(executableOption);
rootCommand.Options.Add(workingDirectoryOption);
rootCommand.Options.Add(noColorOption);
rootCommand.Arguments.Add(targetArgumentsArgument);
rootCommand.Validators.Add(result =>
{
    var requestsMetadata = args.Any(static argument =>
        argument is "--help" or "-h" or "-?" or "--version");
    if (!requestsMetadata && !args.Contains("--", StringComparer.Ordinal))
    {
        result.AddError("Specify '--' before target arguments.");
    }
});
rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("The UE3 dual-mode console wrapper requires Windows.");
        return UE3DualModeConsoleWrapper.ExitCode.RuntimeError;
    }

    try
    {
        var executablePath = parseResult.GetRequiredValue(executableOption);
        var workingDirectory = parseResult.GetValue(workingDirectoryOption) ??
                               Path.GetDirectoryName(Path.GetFullPath(executablePath)) ??
                               Environment.CurrentDirectory;

        var noColor = parseResult.GetValue(noColorOption)
                      || Environment.GetEnvironmentVariable("NO_COLOR") is not null;

        return await UE3DualModeConsoleWrapper.DualModeLauncher
            .RunAsync(
                executablePath,
                workingDirectory,
                parseResult.GetValue(targetArgumentsArgument) ?? [],
                noColor,
                cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        Console.Error.WriteLine("The operation was cancelled.");
        return UE3DualModeConsoleWrapper.ExitCode.Cancelled;
    }
    catch (Exception exception) when (
        exception is ArgumentException
            or IOException
            or InvalidOperationException
            or PlatformNotSupportedException
            or UnauthorizedAccessException
            or Win32Exception)
    {
        Console.Error.WriteLine($"UE3 dual-mode console wrapper failed: {exception.Message}");
        return UE3DualModeConsoleWrapper.ExitCode.RuntimeError;
    }
});

var parseResult = rootCommand.Parse(args);
var exitCode = await parseResult.InvokeAsync();
return parseResult.Errors.Count > 0
    ? UE3DualModeConsoleWrapper.ExitCode.UsageError
    : exitCode;

namespace UE3DualModeConsoleWrapper
{
    internal static class ExitCode
    {
        public const int Success = 0;
        public const int RuntimeError = 1;
        public const int UsageError = 2;
        public const int Cancelled = 130;
    }
}