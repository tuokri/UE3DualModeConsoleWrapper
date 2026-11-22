using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

var logOption = new Option<bool>("-log");
var noConnectOption = new Option<bool>("--fake-no-connect");
var blockOption = new Option<bool>("--fake-block");
var failOption = new Option<bool>("--fake-fail");
var descendantOption = new Option<bool>("--fake-descendant");
var childOption = new Option<bool>("--fake-child");
var closeBeforeExitOption = new Option<bool>("--fake-close-before-exit");
var profileOption = new Option<string>("--fake-profile")
{
    DefaultValueFactory = _ => "compact"
};
profileOption.AcceptOnlyFromAmong("compact", "startup-prefix");
var delayOption = new Option<int?>("--fake-delay-ms");
var connectDelayOption = new Option<int?>("--fake-connect-delay-ms");
var closeDelayOption = new Option<int?>("--fake-close-delay-ms");
var exitCodeOption = new Option<int>("--fake-exit-code")
{
    DefaultValueFactory = _ => 0
};
var metadataOption = new Option<string?>("--fake-metadata");

var command = new RootCommand("Synthetic UE3 dual-mode protocol target.")
{
    TreatUnmatchedTokensAsErrors = false
};
command.Options.Add(logOption);
command.Options.Add(noConnectOption);
command.Options.Add(blockOption);
command.Options.Add(failOption);
command.Options.Add(descendantOption);
command.Options.Add(childOption);
command.Options.Add(closeBeforeExitOption);
command.Options.Add(profileOption);
command.Options.Add(delayOption);
command.Options.Add(connectDelayOption);
command.Options.Add(closeDelayOption);
command.Options.Add(exitCodeOption);
command.Options.Add(metadataOption);
command.SetAction(async (parseResult, cancellationToken) =>
{
    var selectedDelay = parseResult.GetValue(delayOption);
    var options = new FakeUDKOptions
    {
        Profile = parseResult.GetRequiredValue(profileOption) switch
        {
            "compact" => FakeUDKProfile.Compact,
            "startup-prefix" => FakeUDKProfile.StartupPrefix,
            var value => throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Unknown FakeUDK profile '{value}'.")
        },
        UseLogRouting = parseResult.GetValue(logOption),
        SkipPipeConnection = parseResult.GetValue(noConnectOption),
        Block = parseResult.GetValue(blockOption),
        FailCompilation = parseResult.GetValue(failOption),
        StartDescendant = parseResult.GetValue(descendantOption),
        ChildMode = parseResult.GetValue(childOption),
        ClosePipeBeforeExit = parseResult.GetValue(closeBeforeExitOption),
        ExitCode = parseResult.GetValue(exitCodeOption),
        SelectedDelayMilliseconds = selectedDelay ??
                                    Random.Shared.Next(
                                        FakeUDK.MinimumCompileDelayMilliseconds,
                                        FakeUDK.MaximumCompileDelayMilliseconds + 1),
        ConnectionDelayMilliseconds = parseResult.GetValue(connectDelayOption) ?? 0,
        DelayAfterCloseMilliseconds = parseResult.GetValue(closeDelayOption) ?? 0,
        MetadataPath = parseResult.GetValue(metadataOption)
    };

    options.Validate();
    if (options.DelayAfterCloseMilliseconds > 0)
    {
        options.ClosePipeBeforeExit = true;
    }

    if (options.FailCompilation && options.ExitCode == 0)
    {
        options.ExitCode = 1;
    }

    return await FakeUDK
        .RunAsync(args, options, cancellationToken)
        .ConfigureAwait(false);
});

return await command.Parse(args).InvokeAsync().ConfigureAwait(false);

