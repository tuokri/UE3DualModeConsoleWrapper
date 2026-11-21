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

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

if (args is not
    [
        "--editor-path",
        var editorPath,
        "--working-directory",
        var workingDirectory,
        "--arguments-json",
        var argumentsJson
    ])
{
    Console.Error.WriteLine(
        "Usage: UE3DualModeConsoleWrapper --editor-path <path> " +
        "--working-directory <path> --arguments-json <json-array>");
    return 2;
}

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("The UE3 dual-mode console wrapper requires Windows.");
    return 2;
}

try
{
    var editorArguments = JsonSerializer.Deserialize<string[]>(argumentsJson)
                          ?? throw new JsonException("The target arguments must be a JSON string array.");
    if (editorArguments.Any(static argument => argument is null))
    {
        throw new JsonException("Target arguments cannot contain null values.");
    }

    return await Ue3.DualModeConsoleWrapper.DualModeLauncher
        .RunAsync(editorPath, workingDirectory, editorArguments)
        .ConfigureAwait(false);
}
catch (Exception exception) when (
    exception is ArgumentException or
        IOException or
        InvalidOperationException or
        JsonException or
        PlatformNotSupportedException or
        UnauthorizedAccessException or
        Win32Exception)
{
    Console.Error.WriteLine($"UE3 dual-mode console wrapper failed: {exception.Message}");
    return 1;
}

namespace Ue3.DualModeConsoleWrapper
{

    [SupportedOSPlatform("windows")]
    internal static class DualModeLauncher
    {
        private const uint CreateSuspended = 0x00000004;
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

        public static async Task<int> RunAsync(
            string applicationPath,
            string workingDirectory,
            string[] arguments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(applicationPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
            ArgumentNullException.ThrowIfNull(arguments);

            var started = StartSuspended(applicationPath, workingDirectory, arguments);
            using var nativeProcess = started.Process;
            using var nativeThread = started.Thread;

            Process? process = null;
            Task? processExit = null;
            Task? pipeConnection = null;
            CancellationTokenSource? connectionCancellation = null;
            var resumed = false;

            try
            {
                process = Process.GetProcessById(checked((int)started.ProcessId));
                await using var pipe = new NamedPipeServerStream(
                    started.ProcessId.ToString(CultureInfo.InvariantCulture) + "cout",
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                    inBufferSize: 1024,
                    outBufferSize: 1024);
                using var outputClosedEvent = new EventWaitHandle(
                    initialState: false,
                    EventResetMode.AutoReset,
                    "dualmode_die_event" + started.ProcessId.ToString(CultureInfo.InvariantCulture));
                // The target signals this event when it closes its output-pipe client.
                _ = outputClosedEvent;

                connectionCancellation = new CancellationTokenSource();
                pipeConnection = pipe.WaitForConnectionAsync(connectionCancellation.Token);
                processExit = process.WaitForExitAsync();

                var previousSuspendCount = NativeMethods.ResumeThread(nativeThread.DangerousGetHandle());
                if (previousSuspendCount == uint.MaxValue)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to resume the target process.");
                }
                resumed = true;
                nativeThread.Dispose();

                var firstCompleted = await Task.WhenAny(pipeConnection, processExit).ConfigureAwait(false);
                if (firstCompleted == processExit && !pipe.IsConnected && !pipeConnection.IsCompleted)
                {
                    connectionCancellation.Cancel();
                    try
                    {
                        await pipeConnection.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (connectionCancellation.IsCancellationRequested)
                    {
                    }

                    await processExit.ConfigureAwait(false);
                    if (process.ExitCode == 0)
                    {
                        throw new InvalidOperationException(
                            "The target exited without connecting to its dual-mode output pipe.");
                    }

                    return process.ExitCode;
                }

                await pipeConnection.ConfigureAwait(false);
                var output = ForwardOutputAsync(pipe);
                await Task.WhenAll(output, processExit).ConfigureAwait(false);
                return process.ExitCode;
            }
            catch (Exception operationError)
            {
                connectionCancellation?.Cancel();
                await ObservePendingConnectionAsync(pipeConnection).ConfigureAwait(false);
                try
                {
                    await StopProcessAsync(process, nativeProcess, resumed, processExit).ConfigureAwait(false);
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException(
                        "The target process failed and the wrapper could not stop it cleanly.",
                        operationError,
                        cleanupError);
                }

                throw;
            }
            finally
            {
                connectionCancellation?.Dispose();
                process?.Dispose();
            }
        }

        private static unsafe StartedProcessHandles StartSuspended(
            string applicationPath,
            string workingDirectory,
            string[] arguments)
        {
            var commandLine = BuildCommandLine(applicationPath, arguments);
            char[] commandLineBuffer = [.. commandLine, '\0'];
            var startupInfo = new NativeMethods.StartupInfo
            {
                Size = checked((uint)Marshal.SizeOf<NativeMethods.StartupInfo>())
            };
            NativeMethods.ProcessInformation processInformation;

            fixed (char* commandLinePointer = commandLineBuffer)
            {
                var started = NativeMethods.CreateProcess(
                    applicationPath,
                    commandLinePointer,
                    nint.Zero,
                    nint.Zero,
                    inheritHandles: false,
                    CreateSuspended,
                    nint.Zero,
                    workingDirectory,
                    ref startupInfo,
                    out processInformation);
                if (!started)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to start the target process.");
                }
            }

            return new StartedProcessHandles(
                new SafeProcessHandle(processInformation.Process, ownsHandle: true),
                new SafeWaitHandle(processInformation.Thread, ownsHandle: true),
                processInformation.ProcessId);
        }

