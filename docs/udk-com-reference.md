# UDK.com reference

## Scope

This document records behavior observed in the 2015 `UDK.com` launcher and its
UE3 target. It is build-specific reference material. It describes the launcher
and target as they exist.

The analyzed `UDK.com` is a 32-bit .NET Framework 4 Client Profile assembly
named `DualMode2`, version `1.0.4791.34615`. The target is the neighboring
64-bit `UDK.exe`. The peer target behavior is based on static analysis and real
`make`/`make -log` runs. Proprietary binaries, symbols, and log files are not
part of this repository.

## Executable selection

`UDK.com` derives the target path from its own executable path.

- If `UDK.com` is in a directory whose name ends in `Win32` or `Win64`, it
  replaces its extension with `.exe` and launches the same-stem executable in
  that directory.
- On 64-bit Windows, a launcher outside a `Win32`/`Win64` directory first tries
  a `Win64` subdirectory for non-UDK executable names, then a `Win32`
  subdirectory when the preferred file does not exist.
- A missing executable produces the text `Unable to to find binary to run!`
  and an unsuccessful launcher exit.

For the analyzed `UDK-2015-01\Binaries\Win64\UDK.com`, the selected target is
the neighboring `UDK.exe`.

## Target arguments and working directory

`UDK.com` receives target arguments from its own command-line `args` array. It
concatenates them with spaces and surrounds an argument with double quotes when
that argument contains a space.

This argument construction does not escape embedded quote characters and does
not implement complete Windows command-line quoting.

`UDK.com` does not set the target working directory explicitly. The target
therefore inherits the launcher's current working directory.

## Startup ordering

`UDK.com` performs these operations in this order:

1. resolve the neighboring target executable;
2. construct the target argument string;
3. detect `server` with a case-insensitive substring search over that string;
4. subscribe `Console.CancelKeyPress`;
5. call `Process.Start` for the target;
6. construct `dualmode_die_event<PID>`;
7. create the named event;
8. create the PID-specific named pipe server;
9. enter its output/process polling loop.

The target is not created suspended. There is therefore a startup race between
the target opening `\\.\pipe\<PID>cout` and `UDK.com` creating that server.

## Named output pipe

`UDK.com` creates an inbound byte-mode pipe named:

```text
\\.\pipe\<PID>cout
```

`PID` is the target process identifier in decimal.

Observed pipe settings:

| Property            | Value             |
|---------------------|-------------------|
| Direction           | inbound           |
| Mode                | byte              |
| Read mode           | byte              |
| Maximum instances   | 1                 |
| Input buffer        | 1024 bytes        |
| Output buffer       | 1024 bytes        |
| Default timeout     | 1000 milliseconds |
| Security descriptor | null              |

The native connection call is synchronous. `UDK.com` waits for the target to
connect before entering the normal read loop.

The target opens the pipe for write access and replaces its standard output
handle with the resulting pipe handle. The target writes complete or partial
UTF-16LE chunks; pipe reads may divide records and code units at arbitrary byte
boundaries.

## Output reads

Each poll requests 1024 bytes into a 1025-byte buffer. The returned byte count
is read from a four-byte native output buffer. `UDK.com` interprets every
two-byte pair as one little-endian `char`, concatenates the characters, and
returns one managed string for that read.

The poll routine:

- writes ordinary text with `Console.Write`;
- recognizes a color control only when the complete read string starts with the
  color marker;
- handles one control sequence at the beginning of that read;
- does not buffer marker prefixes, incomplete UTF-16 code units, or ordinary
  text before a marker;
- catches and suppresses read/parse exceptions inside the polling routine.

`UDK.com` is therefore not robust to a marker split across two reads, a marker
preceded by ordinary text in one read, or an odd/truncated UTF-16 code unit.

## Color-control records

The target emits color controls as separate UTF-16LE records in this form:

