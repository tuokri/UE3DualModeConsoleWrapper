# UDK.com protocol baseline

## Purpose

This document defines the behavior that the wrapper must preserve to remain
compatible with Unreal Engine 3 (UE3) executables that use the dual-mode console
protocol.

`UDK.com` is a 32-bit Windows launcher for a same-stem `.exe`. The peer
implementation in this analysis is the 64-bit `UDK.exe`. Ghidra static analysis
was performed against the licensed local binaries. The `UDK.exe` analysis was
Program Database (PDB) assisted; the `UDK.com` analysis was not. Proprietary
binaries and debug symbols are not part of this repository.

This is a build-specific baseline. Revalidate it after a UDK target changes.

This document contains AI-assisted program analysis.

## Normative terms

- **Must** describes behavior required for compatibility.
- **Should** describes robust behavior that avoids UDK.com limitations.
- **May** describes an allowed implementation choice.

## Protocol requirements

### Process selection and startup

1. The target must be launched as the executable selected by the wrapper.
2. The wrapper must obtain the target process ID before creating the
   PID-specific protocol objects.
3. The wrapper must create the target suspended with the Windows
   `CREATE_SUSPENDED` flag.
4. The pipe server and required named events must exist before the target's
   primary thread is resumed.
5. The wrapper must preserve target argument boundaries. `UDK.com` copies the
   original command-line tail after the launcher token. The modern wrapper may
   use a stronger quoting strategy, but the target must observe the same logical
   argument array.
6. The wrapper must resume the primary thread exactly once after successful
   protocol setup.

`UDK.com` derives a `.exe` path from its own drive, directory, and base name,
then launches it with `CreateProcessW` and no inherited handle table. For this
protocol, selecting `UDK.exe` explicitly is equivalent. The wrapper's
`--executable` option is therefore an implementation choice, not a protocol
change.

`UDK.com` also checks for a `Win32` subdirectory fallback. That is launcher
selection policy and is not required for the general UE3 wrapper.

### Working directory

`UDK.com` passes a null current-directory pointer to `CreateProcessW`, so the
target inherits the launcher's working directory.

This behavior is not encoded in the pipe protocol. A compatible wrapper may
select another default when it documents the choice. The current wrapper
defaults to the executable directory and supports an explicit
`--working-directory` override.

### Named output pipe

The target connects to this inbound pipe:

```text
\\.\pipe\<PID>cout
```

`PID` is the target process identifier written in decimal without leading
formatting. Both the launcher and the target derive the name from the child
process ID.

The wrapper must provide the pipe as a server endpoint before the target
attempts `CreateFileW`. The target opens the pipe with:

- generic write access;
- no sharing;
- `OPEN_EXISTING` creation disposition;
- a normal, blocking file handle.

The target then assigns the pipe handle to `STD_OUTPUT_HANDLE` with
`SetStdHandle`.

Observed `UDK.com` server settings:

| Property            | UDK.com value     |
|---------------------|-------------------|
| Direction           | inbound           |
| Mode                | byte stream       |
| Maximum instances   | 1                 |
| Input buffer        | 1024 bytes        |
| Output buffer       | 1024 bytes        |
| Default timeout     | 1000 milliseconds |
| Security attributes | default           |

The modern wrapper must preserve the endpoint name, byte-stream semantics, and
one-server assumption. Buffer sizing does not change interoperability, but the
1024-byte values should remain the regression baseline.

UDK.com's null security descriptor grants normal named-object access. The
wrapper may use a more restrictive current-user-only access policy as a security
hardening measure, provided the target runs in the same security context.

### `dualmode_die_event` close notification

The wrapper must create this manual-reset event:

```text
dualmode_die_event<PID>
```

The event is initially unsignaled. During normal output-device shutdown, the
target:

1. opens the event with event-modify access;
2. signals it if the open succeeds;
3. closes the event handle;
4. closes its output pipe handle.

The target ignores failure to open the event and still closes the pipe.
Therefore the wrapper must treat pipe end-of-file and process exit as
authoritative completion signals. The event is a compatibility and completion
hint, not the sole indicator of shutdown.

`UDK.com` waits for either the target process or this event, then joins the
reader thread. The reader thread continues until `ReadFile` reports end-of-file
or an error. The modern wrapper should wait for both output completion and
process completion so a closed output stream cannot hide a still-running target.