        private static string BuildCommandLine(string applicationPath, IEnumerable<string> arguments)
        {
            var commandLine = new StringBuilder(QuoteArgument(applicationPath));
            foreach (var argument in arguments)
            {
                commandLine.Append(' ').Append(QuoteArgument(argument));
            }

            return commandLine.ToString();
        }

        private static string QuoteArgument(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            {
                return argument;
            }

            var quoted = new StringBuilder().Append('"');
            var backslashCount = 0;
            foreach (var character in argument)
            {
                if (character == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (character == '"')
                {
                    quoted.Append('\\', (backslashCount * 2) + 1).Append('"');
                    backslashCount = 0;
                    continue;
                }

                quoted.Append('\\', backslashCount).Append(character);
                backslashCount = 0;
            }

            quoted.Append('\\', backslashCount * 2).Append('"');
            return quoted.ToString();
        }

        private static async Task ForwardOutputAsync(Stream pipe)
        {
            var decoder = new DualModeOutputDecoder();
            using var reader = new StreamReader(pipe, Encoding.Unicode, detectEncodingFromByteOrderMarks: false);
            var buffer = new char[512];
            int charactersRead;
            while ((charactersRead = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                decoder.Append(buffer.AsSpan(0, charactersRead));
                Console.Out.Flush();
            }

            decoder.Flush();
            if (!Console.IsOutputRedirected)
            {
                Console.ResetColor();
            }
            Console.Out.Flush();
        }

        private static async Task ObservePendingConnectionAsync(Task? connection)
        {
            if (connection is null)
            {
                return;
            }

            try
            {
                await connection.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static async Task StopProcessAsync(
            Process? process,
            SafeProcessHandle nativeProcess,
            bool resumed,
            Task? processExit)
        {
            if (process is null)
            {
                if (!NativeMethods.TerminateProcess(nativeProcess.DangerousGetHandle(), exitCode: 1))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Failed to terminate the suspended target process.");
                }

                var waitResult = NativeMethods.WaitForSingleObject(
                    nativeProcess.DangerousGetHandle(),
                    milliseconds: uint.MaxValue);
                if (waitResult == uint.MaxValue)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Failed to wait for the suspended target process to exit.");
                }

                return;
            }

            try
            {
                if (resumed)
                {
                    process.Kill(entireProcessTree: true);
                }
                else if (!NativeMethods.TerminateProcess(nativeProcess.DangerousGetHandle(), exitCode: 1))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Failed to terminate the suspended target process.");
                }
            }
            catch (InvalidOperationException) when (processExit?.IsCompleted is true)
            {
            }
            catch (Win32Exception) when (processExit?.IsCompleted is true)
            {
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
        }

        private readonly record struct StartedProcess(
            SafeProcessHandle Process,
            SafeWaitHandle Thread,
            uint ProcessId);

        private sealed class DualModeOutputDecoder
        {
            private const string ColorMarker = "\u0060~[~\u0060";
            private readonly StringBuilder _pending = new();

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
                    var markerIndex = text.IndexOf(ColorMarker, position, StringComparison.Ordinal);
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
                        if (!Console.IsOutputRedirected)
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
                        if ((uint)colorIndex < (uint)ColorPalette.Length &&
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
    }

    internal readonly record struct StartedProcessHandles(
        SafeProcessHandle Process,
        SafeWaitHandle Thread,
        uint ProcessId);

    internal static partial class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct StartupInfo
        {
            public uint Size;
            public nint Reserved;
            public nint Desktop;
            public nint Title;
            public uint X;
            public uint Y;
            public uint XSize;
            public uint YSize;
            public uint XCountChars;
            public uint YCountChars;
            public uint FillAttribute;
            public uint Flags;
            public ushort ShowWindow;
            public ushort Reserved2Size;
            public nint Reserved2;
            public nint StandardInput;
            public nint StandardOutput;
            public nint StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessInformation
        {
            public nint Process;
            public nint Thread;
            public uint ProcessId;
            public uint ThreadId;
        }

        [LibraryImport(
            "kernel32.dll",
            EntryPoint = "CreateProcessW",
            StringMarshalling = StringMarshalling.Utf16,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static unsafe partial bool CreateProcess(
            string applicationName,
            char* commandLine,
            nint processAttributes,
            nint threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            nint environment,
            string currentDirectory,
            ref StartupInfo startupInfo,
            out ProcessInformation processInformation);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial uint ResumeThread(nint thread);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TerminateProcess(nint process, uint exitCode);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial uint WaitForSingleObject(nint handle, uint milliseconds);
    }
}