```text
`~[~`<payload>\r\n
```

The marker is:

```text
`~[~`
```

A reset record is:

```text
`~[~`Reset\r\n
```

`SetConsoleText` treats a command beginning with `Reset` case-sensitively
through `StartsWith("Reset")` and calls `Console.ResetColor`.

For non-reset records, `UDK.com` parses the first four payload characters with
base 2 and uses the resulting value as an index into this foreground
`ConsoleColor` palette:

| Four-bit binary input | Index | `ConsoleColor` |
|-----------------------|-------|----------------|
| `0000`                | 0     | Black          |
| `0001`                | 1     | Black          |
| `0010`                | 2     | DarkBlue       |
| `0011`                | 3     | Blue           |
| `0100`                | 4     | DarkGreen      |
| `0101`                | 5     | Green          |
| `0110`                | 6     | DarkCyan       |
| `0111`                | 7     | Cyan           |
| `1000`                | 8     | DarkRed        |
| `1001`                | 9     | Red            |
| `1010`                | 10    | DarkMagenta    |
| `1011`                | 11    | Magenta        |
| `1100`                | 12    | DarkYellow     |
| `1101`                | 13    | Yellow         |
| `1110`                | 14    | Gray           |
| `1111`                | 15    | White          |

Characters after the first four payload characters are ignored by this
launcher. In particular, the normal control-record CRLF is consumed implicitly
because the entire read is handled as a control and is not sent to
`Console.Write`.

`UDK.com` does not parse or apply a background color. The `0001` entry is
intentionally recorded as the implemented palette value, `Black`, even though a
generic RGBI interpretation might call that position intensity-only.

## `dualmode_die_event<PID>`

After the target starts, `UDK.com` creates a manual-reset event named:

```text
dualmode_die_event<PID>
```

The event is initially unsignaled. The target opens it during normal output
device shutdown, signals it, closes it, and closes the output pipe.

The launcher polls the event with a one-millisecond wait between output reads:

```text
while target alive and close event not signaled:
    read/poll output
```

Because a poll performs a synchronous pipe read, event and process state are not
rechecked during a blocked read.

## `ComWrapperShutdown`

During Ctrl+C handling, the launcher detects server mode from whether the
constructed target argument string contains the lowercase substring `server`.
For a server target it creates or opens the named manual-reset event:

```text
ComWrapperShutdown
```

The launcher signals that event and otherwise allows its main loop to continue.
Compatible UE3 server commandlets observe the same event and begin their normal
exit path.

The bare name is not process-specific. Multiple simultaneous launchers can
therefore interact with the same named event.

## `-log` output routing

The `-log` switch changes target behavior rather than pipe syntax.

Observed target behavior:

- with `-log`, initial `Init:` and `Log:` startup records appear through the
  inherited dual-mode console path;
- the target then disconnects from that path and writes later compiler and
  commandlet records to `Launch.log`;
- without `-log`, commandlet/compiler records continue through the pipe.

In observed VNEditor runs without `-log`, the terminal begins with:

```text
Log: Executing Class UnrealEd.MakeCommandlet
```

followed by package headings, diagnostics, summary, and elapsed time.

In observed UDK 2015 runs without `-log`, an engine-wide `Init:` sequence is
also present before the commandlet output. Startup verbosity is therefore
target/build-dependent.

Lowercase `info:`, `success:`, and `error:` lines seen in older build-script
output are not UDK.com or target records.

## Exit behavior

`UDK.com` has two completion outcomes:

- if the target process has exited, the launcher returns
  `ProcessToLaunch.ExitCode`;
- if the close event ends the loop while the target is still considered alive,
  the launcher returns `0`.

Executable-selection or startup failure returns a negative .NET process code
represented by `-1` in launcher code.

The launcher waits indefinitely for a blocked pipe read when the target exits
without producing pipe EOF in time. It has no Job Object and no process-tree
tracking.

## Ctrl+C behavior

The console cancellation handler does not set
`ConsoleCancelEventArgs.Cancel`.

- Server mode: signal `ComWrapperShutdown`.
- Non-server mode: call `Process.Kill()` on the main target.

`UDK.com` has no two-stage graceful/forced state machine. It resets console
color when the pipe is destroyed.

## `UDK.exe` peer behavior

The analyzed `UDK.exe` output-device implementation:

- formats `\\.\pipe\<PID>cout` with the current process identifier;
- opens that pipe for generic write using `OPEN_EXISTING`;
- installs the pipe handle as `STD_OUTPUT_HANDLE`;
- writes UTF-16LE output records with `WriteFile`;
- flushes each output record with `FlushFileBuffers`;
- formats `dualmode_die_event<PID>` during output-device disconnect;
- opens that event, signals it when available, closes the event handle, and
  closes the pipe.

Ordinary records have this shape:

```text
[optional category + ": "] + message + "\r\n"
```

No byte-order mark or terminating NUL is part of an ordinary record.

## Known limitations of this launcher

1. The target is started before the PID-specific pipe and close event exist.
2. Target launch and pipe connection can race.
3. Argument quoting only handles arguments containing spaces.
4. Reads are synchronous, fixed-size, and assume complete UTF-16 code units.
5. Control records must begin a single `ReadFile` result.
6. Polling and cancellation depend on `ReadFile` returning.
7. Target/process-tree cleanup is limited to the main child.
8. `ComWrapperShutdown` is shared by simultaneous launchers.
9. A close-event termination can return `0` while the target is still alive.

## Static-analysis anchors

For the managed `UDK.com` build, the relevant methods are:

| Managed method | Observed role |
|----------------|---------------|
| `DualMode2.Program.GetExecutable` | Selects the neighboring/Win32/Win64 target |
| `DualMode2.Program.Main` | Starts target, creates protocol objects, polls, returns exit code |
| `DualMode2.Program.ControlCHandler` | Signals server event or kills main target |
| `DualMode2.NamedPipe.Connect` | Creates and connects the PID pipe |
| `DualMode2.NamedPipe.Read` | Reads 1024 UTF-16LE bytes |
| `DualMode2.NamedPipe.SetConsoleText` | Parses reset or four-bit palette control |
| `DualMode2.NamedPipe.PollOutput` | Writes ordinary text or handles a control |

For the analyzed `UDK.exe` build, the PDB-backed output-device symbols are:

| Address     | Symbol                                                      | Observed role                          |
|-------------|-------------------------------------------------------------|----------------------------------------|
| `1400ea980` | `FOutputDeviceConsoleWindowsInherited::Connect`             | Opens PID pipe and redirects stdout    |
| `1400eaa40` | `FOutputDeviceConsoleWindowsInherited::DisconnectInherited` | Signals close event and closes pipe    |
| `1400efb00` | `FOutputDeviceConsoleWindowsInherited::Serialize`           | Writes UTF-16LE text and color records |