internal static class FakeUDK
{
    private const string ColorMarker = "`~[~`";
    private const string ResetCommand = "Reset";
    public const int MinimumCompileDelayMilliseconds = 200;
    public const int MaximumCompileDelayMilliseconds = 1000;
    private static readonly Encoding OutputEncoding = new UnicodeEncoding(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(
        string[] arguments,
        FakeUDKOptions options,
        CancellationToken cancellationToken)
    {
        var metadata = new FakeUDKMetadata
        {
            Arguments = arguments,
            Pid = Environment.ProcessId,
            Mode = options.Block ? "block" : options.Profile.ToString(),
            LogRouting = options.UseLogRouting,
            SelectedDelayMilliseconds = options.SelectedDelayMilliseconds,
            ExitCode = options.ExitCode
        };

        WriteMetadata(options.MetadataPath, metadata);

        if (options.ChildMode)
        {
            metadata.IsDescendant = true;
            metadata.State = "blocking-descendant";
            WriteMetadata(options.MetadataPath, metadata);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        if (options.StartDescendant)
        {
            metadata.ChildPid = StartDescendant();
            WriteMetadata(options.MetadataPath, metadata);
        }

        if (options.SkipPipeConnection)
        {
            metadata.State = "exited-before-pipe";
            WriteMetadata(options.MetadataPath, metadata);
            return options.ExitCode;
        }

        EventWaitHandle? closeEvent = null;
        EventWaitHandle? shutdownEvent = null;
        NamedPipeClientStream? pipe = null;
        try
        {
            if (options.ConnectionDelayMilliseconds > 0)
            {
                metadata.State = "delaying-connection";
                WriteMetadata(options.MetadataPath, metadata);
                await Task
                    .Delay(options.ConnectionDelayMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
            }

            pipe = new NamedPipeClientStream(
                ".",
                Environment.ProcessId.ToString() + "cout",
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            metadata.State = "connecting-pipe";
            WriteMetadata(options.MetadataPath, metadata);
            pipe.Connect();
            metadata.PipeConnected = true;

            closeEvent = EventWaitHandle.OpenExisting(
                "dualmode_die_event" + Environment.ProcessId);
            shutdownEvent = EventWaitHandle.OpenExisting("ComWrapperShutdown");
            metadata.EventsOpened = true;
            WriteMetadata(options.MetadataPath, metadata);

            if (options.Block)
            {
                metadata.State = "blocking-after-connect";
                WriteMetadata(options.MetadataPath, metadata);
                await WaitForShutdownAsync(shutdownEvent).ConfigureAwait(false);
                metadata.ShutdownRequested = true;
                metadata.State = "shutdown-requested";
                WriteMetadata(options.MetadataPath, metadata);
                await WriteRecordAsync(
                    pipe,
                    "Log: Cooperative shutdown requested\r\n")
                    .ConfigureAwait(false);
            }
            else
            {
                var shutdownTask = WaitForShutdownAsync(shutdownEvent);
                var compilationOutput = EmitCompilationAsync(
                    pipe,
                    options,
                    metadata,
                    shutdownTask,
                    cancellationToken);
                await compilationOutput.ConfigureAwait(false);
                metadata.ShutdownRequested =
                    shutdownTask.IsCompletedSuccessfully;
            }

            if (options.ClosePipeBeforeExit)
            {
                metadata.State = "closing-pipe";
                metadata.PipeClosed = true;
                WriteMetadata(options.MetadataPath, metadata);
                closeEvent.Set();
                pipe.Dispose();
                pipe = null;

                if (options.DelayAfterCloseMilliseconds > 0)
                {
                    await Task
                        .Delay(options.DelayAfterCloseMilliseconds)
                        .WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            metadata.State = "exiting";
            metadata.ExitCode = options.ExitCode;
            WriteMetadata(options.MetadataPath, metadata);
            return options.ExitCode;
        }
        finally
        {
            if (closeEvent is not null && pipe is not null)
            {
                closeEvent.Set();
            }

            pipe?.Dispose();
            closeEvent?.Dispose();
            shutdownEvent?.Dispose();
            metadata.PipeClosed = true;
            metadata.State = "completed";
            WriteMetadata(options.MetadataPath, metadata);
        }
    }

    private static async Task EmitCompilationAsync(
        NamedPipeClientStream pipe,
        FakeUDKOptions options,
        FakeUDKMetadata metadata,
        Task shutdownTask,
        CancellationToken cancellationToken)
    {
        var stages = BuildStages(options);
        metadata.State = "compiling";
        WriteMetadata(options.MetadataPath, metadata);

        if (options.UseLogRouting)
        {
            await WriteRecordAsync(pipe, "Init: Version: 12791\r\n").ConfigureAwait(false);
            await WriteRecordAsync(
                    pipe,
                    "Init: Command line: make -log\r\n")
                .ConfigureAwait(false);
            await WriteSeverityRecordAsync(
                    pipe,
                    "1111",
                    "Log: Executing Class UnrealEd.MakeCommandlet\r\n")
                .ConfigureAwait(false);
            var completed = await Task
                    .WhenAny(
                        Task.Delay(options.SelectedDelayMilliseconds, cancellationToken),
                        shutdownTask)
                    .ConfigureAwait(false);
            if (completed == shutdownTask)
            {
                metadata.ShutdownRequested = true;
                metadata.State = "shutdown-requested";
            }

            metadata.CompletedStages = stages.Count;
            WriteMetadata(options.MetadataPath, metadata);
            return;
        }

        if (options.Profile == FakeUDKProfile.StartupPrefix)
        {
            await WriteRecordAsync(pipe, "Init: Version: 12791\r\n").ConfigureAwait(false);
            await WriteRecordAsync(
                    pipe,
                    "Init: Base directory: C:\\FakeUDK\\Binaries\\Win64\\\r\n")
                .ConfigureAwait(false);
        }

        await WriteSeverityRecordAsync(
                pipe,
                "1111",
                "Log: Executing Class UnrealEd.MakeCommandlet\r\n")
            .ConfigureAwait(false);

        for (var index = 0; index < stages.Count; index++)
        {
            var stage = stages[index];
            metadata.CurrentStage = stage.Name;
            metadata.CompletedStages = index;
            WriteMetadata(options.MetadataPath, metadata);
            await WriteRecordAsync(pipe, stage.Output).ConfigureAwait(false);
            var completed = await Task
                .WhenAny(
                    Task.Delay(stage.DelayMilliseconds, cancellationToken),
                    shutdownTask)
                .ConfigureAwait(false);
            if (completed == shutdownTask)
            {
                metadata.ShutdownRequested = true;
                metadata.State = "shutdown-requested";
                WriteMetadata(options.MetadataPath, metadata);
                await WriteRecordAsync(
                        pipe,
                        "Log: Cooperative shutdown requested\r\n")
                    .ConfigureAwait(false);
                return;
            }
        }

        metadata.CompletedStages = stages.Count;
        if (options.FailCompilation)
        {
            await WriteSeverityRecordAsync(
                    pipe,
                    "1101",
                    "Synthetic.uc(17) : Warning, unused local variable\r\n")
                .ConfigureAwait(false);
            await WriteSeverityRecordAsync(
                    pipe,
                    "1001",
                    "Synthetic.uc(23) : Error, Bad command or expression\r\n")
                .ConfigureAwait(false);
            await WriteRecordAsync(pipe, "Compile aborted due to errors.\r\n").ConfigureAwait(false);
            await WriteRecordAsync(pipe, "\r\nWarning/Error Summary\r\n---------------------\r\n").ConfigureAwait(false);
            await WriteSeverityRecordAsync(
                    pipe,
                    "1001",
                    "Synthetic.uc(23) : Error, Bad command or expression\r\n")
                .ConfigureAwait(false);
            await WriteRecordAsync(pipe, "\r\nFailure - 1 error(s), 1 warning(s)\r\n").ConfigureAwait(false);
        }
        else
        {
            await WriteRecordAsync(
                    pipe,
                    "Log: Scripts successfully compiled - saving package FakeUDK.u\r\n")
                .ConfigureAwait(false);
            await WriteRecordAsync(pipe, "\r\nSuccess - 0 error(s), 0 warning(s)\r\n").ConfigureAwait(false);
        }

        await WriteRecordAsync(
                pipe,
                $"Execution of commandlet took:  " +
                $"{(options.SelectedDelayMilliseconds / 1000.0).ToString("0.00", CultureInfo.InvariantCulture)} seconds\r\n")
            .ConfigureAwait(false);
        metadata.State = "compilation-finished";
        WriteMetadata(options.MetadataPath, metadata);
    }

    private static List<FakeCompilationStage> BuildStages(FakeUDKOptions options)
    {
        var stages = new List<FakeCompilationStage>
        {
            new("heading-core", "--------------------Core - Release--------------------\r\n"),
            new("analyzing", "Analyzing...\r\n"),
            new("heading-game", "--------------------FakeGame - Release--------------------\r\n"),
            new("compiling", "Log: Compiling synthetic scripts\r\n")
        };

        var remaining = options.SelectedDelayMilliseconds;
        for (var index = 0; index < stages.Count; index++)
        {
            var stagesLeft = stages.Count - index;
            var delay = stagesLeft == 1
                ? remaining
                : remaining / stagesLeft;
            stages[index].DelayMilliseconds = delay;
            remaining -= delay;
        }

        return stages;
    }

    private static async Task WriteSeverityRecordAsync(
        NamedPipeClientStream pipe,
        string binaryColor,
        string text)
    {
        await WriteControlRecordAsync(pipe, binaryColor).ConfigureAwait(false);
        await WriteRecordAsync(pipe, text).ConfigureAwait(false);
        await WriteControlRecordAsync(pipe, ResetCommand).ConfigureAwait(false);
    }

    private static async Task WriteControlRecordAsync(
        NamedPipeClientStream pipe,
        string command)
    {
        var bytes = OutputEncoding.GetBytes(ColorMarker + command + "\r\n");
        var splitPoints = new[]
        {
            Math.Min(1, bytes.Length),
            Math.Min(3, bytes.Length),
            bytes.Length
        };
        var offset = 0;
        foreach (var splitPoint in splitPoints)
        {
            if (splitPoint > offset)
            {
                await pipe
                    .WriteAsync(bytes.AsMemory(offset, splitPoint - offset))
                    .ConfigureAwait(false);
                await pipe.FlushAsync().ConfigureAwait(false);
                offset = splitPoint;
            }
        }
    }

    private static async Task WriteRecordAsync(
        NamedPipeClientStream pipe,
        string text)
    {
        var bytes = OutputEncoding.GetBytes(text);
        if (bytes.Length > 3)
        {
            await pipe.WriteAsync(bytes.AsMemory(0, 1)).ConfigureAwait(false);
            await pipe.WriteAsync(bytes.AsMemory(1, 2)).ConfigureAwait(false);
            await pipe.WriteAsync(bytes.AsMemory(3)).ConfigureAwait(false);
        }
        else
        {
            await pipe.WriteAsync(bytes.AsMemory()).ConfigureAwait(false);
        }

        await pipe.FlushAsync().ConfigureAwait(false);
    }

    private static async Task WaitForShutdownAsync(EventWaitHandle shutdownEvent)
    {
        await Task.Run(shutdownEvent.WaitOne).ConfigureAwait(false);
    }

    private static int StartDescendant()
    {
        var startInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--fake-child");

        var child = Process.Start(startInfo) ??
                   throw new InvalidOperationException("Failed to start FakeUDK descendant.");
        return child.Id;
    }

    private static void WriteMetadata(string? path, FakeUDKMetadata metadata)
    {
        if (path is null)
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(metadata, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private sealed class FakeCompilationStage
    {
        public FakeCompilationStage(string name, string output)
        {
            Name = name;
            Output = output;
        }

        public string Name { get; }

        public string Output { get; }

        public int DelayMilliseconds { get; set; }
    }

    private sealed class FakeUDKMetadata
    {
        public string[] Arguments { get; set; } = [];

        public int Pid { get; set; }

        public int? ChildPid { get; set; }

        public bool IsDescendant { get; set; }

        public string Mode { get; set; } = "compact";

        public bool LogRouting { get; set; }

        public bool PipeConnected { get; set; }

        public bool PipeClosed { get; set; }

        public bool EventsOpened { get; set; }

        public bool ShutdownRequested { get; set; }

        public int SelectedDelayMilliseconds { get; set; }

        public int CompletedStages { get; set; }

        public string? CurrentStage { get; set; }

        public string? State { get; set; }

        public int ExitCode { get; set; }
    }
}

internal sealed class FakeUDKOptions
{
    public FakeUDKProfile Profile { get; set; } = FakeUDKProfile.Compact;

    public bool UseLogRouting { get; set; }

    public bool SkipPipeConnection { get; set; }

    public bool ClosePipeBeforeExit { get; set; } = true;

    public bool Block { get; set; }

    public bool FailCompilation { get; set; }

    public bool StartDescendant { get; set; }

    public bool ChildMode { get; set; }

    public int ExitCode { get; set; }

    public int SelectedDelayMilliseconds { get; set; }

    public int ConnectionDelayMilliseconds { get; set; }

    public int DelayAfterCloseMilliseconds { get; set; }

    public string? MetadataPath { get; set; }

    public void Validate()
    {
        if (SelectedDelayMilliseconds is < FakeUDK.MinimumCompileDelayMilliseconds
            or > FakeUDK.MaximumCompileDelayMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SelectedDelayMilliseconds),
                $"FakeUDK delay must be between " +
                $"{FakeUDK.MinimumCompileDelayMilliseconds} and " +
                $"{FakeUDK.MaximumCompileDelayMilliseconds} milliseconds.");
        }

        if (ConnectionDelayMilliseconds < 0 || DelayAfterCloseMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConnectionDelayMilliseconds),
                "FakeUDK delay options must not be negative.");
        }

        if (ExitCode < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ExitCode),
                "FakeUDK exit code must not be negative.");
        }
    }
}

internal enum FakeUDKProfile
{
    Compact,
    StartupPrefix
}
