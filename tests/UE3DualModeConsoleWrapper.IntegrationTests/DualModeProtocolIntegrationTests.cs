using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using System.Text.Json;
using Xunit;

namespace UE3DualModeConsoleWrapper.IntegrationTests;

[SupportedOSPlatform("windows")]
public sealed partial class DualModeProtocolIntegrationTests
{
    [Fact(Timeout = 30_000)]
    public async Task ForwardsArgumentsOutputAndExitCode()
    {
        _ = TestContext.Current.CancellationToken;
        var resultPath = CreateMetadataPath("forwarding");
        var targetArguments = new[]
        {
            "--fake-metadata", resultPath,
            "--fake-delay-ms", "200",
            "--fake-close-delay-ms", "150",
            "--fake-exit-code", "7",
            "make",
            "-useunpublished"
        };

        var result = await RunWrapperAsync(targetArguments);
        var metadata = await ReadMetadataAsync(resultPath);

        Assert.Equal(7, result.ExitCode);
        Assert.Contains("Log: Executing Class UnrealEd.MakeCommandlet", result.Output);
        Assert.Equal(targetArguments, metadata.Arguments);
        Assert.True(metadata.PipeConnected);
        Assert.True(metadata.PipeClosed);
        Assert.Equal(4, metadata.CompletedStages);
        Assert.Equal(200, metadata.SelectedDelayMilliseconds);
    }

    [Fact(Timeout = 30_000)]
    public async Task HandlesExitBeforePipeConnection()
    {
        _ = TestContext.Current.CancellationToken;
        var zeroResultPath = CreateMetadataPath("no-connect-zero");
        var zero = await RunWrapperAsync(
            "--fake-no-connect",
            "--fake-metadata",
            zeroResultPath);

        var sevenResultPath = CreateMetadataPath("no-connect-seven");
        var seven = await RunWrapperAsync(
            "--fake-no-connect",
            "--fake-exit-code",
            "7",
            "--fake-metadata",
            sevenResultPath);

        Assert.Equal(1, zero.ExitCode);
        Assert.Contains(
            "target exited without connecting",
            zero.Output,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7, seven.ExitCode);
        Assert.Empty(seven.Output);
    }

    [Fact(Timeout = 30_000)]
    public async Task FakeUDKFallsBackToConsoleWhenPipeIsAbsent()
    {
        _ = TestContext.Current.CancellationToken;
        var resultPath = CreateMetadataPath("standalone-fallback");
        var result = await RunFakeUDKAsync(
            "--fake-delay-ms",
            "200",
            "--fake-metadata",
            resultPath,
            "make");
        var metadata = await ReadMetadataAsync(resultPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "Log: Executing Class UnrealEd.MakeCommandlet",
            result.Output);
        Assert.True(metadata.UsedConsoleFallback);
        Assert.False(metadata.PipeConnected);
    }

