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
using Microsoft.Win32.SafeHandles;

namespace UE3DualModeConsoleWrapper;

[SupportedOSPlatform("windows")]
internal static class DualModeLauncher
{
    private const uint CreateSuspended = 0x00000004;

    public static async Task<int> RunAsync(
        string applicationPath,
        string workingDirectory,
        string[] arguments,
        bool noColor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        using var job = CreateJobObject();
        var started = StartSuspended(applicationPath, workingDirectory, arguments);
        using var nativeProcess = started.Process;
        using var nativeThread = started.Thread;

        Process? process = null;
        Task? processExit = null;
        Task? pipeConnection = null;
        CancellationTokenSource? connectionCancellation = null;
        var resumed = false;
        var forceTerminationStarted = 0;

        try
        {
            if (!NativeMethods.AssignProcessToJobObject(job, nativeProcess))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Failed to assign the suspended target process to the Job Object.");
            }

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
                EventResetMode.ManualReset,
                "dualmode_die_event" + started.ProcessId.ToString(CultureInfo.InvariantCulture));
            // The target signals this event when it closes its output-pipe client.
            using var shutdownEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.ManualReset,
                "ComWrapperShutdown");
            connectionCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var interruptController = new ConsoleInterruptController(
                () =>
                {
                    shutdownEvent.Set();
                    QueueMainWindowClose(process);
                },
                () =>
                    ForceTerminateJob(
                        job,
                        connectionCancellation,
                        ref forceTerminationStarted),
                registerWithConsole: true);
            await using var cancellationRegistration = cancellationToken.Register(() =>
                ForceTerminateJob(
                    job,
                    connectionCancellation,
                    ref forceTerminationStarted));

            pipeConnection = pipe.WaitForConnectionAsync(connectionCancellation.Token);
            processExit = process.WaitForExitAsync(cancellationToken);

            var previousSuspendCount = NativeMethods.ResumeThread(nativeThread.DangerousGetHandle());
            if (previousSuspendCount == uint.MaxValue)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Failed to resume the target process.");
            }

            resumed = true;
            // ReSharper disable once DisposeOnUsingVariable
            nativeThread.Dispose();

            var firstCompleted = await Task.WhenAny(pipeConnection, processExit);
            if (firstCompleted == processExit && !pipe.IsConnected && !pipeConnection.IsCompleted)
            {
                await connectionCancellation.CancelAsync();
                try
                {
                    await pipeConnection;
                }
                catch (OperationCanceledException) when (connectionCancellation.IsCancellationRequested)
                {
                }

                await processExit;
                if (process.ExitCode == ExitCode.Success)
                {
                    throw new InvalidOperationException(
                        "The target exited without connecting to its dual-mode output pipe.");
                }

                return interruptController.ShutdownRequested
                    ? ExitCode.Cancelled
                    : process.ExitCode;
            }

            await pipeConnection;
            var output = ForwardOutputAsync(pipe, noColor, cancellationToken);
            await Task.WhenAll(output, processExit);
            return interruptController.ShutdownRequested
                ? ExitCode.Cancelled
                : process.ExitCode;
        }
        catch (Exception operationError)
        {
            if (connectionCancellation is not null)
            {
                await connectionCancellation.CancelAsync();
            }

            await ObservePendingConnectionAsync(pipeConnection);
            try
            {
                await StopProcessAsync(
                    process,
                    nativeProcess,
                    job,
                    resumed,
                    processExit,
                    forceTerminationStarted);
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

    private static SafeFileHandle CreateJobObject()
    {
        var job = NativeMethods.CreateJobObject(
            jobAttributes: nint.Zero,
            name: null);
        if (job.IsInvalid)
        {
            job.Dispose();
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Failed to create the target Job Object.");
        }

        var information = new NativeMethods.JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new NativeMethods.JobObjectBasicLimitInformation
            {
                LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose
            }
        };
        if (!NativeMethods.SetInformationJobObject(
                job,
                NativeMethods.JobObjectInfoClassExtendedLimitInformation,
                ref information,
                checked((uint)Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>())))
        {
            job.Dispose();
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Failed to configure the target Job Object.");
        }

        return job;
    }

    private static void ForceTerminateJob(
        SafeFileHandle job,
        CancellationTokenSource cancellation,
        ref int forceTerminationStarted)
    {
        if (Interlocked.CompareExchange(ref forceTerminationStarted, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (!NativeMethods.TerminateJobObject(job, ExitCode.Cancelled))
            {
                Volatile.Write(ref forceTerminationStarted, 2);
            }
        }
        catch
        {
            Volatile.Write(ref forceTerminationStarted, 2);
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    private static void QueueMainWindowClose(Process process)
    {
        _ = Task.Run(() =>
        {
            try
            {
                if (process.MainWindowHandle != nint.Zero)
                {
                    process.CloseMainWindow();
                }
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    private static unsafe StartedProcessHandles StartSuspended(
        string applicationPath,
        string workingDirectory,
        string[] arguments)
    {
        var commandLine = NativeProcessCommandLine.Build(applicationPath, arguments);
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
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Failed to start the target process.");
            }
        }

        return new StartedProcessHandles(
            new SafeProcessHandle(processInformation.Process, ownsHandle: true),
            new SafeWaitHandle(processInformation.Thread, ownsHandle: true),
            processInformation.ProcessId);
    }

    private static async Task ForwardOutputAsync(
        Stream pipe,
        bool noColor,
        CancellationToken cancellationToken)
    {
        var decoder = new DualModeOutputDecoder(!noColor);
        await decoder.DecodeAsync(pipe, cancellationToken);
    }

    private static async Task ObservePendingConnectionAsync(Task? connection)
    {
        if (connection is null)
        {
            return;
        }

        try
        {
            await connection;
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
        SafeFileHandle job,
        bool resumed,
        Task? processExit,
        int forceTerminationStarted)
    {
        if (process is null)
        {
            if (!NativeMethods.TerminateProcess(
                    nativeProcess.DangerousGetHandle(),
                    exitCode: ExitCode.RuntimeError))
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
                if (forceTerminationStarted != 1 &&
                    !NativeMethods.TerminateJobObject(job, ExitCode.RuntimeError))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Failed to terminate the target Job Object.");
                }
            }
            else if (!NativeMethods.TerminateProcess(
                         nativeProcess.DangerousGetHandle(),
                         exitCode: ExitCode.RuntimeError))
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

        await process.WaitForExitAsync();
    }

    private readonly record struct StartedProcessHandles(
        SafeProcessHandle Process,
        SafeWaitHandle Thread,
        uint ProcessId);
}