### `ComWrapperShutdown` server event

`UDK.com` creates an initially unsignaled, manual-reset named event:

```text
ComWrapperShutdown
```

The UE3 server commandlet opens a synchronization object with that name and
checks it during its command loop. When it is signaled, the server begins its
normal game-ending/exit path.

This event is relevant to server-mode graceful shutdown. A wrapper that claims
graceful compatibility with UE3 server targets must create the event and signal
it as part of the first graceful shutdown request.

The event name is process-local to the current session unless a `Global\` or
`Local\` prefix is added; the baseline uses the bare name. Because the bare name
is not PID-specific, multiple simultaneous wrappers can observe the same object.
Implementations should document that limitation and avoid changing the peer
name.

### UTF-16 output stream

The target writes UTF-16 little-endian (UTF-16LE) code units. It does not write
a byte-order mark. No terminating NUL is written for an ordinary output record.

Each normal record is assembled as:

```text
[optional category + ": "] + message + "\r\n"
```

The entire record is written with `WriteFile`. The target calls
`FlushFileBuffers` after each write.

The wrapper must:

1. decode UTF-16LE, not the host's ANSI code page;
2. preserve ordinary text and line endings;
3. support a UTF-16 code unit split across pipe reads;
4. continue reading until pipe end-of-file;
5. flush forwarded output promptly enough that a full pipe cannot stall the
   target.

Strict replacement behavior is an interoperability decision. UDK.com divides
the byte count by two and does not recover odd trailing bytes. The modern
wrapper should detect truncated code units instead of silently producing
corrupt text.

### Console color records

The target emits color controls as separate UTF-16LE output records:

```text
`~[~` + payload + "\r\n"
```

The five-code-unit marker is backtick, tilde, left bracket, tilde, backtick:

```text
`~[~`
```

An empty color string is changed by the target to `Reset`, so the normal reset
record is:

```text
`~[~`Reset\r\n
```

`UDK.com` compares the marker and `Reset` case-insensitively for a record that
begins at a read-buffer boundary.

In a `make -log` terminal run, the visible startup records use categories such
as `Init:` and `Log:`. The later clean-run package headings and completion text
such as `Log: Success - 0 error(s), 0 warning(s)`, and failed-run `Error:`
records ending with `Log: Compile aborted due to errors.`,
`Log: Warning/Error Summary`, and
`Log: Failure - N error(s), M warning(s)`, are in `Launch.log`, not in the
wrapper's terminal output. These text records do not contain the marker because
`UDK.com` consumes color-control records before printing ordinary output.

The concise non-`-log` commandlet path differs: observed VNEditor output begins
with `Log: Executing Class UnrealEd.MakeCommandlet`, followed by package headings,
compiler diagnostics, summaries, and elapsed time. It does not show the
engine-wide `Init:` startup sequence. Lowercase `info:`, `success:`, and `error:`
messages seen in the older build workflow are emitted by `build.ps1`, not by
`UDK.exe`, `VNEditor.exe`, or the wrapper.

The `-log` argument also changes target output routing. In observed real UDK and
VNEditor runs, the inherited dual-mode pipe carried startup records, then the
target disconnected from that inherited console path. Later compilation and
commandlet records were written to `Launch.log` instead of the pipe. The wrapper
continued waiting for the target and propagated its exit code. Running the same
target without `-log` kept commandlet/compiler records on the inherited
dual-mode pipe, which produced the concise build-oriented output seen in the
older wrapper.

This difference is UE3 target behavior, not a wrapper quoting or pipe-decoding
difference. The original `UDK.com` wrapper exhibits the same visible distinction.
`-FORCELOGFLUSH` affects log flushing; it does not restore compiler output to the
dual-mode pipe.

The generic wrapper must not automatically discover or merge a target's log
file. A future "pipe plus log tee" mode would need explicit configuration for
the log path, rotation/backup behavior, de-duplication, and target startup
ordering.

For a non-reset record, UDK.com reads the first eight payload code units as
binary component digits. Each digit must be `0` or `1`. The digit positions map
to Windows console color attributes as follows:

| Position | Component            | Console attribute |
|----------|----------------------|-------------------|
| 0        | foreground red       | `0x0004`          |
| 1        | foreground green     | `0x0002`          |
| 2        | foreground blue      | `0x0001`          |
| 3        | foreground intensity | `0x0008`          |
| 4        | background red       | `0x0040`          |
| 5        | background green     | `0x0020`          |
| 6        | background blue      | `0x0010`          |
| 7        | background intensity | `0x0080`          |

Payloads shorter than eight digits represent missing positions as zero. For
example:

| Payload    | Windows attributes | Meaning                                  |
|------------|--------------------|------------------------------------------|
| `0000`     | `0x0000`           | foreground black                         |
| `0001`     | `0x0008`           | bright/intense black                     |
| `0010`     | `0x0001`           | foreground blue                          |
| `0100`     | `0x0002`           | foreground green                         |
| `1000`     | `0x0004`           | foreground dark red                      |
| `1111`     | `0x000f`           | foreground white                         |
| `11111111` | `0x00ff`           | all foreground and background attributes |

This is a binary attribute record, not a four-digit hexadecimal palette index.
Implementations must not parse `1111` as hexadecimal value `0x1111`.

If a payload character is not `0` or `1`, UDK.com replaces it with NUL while
scanning and ignores the remainder of that record's digit sequence. The current
wrapper should reject malformed control records deterministically or reproduce
the baseline behavior deliberately. It must not emit a partial color record as
ordinary output.

The wrapper may expose `--no-color` and `NO_COLOR`. When disabled, it must consume
valid color records and emit only ordinary text.

### Output completion and exit code

The wrapper must keep the target's pipe open until the target closes it and
must not exit merely because the pipe reaches end-of-file.

The wrapper must return the target process's exit code after it has both:

1. drained the output pipe; and
2. observed target process exit.

`UDK.com` reads the exit code with `GetExitCodeProcess` and uses that value as
its own process exit code. It returns failure code `1` when it cannot create
the target process or protocol objects.

## Ctrl+C behavior in UDK.com

`UDK.com` installs a `SetConsoleCtrlHandler` callback. The callback's behavior
depends on whether the lowercase original argument tail contains `server`:

- For a server target, it signals `ComWrapperShutdown`, waits for the target
  process to exit, and then exits the wrapper.
- For other targets, it calls `TerminateProcess` with exit code zero.
- It resets the console attributes to `0x0007`.
- It calls `ExitProcess(0)`.

The first Ctrl+C therefore initiates immediate wrapper termination; UDK.com has
no two-stage force state. It does not propagate cancellation exit code `130`.

The modernization plan intentionally changes this user interaction to a
two-stage graceful/forced shutdown. That difference is allowed, but the first
graceful phase must preserve the peer's cooperative server mechanism and keep
draining output. The forced phase must not leave the target alive.

## UDK.com implementation defects that the wrapper must not copy

These behaviors are observable but are defects rather than compatibility
requirements:

1. A named-pipe, close-event, or shutdown-event creation failure after
   `CreateProcessW` can leave the target suspended forever.
2. `UDK.com` has no Job Object and may leave descendants running.
3. `UDK.com` recognizes a color marker only at the beginning of one `ReadFile`
   buffer. It is not robust to a marker split across reads or preceded by text
   in the same read.
4. `UDK.com` assumes complete UTF-16 code units in each read.
5. If the target exits without connecting to the pipe, `UDK.com` can wait
   indefinitely for its reader thread to finish connecting.
6. Redirected output is converted to the console output code page, which loses
   Unicode fidelity in some cases.
7. The single global `ComWrapperShutdown` name makes simultaneous launcher
   instances interfere with each other.
8. Non-server Ctrl+C forcibly terminates only the main target with exit code
   zero.

## Baseline conformance matrix

| Behavior                               | Baseline                  | Current wrapper                  | Required action                             |
|----------------------------------------|---------------------------|----------------------------------|---------------------------------------------|
| Suspended target startup               | Required                  | Matches                          | Preserve                                    |
| PID pipe name                          | `\\.\pipe\<PID>cout`      | Matches                          | Preserve                                    |
| Pre-resume pipe creation               | Required                  | Matches                          | Preserve                                    |
| Byte-mode, one-instance pipe           | Required                  | Matches                          | Preserve                                    |
| PID close event                        | Required                  | Matches creation                 | Preserve and verify target close ordering   |
| `ComWrapperShutdown` event             | Used for servers          | Missing                          | Add for server-compatible graceful shutdown |
| UTF-16LE stream                        | Required                  | Uses `Encoding.Unicode`          | Preserve; make decoder strict               |
| Split UTF-16 units/markers             | UDK.com is weak           | Marker decoder is boundary-aware | Preserve and test every split point         |
| Binary RGBI color payload              | Required                  | Treated as hexadecimal           | Fix and test                                |
| `Reset` color record                   | Required                  | Matches                          | Preserve                                    |
| Wait for output drain and process exit | Required                  | Matches                          | Preserve                                    |
| Target exit code                       | Propagated                | Matches                          | Preserve                                    |
| Zero exit without pipe                 | UDK.com can hang          | Wrapper reports protocol error   | Intentional robust difference               |
| Ctrl+C                                 | Immediate termination     | Planned two-stage shutdown       | Intentional documented difference           |
| Job Object/process tree cleanup        | Absent                    | Planned                          | Required hardening                          |
| Current-user pipe access               | Default access in UDK.com | More restrictive                 | Intentional hardening                       |
| Working directory                      | Inherited from launcher   | Defaults to executable directory | Documented wrapper behavior                 |

## Regression-test requirements

The synthetic child used for conformance tests must verify:

1. it receives the exact target argument array;
2. it opens `\\.\pipe\<own PID>cout` for generic write access;
3. it writes UTF-16LE without a byte-order mark;
4. ordinary `message\r\n` records reach the wrapper unchanged;
5. every possible byte boundary can split a UTF-16 code unit;
6. every character boundary can split the color marker, `Reset`, and a binary
   color payload;
7. `1111` produces foreground white and is not interpreted as hexadecimal;
8. `11111111` sets all eight console attribute bits;
9. `Reset` restores the original console attributes;
10. a `--no-color` run consumes controls without changing output color;
11. the target signals `dualmode_die_event<PID>` before closing its pipe;
12. pipe end-of-file does not complete the wrapper while the target remains
    alive;
13. the target exit code is returned exactly;
14. a zero exit without a pipe connection is reported as a protocol error;
15. a nonzero exit without a pipe connection preserves the target exit code;
16. first graceful cancellation signals `ComWrapperShutdown` for cooperative
    server shutdown and continues draining;
17. forced cancellation terminates the target and descendants;
18. malformed and incomplete color records have deterministic documented
    behavior.

## Static-analysis evidence

The following functions anchor the baseline for future revalidation:

### `UDK.com`

| Address    | Role                                                                                            |
|------------|-------------------------------------------------------------------------------------------------|
| `00401000` | Console control handler                                                                         |
| `00401060` | Creates the pipe, PID event, and `ComWrapperShutdown` event                                     |
| `00401140` | Closes handles and resets console attributes                                                    |
| `00401440` | Connects, reads, decodes, and forwards output                                                   |
| `00401780` | Starts the output-reader thread                                                                 |
| `004017a0` | Builds target command line, creates suspended process, resumes it, waits, and returns exit code |

### `UDK.exe`

PDB-assisted names and addresses used in the current build:

| Address     | Symbol                                                      | Role                                   |
|-------------|-------------------------------------------------------------|----------------------------------------|
| `1400ea980` | `FOutputDeviceConsoleWindowsInherited::Connect`             | Opens PID pipe and redirects stdout    |
| `1400eaa40` | `FOutputDeviceConsoleWindowsInherited::DisconnectInherited` | Signals PID event and closes pipe      |
| `1400efb00` | `FOutputDeviceConsoleWindowsInherited::Serialize`           | Writes UTF-16LE text and color records |
| `14039e6f0` | `UServerCommandlet::Main`                                   | Polls `ComWrapperShutdown`             |

Function names without PDB symbols and decompiler-generated type names are
evidence aids, not source-level API guarantees. Addresses are specific to this
analyzed image and must be refreshed for a changed build.

## Revision checklist

When either peer executable changes, rerun the following checks:

1. Confirm the pipe and event format strings.
2. Confirm the target's `CreateFileW`, `OpenEventW`, `WriteFile`, and close
   sequence.
3. Confirm UTF-16LE byte counts, line endings, flush behavior, and category
   prefixing.
4. Confirm the marker, reset payload, and binary color digit mapping.
5. Confirm startup ordering around `CreateProcessW` and `ResumeThread`.
6. Confirm wait ordering and exit-code propagation.
7. Confirm whether `ComWrapperShutdown` still exists and which target modes
   observe it.
8. Update the conformance tests before changing production behavior.