    [Fact(Timeout = 30_000)]
    public async Task SupportsTwoStageConsoleInterruption()
    {
        _ = TestContext.Current.CancellationToken;
        var resultPath = CreateMetadataPath("two-stage");
        using var wrapper = StartWrapperProcessGroup(
            "--fake-block",
            "--fake-descendant",
            "--fake-hold-after-shutdown",
            "--fake-metadata",
            resultPath);

        try
        {
            var connected = await WaitForMetadataAsync(
                resultPath,
                value => value is
                         {
                             EventsOpened: true,
                             PipeConnected: true,
                             ChildPid: not null,
                             ChildReadyPath: not null
                         } &&
                         File.Exists(value.ChildReadyPath));
            var targetPid = connected.Pid;
            Debug.Assert(connected.ChildPid != null);
            var childPid = connected.ChildPid.Value;

            GenerateConsoleCtrlEvent(CtrlBreakEvent, (uint)wrapper.Id);
            _ = await WaitForMetadataAsync(
                resultPath,
                value => value is { ShutdownRequested: true, State: "shutdown-held" });

            Assert.False(wrapper.HasExited);
            AssertProcessAlive(targetPid);
            AssertProcessAlive(childPid);

            GenerateConsoleCtrlEvent(CtrlBreakEvent, (uint)wrapper.Id);
            await wrapper.WaitForExitAsync(TestContext.Current.CancellationToken);

            Assert.Equal(ExitCode.Cancelled, wrapper.ExitCode);
            AssertProcessGone(targetPid);
            AssertProcessGone(childPid);
        }
        finally
        {
            if (!wrapper.HasExited)
            {
                wrapper.Kill();
                await wrapper.WaitForExitAsync(
                    TestContext.Current.CancellationToken);
            }
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task TerminatesStartedDescendants()
    {
        _ = TestContext.Current.CancellationToken;
        await VerifyAbruptJobCleanupAsync();
    }

    private static async Task VerifyAbruptJobCleanupAsync()
    {
        var resultPath = CreateMetadataPath("abrupt-job-close");
        var wrapper = StartWrapper(
            "--fake-descendant",
            "--fake-block",
            "--fake-metadata",
            resultPath);

        try
        {
            var metadata = await WaitForMetadataAsync(
                resultPath,
                value => value.ChildPid.HasValue);
            var parentPid = metadata.Pid;
            Debug.Assert(metadata.ChildPid != null);
            var childPid = metadata.ChildPid.Value;

            wrapper.Kill();
            await wrapper.WaitForExitAsync(TestContext.Current.CancellationToken);
            await Task.Delay(250, TestContext.Current.CancellationToken);

            AssertProcessGone(parentPid);
            AssertProcessGone(childPid);
        }
        finally
        {
            if (!wrapper.HasExited)
            {
                wrapper.Kill();
                await wrapper.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
        }
    }

    private static Process StartWrapper(params string[] targetArguments) =>
        StartProcess(
            GetTestExecutable("UE3DualModeConsoleWrapper.exe"),
            ["--executable", GetTestExecutable("FakeUDK.exe"), "--", .. targetArguments]);

    private static Process StartWrapperProcessGroup(params string[] targetArguments)
    {
        var wrapperPath = GetTestExecutable("UE3DualModeConsoleWrapper.exe");
        var commandLine = NativeProcessCommandLine.Build(
            wrapperPath,
            [
                "--executable",
                GetTestExecutable("FakeUDK.exe"),
                "--",
                .. targetArguments
            ]);
        var commandLineBuffer = (commandLine + '\0').ToCharArray();
        var startupInfo = new StartupInfo
        {
            Size = checked((uint)Marshal.SizeOf<StartupInfo>())
        };

        unsafe
        {
            fixed (char* commandLinePointer = commandLineBuffer)
            {
                if (!CreateProcess(
                        wrapperPath,
                        commandLinePointer,
                        nint.Zero,
                        nint.Zero,
                        inheritHandles: false,
                        CreateNewProcessGroup,
                        nint.Zero,
                        AppContext.BaseDirectory,
                        ref startupInfo,
                        out var processInformation))
                {
                    throw new Win32Exception(
                        Marshal.GetLastPInvokeError(),
                        "Failed to start wrapper process group.");
                }

                using var threadHandle = new SafeWaitHandle(
                    processInformation.Thread,
                    ownsHandle: true);
                var processHandle = new SafeWaitHandle(
                    processInformation.Process,
                    ownsHandle: true);
                try
                {
                    return Process.GetProcessById(
                        checked((int)processInformation.ProcessId));
                }
                finally
                {
                    processHandle.Dispose();
                }
            }
        }
    }

    private static async Task<TestProcessResult> RunWrapperAsync(
        params string[] targetArguments)
    {
        using var process = StartWrapper(targetArguments);
        var outputTask = process.StandardOutput.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return new TestProcessResult(
            process.ExitCode,
            await outputTask + await errorTask);
    }

    private static async Task<TestProcessResult> RunFakeUDKAsync(
        params string[] arguments)
    {
        using var process = StartProcess(
            GetTestExecutable("FakeUDK.exe"),
            arguments);
        var outputTask = process.StandardOutput.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return new TestProcessResult(
            process.ExitCode,
            await outputTask + await errorTask);
    }

    private static Process StartProcess(
        string executable,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo) ??
               throw new InvalidOperationException($"Failed to start {executable}.");
    }

    private static string GetTestExecutable(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        Assert.True(File.Exists(path), $"Expected test executable at '{path}'.");
        return path;
    }

    private static string CreateMetadataPath(string name) =>
        Path.Combine(
            Path.GetTempPath(),
            "UE3DualModeConsoleWrapper",
            Guid.NewGuid() + "-" + name + ".json");

    private static async Task<FakeUDKMetadata> ReadMetadataAsync(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(path) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(path), $"Metadata file was not created: {path}");
        return JsonSerializer.Deserialize<FakeUDKMetadata>(
                   await File.ReadAllTextAsync(
                       path,
                       TestContext.Current.CancellationToken)) ??
               throw new InvalidOperationException("Metadata JSON was empty.");
    }

    private static async Task<FakeUDKMetadata> WaitForMetadataAsync(
        string path,
        Func<FakeUDKMetadata, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                try
                {
                    var metadata = JsonSerializer.Deserialize<FakeUDKMetadata>(
                        await File.ReadAllTextAsync(
                            path,
                            TestContext.Current.CancellationToken));
                    if (metadata is not null && predicate(metadata))
                    {
                        return metadata;
                    }
                }
                catch (IOException)
                {
                }
                catch (JsonException)
                {
                }
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Metadata condition was not reached for '{path}'.");
    }

    private static void AssertProcessGone(int processId)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
        }

        Assert.True(
            process is null || process.HasExited,
            $"Process {processId} is still running.");
        process?.Dispose();
    }

    private static void AssertProcessAlive(int processId)
    {
        using var process = Process.GetProcessById(processId);
        Assert.False(process.HasExited, $"Process {processId} exited unexpectedly.");
    }

    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CtrlBreakEvent = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
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
    private struct ProcessInformation
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
    private static unsafe partial bool CreateProcess(
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GenerateConsoleCtrlEvent(
        uint ctrlEvent,
        uint processGroupId);

    private sealed record TestProcessResult(int ExitCode, string Output);

    private sealed class FakeUDKMetadata
    {
        public string[] Arguments { get; init; } = [];

        public int Pid { get; init; }

        public int? ChildPid { get; init; }

        public string? ChildReadyPath { get; init; }

        public bool PipeConnected { get; init; }

        public bool PipeClosed { get; init; }

        public bool EventsOpened { get; init; }

        public bool ShutdownRequested { get; init; }

        public bool UsedConsoleFallback { get; init; }

        public int SelectedDelayMilliseconds { get; init; }

        public int CompletedStages { get; init; }

        public string? State { get; init; }
    }
}