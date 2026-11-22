# Plan: Modernize and extract the UE3 dual-mode console wrapper

## Objective

Turn the current AircraftMod-specific `VNEditorConsoleWrapper` into a
standalone, general-purpose Windows wrapper for executables that implement the
UE3 dual-mode named-pipe console protocol. Modernize the command-line interface,
native process lifecycle, tests, documentation, licensing, CI, versioning, and
release automation while preserving the verified protocol behavior.

This plan is for implementation by a human or coding agent. It does not authorize
code changes by itself.

## Confirmed decisions

- The project remains a **Windows wrapper for the UE3 dual-mode protocol**, not a
  replacement for general-purpose `subprocess`-style process launching.
- Use `System.CommandLine` 3.x for CLI parsing and `--` for forwarding target
  executable arguments.
- Target the newest stable .NET and C# release available at the implementation
  time. The baseline verified for this plan is **.NET 11.0 and C# 15**; do not
  depend on preview SDKs or a floating `preview` language version.
- Build and test the `Release` configuration in CI. Use Debug locally when a
  developer needs unoptimized stepping; do not maintain a second Debug CI matrix
  unless a real configuration-specific defect appears.
- Publish matching portable PDB files in a separate symbols archive. Do not
  embed symbols in the normal single-file executable by default.
- Use `AGPL-3.0-only` for all original project code.
- Generate a version from Git tags with MinVer.
- Use xUnit v3 with Microsoft Testing Platform for tests.
- Run all build, test, coverage, packaging, and CI helper checks on
  `windows-latest`. The supported runtime and protocol are Windows-specific;
  `ubuntu-latest` would duplicate tests without validating the native Windows
  behavior.
- Use synthetic child executables for repeatable end-to-end tests. Do not include
  proprietary Unreal Engine or game executables in CI.
- The README is a short installation and usage guide. Detailed engineering
  material belongs under `docs/`.

## Baseline findings to preserve

The review identified these existing strengths:

- `CREATE_SUSPENDED` prevents a startup race between the target PID and its
  PID-specific pipe.
- Source-generated `[LibraryImport]`, `SafeProcessHandle`, and `SafeWaitHandle`
  are appropriate for the required native Windows calls.
- The wrapper currently forwards UTF-16LE text, recognizes split color-control
  records, waits for both pipe EOF and process exit, and propagates the target's
  exit code. UDK.com's verified color payload is a four-digit binary value used
  as a 16-entry foreground `ConsoleColor` palette index, not a hexadecimal
  value and not an eight-component console-attribute string.
- `dotnet build -warnaserror` and `dotnet format --verify-no-changes` pass on the
  current branch.

The implementation must fix these identified weaknesses:

1. Cancellation or interruption can leave a resumed editor process running.
2. Console color is reset only on the normal output-forwarding path.
3. `AggregateException` from cleanup is not covered by the current top-level
   exception filter, which can expose an unhandled stack trace.
4. There is no automated coverage for command-line quoting, stream decoding, or
   native lifecycle races.
5. An unused record and redundant event-discard statement should be removed.

## Target repository

Use a name that describes the protocol rather than one product:

- Suggested repository: `UE3DualModeConsoleWrapper`
- Suggested executable: `UE3DualModeConsoleWrapper.exe`
- Suggested namespace: `UE3DualModeConsoleWrapper`
- Suggested project layout:

```text
/
├─ .config/
│  └─ dotnet-tools.json
├─ .github/
│  └─ workflows/
│     ├─ ci.yml
│     ├─ publish-coverage.yml
│     └─ release.yml
├─ docs/
│  ├─ architecture.md
│  ├─ development.md
│  ├─ protocol.md
│  ├─ testing.md
│  └─ udk-com-reference.md
├─ scripts/
│  ├─ ci/
│  │  ├─ Invoke-LoggedCommand.ps1
│  │  ├─ New-BuildMetadata.ps1
│  │  ├─ New-Checksums.ps1
│  │  └─ Stage-Artifacts.ps1
│  ├─ Generate-BuildMetadata.csproj
│  └─ Program.cs
├─ src/
│  └─ UE3DualModeConsoleWrapper/
├─ tests/
│  ├─ UE3DualModeConsoleWrapper.UnitTests/
│  ├─ UE3DualModeConsoleWrapper.IntegrationTests/
│  └─ UE3DualModeConsoleWrapper.FakeUDK/
├─ Directory.Build.props
├─ Directory.Packages.props
├─ global.json
├─ LICENSE
├─ README.md
└─ UE3DualModeConsoleWrapper.slnx
```

Keep the repository name, binary name, and namespace consistent. Do not copy
AircraftMod source, examples, workflow references, package identity, or
`build.ps1` references into the new repository.

## Work items

### 1. Extract the standalone project and establish provenance

1. Create the standalone repository without first modifying the AircraftMod
   branch.
2. Copy the wrapper source, current protocol documentation, and relevant review
   evidence into the new repository.
3. Prefer preserving history with `git filter-repo`; otherwise create one clear
   import commit that records the AircraftMod source commit from which the files
   originated.
4. Rename all files, namespaces, display names, assembly names, and messages from
   `AircraftMod`/`VNEditorConsoleWrapper` to protocol-oriented names where
   applicable.
5. Replace project-specific option names with generic names, such as
   `--executable` rather than `--editor-path`, unless retaining an alias is
   necessary for a documented compatibility period.
6. Do not copy proprietary UDK binaries, PDB files, game binaries, game paths,
   package contents, reverse-engineering exports, or local test logs.
7. Add a source-provenance note for the implementation and confirm the copyright
   holder may relicense all original code.
8. Verify `git grep -i -E 'AircraftMod|build\.ps1'` returns no matches.

**Acceptance criteria**

- The project clones, restores, builds, and runs without the AircraftMod
  repository.
- No checked-in file refers to AircraftMod or `build.ps1`.
- The repository contains no proprietary executable, PDB, game package, secret,
  or local path.

### 2. Relicense the project as AGPL-3.0-only

1. Replace the current LICENSE text with the complete GNU Affero General Public
   License version 3.
2. Use the SPDX identifier `AGPL-3.0-only` in package and assembly metadata.
3. Replace MIT headers in original source files with a short AGPL notice, or
   remove redundant per-file legal headers if repository-level licensing is the
   chosen convention.
4. Include a `THIRD_PARTY_NOTICES` file for dependencies and any retained
   third-party code or documentation.
5. Verify every imported contribution is owned by the copyright holder or is
   compatible with AGPL-3.0-only; retain all required upstream notices.
6. Include `LICENSE`, `THIRD_PARTY_NOTICES`, and a source-location note in every
   binary release.
7. Add an explicit source-archive/repository link to the release description and
   ensure a tagged source snapshot remains available.
8. Do not make legal conclusions beyond the repository's observable provenance;
   resolve uncertain third-party material before the first tagged release.

**Acceptance criteria**

- SPDX metadata, file headers, LICENSE, and release contents all consistently
  identify `AGPL-3.0-only`.
- All original and third-party files have a documented license basis.

### 3. Pin a modern .NET and C# baseline

1. Pin the stable .NET 11 SDK in `global.json`:
    - prefer the newest installed/stable `11.0.x`;
    - use `rollForward: latestPatch`;
    - disallow prerelease SDKs.
2. If a newer stable .NET/C# release exists when implementation starts, update the
   baseline rather than forcing an obsolete version. Do not silently use a
   preview language mode.
3. Add `Directory.Build.props` with:
    - `TargetFramework` `net11.0` for the verified baseline;
    - `LangVersion` `15.0`;
    - nullable reference types and implicit usings enabled;
    - `TreatWarningsAsErrors`;
    - `EnableNETAnalyzers`;
    - latest stable analyzer level;
    - code-style enforcement in build;
    - deterministic builds;
    - Source Link and repository metadata for tagged builds.
4. Add central package management in `Directory.Packages.props`.
5. Add locked `packages.lock.json` files or equivalent locked restore inputs.
6. Configure `DebugType` as `portable` for managed projects in both Debug and
   Release. Keep optimization enabled for Release and disabled for local Debug.
   A portable PDB is separate symbol data; it does not turn a Release executable
   into a Debug build.
7. Enable deterministic builds and CI path mapping. Add Source Link/GitHub source
   metadata so a Release PDB can resolve the exact tagged source without relying
   on a developer's local path.
8. Confirm generated PDBs do not contain absolute developer/runner paths or
   secrets. Add an automated inspection step if practical.
9. Pin every package and CI action version. Do not use floating package versions,
   prerelease dependencies, or moving major tags for actions in final workflows.
10. Add `.config/dotnet-tools.json` for `dotnet format` and ReportGenerator.
11. Confirm every selected dependency supports .NET 11 and the current C#
    language version.

Suggested dependency baseline to recheck at implementation time:

- `System.CommandLine` 3.x
- xUnit v3 4.x
- `Microsoft.Testing.Extensions.CodeCoverage`
- ReportGenerator 5.x
- MinVer 8.x
- `Microsoft.SourceLink.GitHub`

**Acceptance criteria**

- `dotnet restore --locked-mode`, build, format, and analyzer checks all work from
  a clean checkout.
- No preview SDK, floating package version, or manual source version is required.
- The same Release build produces optimized binaries and matching portable PDBs.

### 4. Migrate the public CLI to System.CommandLine

Implement this primary contract:

```text
UE3DualModeConsoleWrapper
  --executable <path>
  [--working-directory <path>]
  [--no-color]
  --
  <target arguments...>
```

Example:

```powershell
.\UE3DualModeConsoleWrapper.exe `
  --executable 'C:\Program Files\UDK\Binaries\Win64\UDK.exe' `
  -- make -useunpublished -FORCELOGFLUSH
```

Implementation steps:

1. Add `System.CommandLine`.
2. Create a `RootCommand` with a required executable option and an optional
   working-directory option that defaults to the executable's directory.
3. Add a variadic positional argument representing every token after the first
   `--`.
4. Configure `TreatUnmatchedTokensAsErrors`.
5. Require the delimiter before target arguments so wrapper options cannot be
   confused with target options.
6. Consume only the first delimiter. Preserve a literal later `--` as a target
   argument.
7. Use strongly typed option values and `GetRequiredValue` rather than inspecting
   raw `args`.
8. Use the asynchronous `SetAction` overload that receives a cancellation token.
9. Let `System.CommandLine` own help, version, parse diagnostics, and completion.
10. Add `--no-color`; also honor the standard `NO_COLOR` environment variable.
11. Map wrapper usage/parse failures to exit code `2`, runtime failures to `1`,
    target exit codes to the target's code, and cancellation to `130`.
12. Remove the JSON argument option and all `System.Text.Json` use from CLI
    argument parsing.
13. Update every in-repository caller to use `ProcessStartInfo.ArgumentList`,
    adding `--` as an explicit wrapper token before target arguments.
14. Do not forward the delimiter itself to the target.
15. Run a small System.CommandLine compatibility spike first to lock down
    variadic `--` behavior for empty strings, option-like values, and repeated
    `--` tokens.

**Acceptance criteria**

- Exact argument boundaries are preserved without JSON.
- Paths, empty values, values beginning with `-` or `--`, quotes, tabs, and
  trailing backslashes round-trip unchanged to the child.
- `--help`, `--version`, and shell-completion paths work.
- Invalid wrapper syntax fails before a process is started.

### 5. Refactor for responsibility and testability

Split the current monolithic source file only where it creates a clear
implementation or testing benefit:

- `Program`: constructs and parses wrapper CLI options, invokes the launcher,
  and maps exceptions and exit codes.
- `DualModeLauncher`: coordinates the complete child/pipe/job lifecycle,
  including suspended startup, protocol objects, output forwarding, graceful
  shutdown, forced shutdown, and cleanup.
- `NativeProcessCommandLine`: quotes and builds the native `CreateProcessW`
  command line passed from the wrapper to the target process.
- `DualModeOutputDecoder`: parses text and color-control records.
- `NativeMethods`: contains source-generated or generated native definitions.
- immutable value types for started-process metadata when a separate
  representation remains useful.

Implementation rules:

1. Keep implementation types internal and expose only a test-facing friend
   assembly through `InternalsVisibleTo`.
2. Replace nullable setup state with an explicit lifecycle object only if tests
   show that it simplifies cleanup.
3. Keep `Program` as the CLI boundary. Do not introduce `CommandLineFactory`
   or another wrapper-CLI component unless tests demonstrate a concrete need.
4. Keep startup and shutdown inside `DualModeLauncher` initially. Do not
   introduce `SuspendedProcessStarter` or `ProcessShutdownController` unless
   the lifecycle becomes too complex to test or reason about as one owner.
5. Keep ownership of every process, thread, pipe, event, job, and cancellation
   source explicit and single. Small lifecycle and process metadata types may
   remain local to `DualModeLauncher`.
6. Remove the unused `StartedProcess` record.
7. Remove the redundant `_ = outputClosedEvent` statement.
8. Inject only the unavoidable boundaries:
    - console/output sink;
    - process control;
    - clock/timeout provider if needed.
9. Do not add a hosting framework, dependency-injection container, MediatR,
   Spectre.Console, or a plugin system. The tool is too small to benefit.

**Acceptance criteria**

- `Program` is the single wrapper-CLI parsing and exit-code mapping boundary.
- Native target quoting, output decoding, and native interop are isolated only
  where their test cases or API boundaries justify separate components.
- `DualModeLauncher` remains the single owner of the child process and protocol
  lifecycle.
- Unit tests use narrow fakes instead of `public` implementation classes or a
  global service locator.

### 6. Modernize and harden native process handling

Use `System.Diagnostics.Process` after native startup for:

- `WaitForExitAsync(CancellationToken)`;
- exit code access;
- main-window close requests during graceful shutdown.

Retain direct `CreateProcessW` because `ProcessStartInfo` cannot express
`CREATE_SUSPENDED`.

Continue to prefer source-generated `[LibraryImport]` and SafeHandle types. Use
CsWin32 only if the Job Object or console-control interop becomes too large or
fragile when handwritten. If adopted, migrate the Windows declarations coherently
rather than mixing generated and handwritten definitions for the same APIs.

Add a Windows Job Object:

1. Create and configure the Job Object before launching the target.
2. Set `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` and do not enable any breakaway
   option.
3. Create the target with `CREATE_SUSPENDED`.
4. Assign the suspended target to the Job Object before `ResumeThread`. If
   assignment fails, terminate the still-suspended child and never resume it.
5. Create the byte-mode PID pipe and the initially unsignaled manual-reset
   `dualmode_die_event<PID>` event.
6. Create the initially unsignaled manual-reset `ComWrapperShutdown` event.
   Preserve the bare name for UDK protocol compatibility; document that
   concurrent wrappers can share it and therefore interfere with one another.
7. Start wrapper-owned connection and process waits, then call `ResumeThread`
   exactly once after all required protocol and cleanup objects exist.
8. Treat pipe end-of-file and process exit as the authoritative normal-completion
   signals. The PID close event remains a compatibility hint; do not require it
   to signal. `ComWrapperShutdown` is wrapper-signaled only during graceful
   cancellation.
9. Treat `TerminateJobObject` as the authoritative forced-shutdown operation for a
   resumed process and all descendants that remain in the job. Terminate with
   cancellation code `130` for user-forced cancellation and runtime-error code
   `1` for operational cleanup.
10. Use `TerminateProcess` for a target that never resumed. Do not use
    `Process.Kill(entireProcessTree: true)` in the normal lifecycle.
11. Keep normal output and connection waits cancellable only at boundaries that
    cannot abandon a live target. Cleanup waits must use uncancelled bounded or
    native waits where appropriate.
12. Dispose resources in reverse ownership order after the target is stopped or
    exited: active waits, pipe, events, process wrapper, thread, and Job Object.
    A failed setup operation must run the same deterministic cleanup path.

Do not use `ProcessStartInfo.Arguments` for generated command lines. Keep the
Windows quoting algorithm because `CreateProcessW` receives one mutable native
command-line buffer, but isolate and test it.

Quoting changes:

1. Move command-line construction into `NativeProcessCommandLine` so it cannot
   be confused with wrapper CLI parsing in `Program`.
2. Reject embedded NUL characters before native launch.
3. Test spaces, tabs, quotes, empty strings, leading/trailing backslashes, and
   every combination required by the Microsoft CRT command-line rules.
4. Add a Windows-only test that decodes generated command lines with
   `CommandLineToArgvW` and compares each original token.
5. Use a synthetic child that parses `argv` with the Microsoft CRT and records
   its exact token array. Keep this alongside `CommandLineToArgvW` because the
   two parsers provide complementary coverage.

**Acceptance criteria**

- No target or descendant that remains assigned to the Job Object survives
  normal wrapper failure, graceful failure, forced shutdown, or abrupt wrapper
  closure.
- A failed Job Object assignment never runs the target resumed.
- Failure of job creation/configuration, suspended process creation, Job Object
  assignment, pipe/event creation, wait setup, or resume leaves no running
  suspended or resumed target and deterministically disposes acquired handles.
- Every successful startup resumes the primary thread exactly once and only
  after the Job Object, pipe, events, and waits are ready.

### 7. Implement two-stage Ctrl+C shutdown semantics

Handle `Console.CancelKeyPress` with a thread-safe state machine and
`Interlocked`/atomic state rather than boolean races.

First Ctrl+C:

1. Set `ConsoleCancelEventArgs.Cancel = true` so the runtime does not terminate
   immediately.
2. Write a single clear message:
   `Exiting... Press Ctrl+C again to force exit.`
3. Signal the initially unsignaled `ComWrapperShutdown` event once so cooperative
   UE3 server targets begin their normal game-ending path.
4. Start graceful shutdown and continue draining the named pipe so the target is
   not blocked by a full output buffer.
5. Attempt `Process.CloseMainWindow()` once for an interactive target when a main
   window exists; do not synthesize keystrokes or automate dialogs.
6. Cancel wrapper-owned waits only at points where doing so is safe for output
   draining and lifecycle cleanup.
7. Keep waiting for normal target completion. The user can issue a second Ctrl+C
   if graceful shutdown stalls.

Second or later Ctrl+C:

1. Keep preventing the default handler from racing wrapper cleanup.
2. Write: `Forcing exit...`
3. Immediately terminate the Windows Job Object and all processes assigned to it.
4. Cancel remaining asynchronous operations, wait for process termination and
   output cleanup without cancelling cleanup waits, dispose native resources,
   and return cancellation code `130`.
5. Make forced shutdown idempotent if Ctrl+C arrives several times concurrently.

Apply the same state machine to `Ctrl+Break` where Windows reports it through the
same console event; document Ctrl+C as the primary behavior.

Do not call blocking process APIs from `Console.CancelKeyPress` except a small,
well-bounded native job termination call. Route longer cleanup to the main
async control flow.

Tests:

1. First signal switches to graceful state and leaves the child alive.
2. Graceful state signals `ComWrapperShutdown`, requests a close where
   applicable, and continues output draining.
3. Second signal terminates the job, main child, and synthetic descendants.
4. Multiple signals are idempotent.
5. A normal exit during graceful shutdown produces the expected cancellation
   result and no orphan.

**Acceptance criteria**

- The exact first/second-signal behavior is observable in a fake process test.
- The Windows integration test proves all started descendants are gone after
  forced shutdown.

### 8. Fix review findings in output and cleanup paths

1. Move final `Console.ResetColor()` into a `finally` block in the output path.
2. Reset color after EOF, pipe failure, decoder failure, cancellation, and console
   write failure.
3. Inject an output/color abstraction so unit tests do not change the test
   runner's terminal.
4. Use `StreamReader.ReadAsync(Memory<char>, CancellationToken)` rather than the
   older array-only overload where practical.
5. Create the UTF-16 decoder explicitly as UTF-16LE with strict fallback so a
   truncated final code unit is reported instead of silently replaced.
6. Match the exact UDK.com control parser for the verified 2015 build: parse the
   first four binary digits as a base-2 palette index and select from its
   16-entry foreground `ConsoleColor` palette. In that palette `0001` is
   deliberately `Black`. Do not interpret `1111` as hexadecimal, and do not
   apply background colors. Support a longer suffix only as ignored extension
   compatibility unless a target-side trace proves it carries protocol meaning.
   Preserve `Reset` behavior case-insensitively.
7. Treat a color control as the entire `marker + payload + CRLF` record. Consume
   the trailing `\r\n` as part of the control record; forwarding it creates
   extra blank lines around colored UE3 output.
8. Preserve marker, reset, binary payload, and terminating CRLF across every
   possible read boundary. Consume valid color records rather than forwarding
   them.
9. Catch expected operational exceptions and cleanup `AggregateException` in the
   top-level boundary.
10. Report a concise primary message to stderr and preserve the original and
    cleanup exceptions in one aggregate diagnostic.
11. Do not catch fatal runtime failures such as stack overflow or out-of-memory
    conditions.
12. Flush output after each complete read batch.

**Acceptance criteria**

- Error paths never leave altered terminal color.
- Cleanup failure returns `1` through the documented top-level handler and does
  not crash with an unhandled stack trace.
- Ordinary output remains byte-for-byte equivalent apart from consumed color
  records.

### 9. Verify the implementation against UDK.com at development time

This is a required development/reference task, not a CI dependency.

1. Obtain a licensed local copy of the matching `UDK.com` and `UDK.exe`, plus
   PDBs where they are available. Record exact file versions and SHA-256 hashes
   only in local development notes.
2. Import `UDK.com` and `UDK.exe` plus available PDBs into Ghidra. For a managed
   `UDK.com`, use a C# decompiler to recover method-level behavior and Ghidra to
   verify PE/managed metadata; use PDB-assisted Ghidra analysis for the native
   target.
3. Trace:
    - target startup;
    - argument construction and quoting;
    - suspended/startup ordering, if any;
    - pipe name and security mode;
    - pipe byte/message mode and buffers;
    - UTF-16 handling;
    - event creation and signaling;
    - close-event reset mode and target close ordering;
    - output loop and close detection;
    - color-control marker mapping;
    - target exit/exit-code handling;
    - process-tree and Ctrl+C behavior.
4. Produce a parity matrix with columns for UDK.com behavior, current wrapper
   behavior, proposed wrapper behavior, observable test, and reason for an
   intentional difference. Keep that matrix in this plan; `docs/udk-com-reference.md`
   must contain only observed UDK.com/UE3 target behavior.
5. Intentionally differ where the standalone wrapper is safer:
    - asynchronous I/O instead of polling;
    - suspended startup for the known PID;
    - Job Object cleanup;
    - explicit two-stage Ctrl+C;
    - strict two-signal completion.
6. Mirror UDK.com behavior for externally visible protocol details unless the
   trace proves a defect or the new wrapper explicitly documents the safer
   alternative.
7. Add synthetic fixtures for every protocol behavior confirmed from UDK.com.
8. Record behavior, structures, constants, and test expectations needed for
   interoperability. Short, attributed decompiled snippets may be included in
   `docs/udk-com-reference.md` when they clarify behavior; do not reproduce the
   entire proprietary implementation.
9. Repeat the comparison after a target-protocol or UDK build change.

**Acceptance criteria**

- `docs/udk-com-reference.md` contains a human-readable, technically detailed
  behavioral reference for UDK.com and its target without wrapper-status,
  testing, parity, or replacement advice.
- The implementation parity matrix remains in this plan and is updated with the
  matching UDK.com behavior and any intentional difference.
- Every externally observable protocol decision is either matched by UDK.com or
  listed as an intentional documented difference.
- CI depends only on synthetic fixtures, never on Ghidra, PDBs, UDK.com, or
  UDK.exe.

### UDK.com implementation parity matrix

This matrix belongs here because it compares the reference behavior with the
implementation. `docs/udk-com-reference.md` records only the reference facts.

| Behavior | Verified UDK.com/UE3 behavior | Current implementation | Required implementation direction |
| --- | --- | --- | --- |
| Target selection | UDK.com derives a same-stem `.exe` and has Win32/Win64 fallback behavior | Explicit `--executable` | Intentional generalization |
| Working directory | UDK.com inherits its own current directory | Defaults to executable directory, with optional override | Documented intentional difference |
| Argument forwarding | UDK.com joins strings and quotes only values containing spaces | Stronger `System.CommandLine` parsing plus CRT-aware native quoting | Intentional robustness improvement; add round-trip tests |
| Startup order | UDK.com starts the target, then creates the PID event and pipe | Creates target suspended, then prepares protocol objects before resume | Intentional race fix |
| Pipe name/mode | `\\.\pipe\<PID>cout`, inbound byte pipe, one instance, 1024-byte buffers | Same | Preserve |
| Pipe security | Null default security descriptor | `CurrentUserOnly` | Intentional hardening; verify same-user target interoperability |
| PID close event | Manual-reset, initially unsignaled `dualmode_die_event<PID>` | Auto-reset | Fix to match manual-reset semantics |
| `ComWrapperShutdown` | Created/opened by the launcher during server Ctrl+C and shared by name | Missing | Add for cooperative server shutdown |
| Read path | Synchronous 1024-byte reads; recognizes controls only at the start of one read | Async stream reads with boundary-aware decoder | Intentional robustness improvement |
| UTF-16LE framing | Target writes UTF-16LE without BOM; launcher assumes complete code units per read | Stream decoder is boundary-aware but not strict yet | Preserve boundary handling and add strict fallback |
| Color payload | First four binary digits become a base-2 index into a 16-entry foreground palette; suffix characters are ignored | Direct RGBI bit mapping, including background extension | Fix exact four-digit palette mapping; remove non-baseline background behavior |
| `0001` color | UDK.com selects its palette entry `Black` | Current direct mapping selects intensity-only foreground | Fix and test explicitly |
| Reset | Any control payload beginning with `Reset` calls `Console.ResetColor`; its CRLF is not printed | Case-insensitive reset, whole control CRLF consumed | More tolerant matching is acceptable; preserve whole-record consumption |
| Normal completion | Pipe close event or process exit ends polling; a close-event path can return `0` while child state is unresolved | Waits for pipe EOF/output drain and process exit | Intentional stricter two-signal completion |
| Exit code | Direct target exit code after process exit; `0` after close-event-only completion | Direct target code; `0` without a connection is a protocol error | Intentional diagnostic difference |
| `-log` routing | Target sends startup through pipe, then later commandlet/compiler records to `Launch.log` | Passes through the target-selected pipe stream | Preserve; no automatic log merge |
| Ctrl+C | Server: signal event; non-server: kill main target; no two-stage state | Two-stage graceful/forced state planned | Intentional UX/lifecycle improvement |
| Process tree | No Job Object | Job Object planned | Intentional hardening |

### 10. Add focused unit tests

Use xUnit v3 on Microsoft Testing Platform. Create ordinary test data with
theories and member data; do not add a custom fluent assertion framework unless a
testability problem proves it necessary.

Command-line tests:

- required options;
- missing delimiter;
- invalid wrapper option;
- empty forwarded argument list;
- option-like target arguments;
- target `--` tokens;
- paths/values with spaces;
- `--no-color` and `NO_COLOR`;
- help and version output;
- documented exit-code mapping.

Command-line quoting tests:

- ordinary text;
- empty string;
- spaces and tabs;
- embedded quotes;
- leading/trailing and repeated backslashes;
- backslashes preceding quotes;
- NUL rejection;
- a Windows `CommandLineToArgvW` round trip.

Decoder tests:

- plain UTF-16LE;
- ordinary records retain their `\r\n` and optional category prefix;
- truncated UTF-16 code units;
- every split point in the color marker;
- every split point in `Reset`;
- every split point in a control record's terminating `\r\n`;
- every four-digit foreground palette payload `0000` through `1111`, with a
  special assertion that `0001` selects `Black`;
- a longer suffix such as `11111111` is ignored after the first four digits;
- no extra blank line is emitted before or after a color or reset record;
- case-insensitive `Reset`;
- malformed and incomplete records;
- redirected-output behavior;
- reset after normal EOF;
- reset after an injected exception.

Lifecycle state-machine tests:

- job creation failure;
- job configuration failure;
- start failure;
- Job Object assignment failure;
- pipe-event and shutdown-event creation failure;
- resume failure;
- exit before pipe connection;
- pipe connection failure;
- output failure;
- graceful Ctrl+C;
- forced Ctrl+C;
- concurrent/repeated Ctrl+C;
- cleanup failure;
- process exit during cleanup.

Use in-memory streams, `StringWriter`/fake output sinks, fake handles, and
process-control interfaces. Never depend on a real developer console for a unit
test.

**Acceptance criteria**

- A change to argument parsing, quoting, color parsing, or Ctrl+C state transitions
  fails a focused test.
- All unit tests pass on `windows-latest`. Keep individual components reasonably
  isolated, but do not add a Linux CI job solely to assert that platform-neutral
  helpers happen to compile there; Windows is the supported execution platform.

### 11. Add synthetic end-to-end Windows tests

Perform this before deciding whether the E2E suite is worth retaining in every
CI run.

Build `FakeUDK.exe` as a minimal target-side implementation of the UE3 dual-mode
protocol. The project remains a synthetic test fixture named
`UE3DualModeConsoleWrapper.FakeUDK`; CI must never replace it with a real
`UDK.exe`.

`FakeUDK.exe` must:

1. use its own PID to connect to `\\.\pipe\<pid>cout`;
2. open `dualmode_die_event<pid>` and signal it before closing the pipe;
3. open `ComWrapperShutdown` for cooperative server-shutdown tests;
4. write its exact argument array to a test-controlled file;
5. recognize `make` as its normal E2E command and emit UDK-like UTF-16LE log
   records for command parsing, script compilation, progress, success/failure,
   and final elapsed time. Support a configurable compact or startup-prefix
   profile. The compact profile begins with
   `Log: Executing Class UnrealEd.MakeCommandlet`; the startup-prefix profile
   additionally emits UDK-like `Init:` records. Do not assume that absence of
   `-log` universally suppresses `Init:`: observed UDK 2015 emits an engine
   startup prefix without `-log`, while the observed VNEditor path is concise.
   Include at least one `Warning:` and `Error:` record in the dedicated
   severity-coverage scenario. Lowercase `info:`, `success:`, and `error:`
   messages are producer-side `build.ps1` output and are not emitted by FakeUDK;
6. support an explicit `-log` mode that reproduces the observed UE3 routing
   change: emit `Init:` and early `Log:` startup records to the inherited pipe,
   then disconnect the dual-mode output device and discard later simulated
   compiler output. The fixture does not need to create `Launch.log`; only the
   text delivered through the wrapper to the terminal is in scope. Keep normal
   `make` without `-log` on the pipe for compilation-rich commandlet output;
7. simulate compilation for one randomly selected total delay between 200 and
   1000 milliseconds. Allocate the delay across several visible synthetic work
   stages and report the selected duration in the controlled result metadata;
8. emit four-digit binary color records in deliberately split byte writes,
   including `1000`, `1101`, and `1111`. Use distinct representative foreground colors for
   normal/log, warning, and error output, and emit `Reset` after each colored
   record or record group. Treat exact category-to-color constants as a
   development-time parity check rather than inventing new wire syntax;
9. close the pipe before remaining alive when the corresponding scenario
   requests it;
10. exit with a selected code;
11. block indefinitely when requested;
12. start a tracked descendant for process-tree tests.

Keep low-level fault controls separate from normal `make` behavior. Tests may
select exit-before-connect, close-before-exit, blocked-child, and descendant
modes through documented target-only arguments or a test fixture configuration;
none of those controls may alter wrapper CLI parsing.

The existing UDK.com/Ghidra evidence already defines the marker, UTF-16LE
encoding, four-digit foreground palette payload format, and reset record, so a
new debugger session is not required merely to implement representative log
colors. Before claiming
exact parity for FakeUDK's normal/warning/error colors, perform a narrow
PDB-backed trace of the UDK log call sites that emit color records and document
the exact payload chosen for each severity. X64dbg is optional; static
PDB-backed Ghidra analysis is sufficient if the symbols resolve those call
sites.

Required scenarios:

1. Invoke `FakeUDK.exe` with `make`, verify exact argument forwarding and
   realistic split UTF-16LE output with distinct valid normal, warning, and
   error color records, observe delayed exit after pipe EOF, and propagate
   exit code `7`.
2. Exit `0` without a pipe connection: wrapper reports a protocol error.
3. Nonzero exit without a pipe connection: child code is propagated.
4. First/second Ctrl+C semantics.
5. Cooperative server shutdown through `ComWrapperShutdown`, with continued
   output draining.
6. Cancellation while waiting for a connection.
7. Cancellation while forwarding output.
8. Descendant termination through the Job Object.
9. Abrupt termination of the wrapper followed by child/descendant termination.
10. Color reset after an injected output failure.
11. The random `make` workload selects and reports a total synthetic delay
    between 200 and 1000 milliseconds while the wrapper remains attached and
    drains all output. Assert the selected fixture value, not the wall-clock
    duration, so scheduler overhead does not make CI flaky.
12. Assert that the normal no-`-log` path emits the selected compact or
    startup-prefix profile, plus concise `Log:`/heading output
    plus dedicated `Warning:` and `Error:` records, each with a valid non-reset
    four-digit palette color record and `Reset` around the colored record or record
    group. Select the compact profile for this assertion and separately cover
    the startup-prefix profile to model UDK 2015.
13. Exercise a realistic failed-compile path without `-log`: multiple
    UnrealScript diagnostic lines, a
    `Warning/Error Summary`, `Log: Compile aborted due to errors.`, and
    `Log: Failure - N error(s), M warning(s)`, followed by commandlet elapsed
    time and shutdown records.
14. Run `make -log` and verify that the wrapper faithfully preserves the
    target-selected terminal output: startup records cross the pipe, later
    compiler records are deliberately absent from terminal output, and the
    wrapper propagates the target exit code while continuing to wait for process
    exit.

### FakeUDK end-to-end scenario matrix

| Scenario                                   | FakeUDK input/state                                        | Expected terminal output                                                                                                                                                        | Expected wrapper/cleanup result                                                                                                       |
|--------------------------------------------|------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------|
| Successful compact commandlet `make`       | `make`; no `-log`; compact profile; random synthetic delay | `Log: Executing Class UnrealEd.MakeCommandlet`, package headings, `Scripts successfully compiled...`, `Success - 0 error(s), N warning(s)`, and elapsed time; no `Init:` prefix | Forward all UTF-16LE text, consume control records including their CRLF, wait through pipe EOF if configured, return `0`              |
| Successful startup-prefix `make`           | `make`; no `-log`; startup-prefix profile                  | `Init:` startup records followed by commandlet/compiler records                                                                                                                 | Match UDK 2015-style output, preserve ordinary text byte-for-byte, consume only control records, return `0`                           |
| Failed commandlet `make`                   | `make`; synthetic compile failure                          | Package headings, `Error:` compiler diagnostics, `Warning:` records, `Compile aborted due to errors.`, warning/error summary, failure total, and elapsed time                   | Forward all records, consume color controls, return configured nonzero target code                                                    |
| Exact argument forwarding with delayed EOF | Target argument array plus exit code `7`                   | Exact commandlet records emitted in split writes; no wrapper-added or missing tokens                                                                                            | Preserve exact argument file, continue after pipe EOF until exit, return `7`                                                          |
| Severity/color coverage                    | Normal/log, warning, and error records                     | Text for each severity, no visible marker/reset text, and no blank line introduced by control-record CRLF                                                                       | Parse UDK.com's four-digit binary palette index, consume record CRLF, apply foreground only, and leave terminal color restored |
| `-log` routing                             | `make -log`                                                | `Init:` and early `Log:` startup records only; later compiler diagnostics and summaries absent                                                                                  | Wait for normal process exit, propagate its code, do not inspect or merge a log file                                                  |
| Exit `0` before connection                 | Skip pipe connection and exit `0`                          | No target output                                                                                                                                                                | Report protocol error and return wrapper runtime code `1`                                                                             |
| Nonzero exit before connection             | Skip pipe connection and exit configured nonzero code      | No target output                                                                                                                                                                | Propagate the exact target code                                                                                                       |
| First Ctrl+C                               | First signal while FakeUDK is active                       | One `Exiting... Press Ctrl+C again to force exit.` message; target may emit cooperative shutdown/output records                                                                 | Keep draining, leave child alive during graceful phase, eventually return cancellation code `130`                                     |
| Second Ctrl+C                              | Second/later signal or forced path                         | One `Forcing exit...`; output may end mid-record                                                                                                                                | Terminate the Job Object idempotently, no surviving child/descendant, return `130`                                                    |
| Cooperative server shutdown                | FakeUDK opens `ComWrapperShutdown`                         | Final shutdown text and EOF after event signal                                                                                                                                  | Continue draining; return normal/cancellation result according to completed state                                                     |
| Cancel while connecting                    | Delay or omit pipe connect                                 | No ordinary compile output                                                                                                                                                      | Terminate target cleanly; no orphan                                                                                                   |
| Cancel while forwarding                    | Stream deliberately split records during cancellation      | Complete received batches only; no marker/control text                                                                                                                          | Terminate target tree and restore color; return `130`                                                                                 |
| Descendant cleanup                         | FakeUDK starts tracked child                               | Optional startup text from parent                                                                                                                                               | Job Object termination removes parent and descendant                                                                                  |
| Abrupt wrapper termination                 | Wrapper process is externally terminated mid-stream        | Output can stop mid-record                                                                                                                                                      | `KILL_ON_JOB_CLOSE` removes FakeUDK and descendants                                                                                   |
| Output sink failure                        | Emit colored record, then inject wrapper write failure     | Colored record may fail to render completely                                                                                                                                    | Restore color, stop target, report wrapper runtime failure                                                                            |
| Random timing                              | `make` selects a delay in `[200, 1000]` ms                 | Same deterministic staged text regardless of selected delay                                                                                                                     | Assert the fixture metadata delay is in range, not wall-clock duration                                                                |

E2E policy:

- Keep the suite if it can complete predictably on `windows-latest` and exposes
  races that unit tests cannot.
- Require explicit per-test timeouts; cap the whole job.
- Keep random timing useful but not behavioral: it may vary the scheduling
  pressure, but output expectations, exit codes, and process cleanup must be
  deterministic.
- Investigate any flaky run before merge.
- Keep a slower real-editor smoke-test script manual; never depend on UDK.com,
  VNEditor, or a game install in CI.

The expected decision is to retain the synthetic E2E suite because the critical
behavior depends on Windows process, pipe, handle, and Job Object interactions.
Drop or reduce only a specific scenario shown to be non-deterministic and
replace it with a deterministic lower-level test.

**Acceptance criteria**

- All synthetic scenarios pass repeatedly on Windows.
- No synthetic child or descendant remains after a successful or failed test.
- `FakeUDK.exe` exercises `make` through the same pipe, event, UTF-16LE, color,
  and lifecycle behavior as a UE3 target.
- FakeUDK's severity-coverage `make` run covers distinct normal, warning, and
  error log colors and resets the console after each colored record or record
  group.
- FakeUDK covers both commandlet-rich output without `-log` and UE3-style
  terminal routing with `-log`: only startup records are forwarded after which
  later simulated compiler output is absent from the terminal.
- FakeUDK does not invent lowercase `info:`, `success:`, or `error:` categories.
  If future consumer-level E2E tests execute a build script, those messages
  remain assertions for the consumer, not for the UE3 target protocol.
- The `TESTING.md` document clearly separates synthetic CI tests from manual
  real-editor validation.

### 12. Keep README focused and move technical material to docs

Rewrite `README.md` as a short user guide. It should contain only:

1. one-sentence purpose;
2. Windows and runtime prerequisites;
3. download/install from a GitHub release;
4. two or three command examples;
5. a basic explanation of `--`;
6. a short note that behavior is based on the UE3 `UDK.com` dual-mode wrapper;
7. exit codes;
8. links to technical docs, releases, license, and source;
9. status and coverage badges.

Keep the README free of reverse-engineering notes, internal class diagrams,
native structure layouts, lengthy compatibility matrices, architecture trivia,
or implementation workflow.

Create these verbose technical documents:

- `docs/protocol.md`: pipe/event names, UTF-16LE, color records, startup race,
  buffering, EOF and process-wait semantics.
- `docs/architecture.md`: process/pipe/job ownership, async flows, cancellation,
  error reporting, and Ctrl+C state transitions.
- `docs/udk-com-reference.md`: UDK.com parity matrix, Ghidra/PDB observations,
  protocol constants, and documented intentional differences.
- `docs/testing.md`: exact unit, integration, and manual real-target commands.
- `docs/development.md`: build, format, package restore, dependency update, and
  local debugging guidance.
- Optional `docs/releasing.md`: tag, version, artifact, and release checks.

Document why CI uses Release: Debug changes optimization and conditional
compilation, so running only Debug would not verify the code that users download.
Explain that Release can still produce portable PDBs and that those PDBs are
published as a separate optional symbol archive for matching crash/debugger
investigations.

Human-language requirements:

- Prefer short sentences and concrete examples.
- Explain acronyms on first use.
- Use active voice and direct commands.
- Avoid unexplained internal terminology.
- Do not mention LLMs, prompts, coding agents, or generated code.
- End with a search for stale JSON examples, AircraftMod, `build.ps1`, and
  obsolete MIT wording.

**Acceptance criteria**

- A user can install and invoke the wrapper from the README alone.
- A contributor can understand and reproduce the implementation from `docs/`
  without reading source first.

### 13. Add GitHub CI with artifacts, logs, coverage, and metadata

Make `ci.yml` reusable with `workflow_call` and also run on pull requests, pushes
to the default branch, and manual dispatch.

Use `windows-latest` for the primary `build-and-test` job:

1. checkout full history;
2. install the exact SDK from `global.json`;
3. restore with locked dependencies;
4. verify formatting;
5. run analyzer/warnings-as-errors builds with `--configuration Release`;
6. run PSScriptAnalyzer;
7. run all unit and synthetic Windows integration tests in Release;
8. collect coverage and test reports;
9. run package vulnerability audit;
10. verify no test child remains;
11. publish a self-contained `win-x64` smoke-test artifact.

Do not add an `ubuntu-latest` build or test job. Linux cannot run the UE3
named-pipe, `CREATE_SUSPENDED`, Job Object, console-signal, or child-process
checks that define this product. A Linux job would test only duplicated,
platform-neutral helper code while adding CI time, a second artifact set, and a
second coverage stream. Run every required check on Windows instead.

The default-branch coverage Pages deployment may be a separate job for workflow
permissions and isolation, but it should also use `windows-latest` unless a
selected Pages action proves Windows-incompatible during implementation. The
official Pages upload action has a Windows archive path, so the platform does
not justify a Linux runner.

The primary job's detailed steps are:

1. `quality-and-unit` work:
    - checkout full history;
    - install the exact SDK from `global.json`;
    - restore with locked dependencies;
    - format verification;
    - analyzer/warnings-as-errors Release build;
    - PSScriptAnalyzer over `scripts/ci`;
    - all unit tests with the Release configuration and `--no-build`;
    - package vulnerability audit.
2. `integration-and-publish` work:
    - use the Release output for the wrapper and `FakeUDK.exe`;
    - run synthetic Windows integration tests;
    - collect coverage;
    - verify no FakeUDK process remains;
    - publish a `win-x64` smoke-test artifact.

Log retention:

- Create `artifacts/logs` at job start.
- Keep CI orchestration in small, checked-in PowerShell 7 helpers under
  `scripts/ci/` where that reduces YAML duplication. Use helpers for:
    - running a command while preserving its output and exit code;
    - writing command output to durable log files;
    - generating or merging coverage commands;
    - staging artifacts;
    - generating checksum files;
    - collecting the environment values used by the metadata manifest.
- Give helpers `Set-StrictMode -Version Latest` and
  `$ErrorActionPreference = 'Stop'`. Do not hide a child process exit code.
- Keep the helper names generic; do not add an AircraftMod-style `build.ps1`.
- Run PSScriptAnalyzer on the helpers and verify their path, quoting, and exit
  handling on `windows-latest`.
- Capture restore, format, build, test, and package-audit output to separate
  files while retaining normal console output in Actions.
- Upload logs with `if: always()`.
- Upload TRX/test XML, coverage XML, HTML coverage, and crash/test diagnostics
  with `if: always()`.
- Do not continue on errors for required checks; required failures must fail the
  job.

Build artifact retention:

- Upload the built/published Windows package, source/publish manifest, and
  checksums.
- Keep portable PDBs in a distinct symbols artifact. Do not omit them from CI
  build artifacts merely because the end-user runtime package excludes them.
- Use stable artifact names containing OS, target RID, and configuration.
- Set a sensible repository-wide retention period and avoid duplicating large
  Debug builds. There is one Release build/test path unless an identified defect
  justifies a small Debug-specific check.

Build metadata manifest:

Adapt the referenced Python workflow's `build_metadata.txt` into a structured
`build-metadata.json`, rather than key/value shell output. Use a small C# utility
under `scripts/` and `System.Text.Json` for correct escaping and deterministic
serialization. A PowerShell helper in `scripts/ci/` should collect environment/job
values and invoke that utility; this keeps GitHub Actions YAML small without
duplicating JSON serialization logic in shell code. Include:

- UTC build timestamp;
- package version and informational version;
- .NET SDK and target framework;
- target RID and configuration;
- Git commit, ref, ref name, base/head ref, repository, and event;
- workflow, run ID, run number, and attempt;
- actor, runner OS, and runner architecture;
- source commit URL and Actions run URL;
- artifact names;
- SHA-256 checksums for final files;
- PDB paths and hashes paired with the executable/DLL paths they describe;
- workflow/job result where available.

Generate the manifest in a deterministic order, print it to the job summary,
upload it with both normal build artifacts and release assets, and include it in
the failure-upload step when later build steps fail.

Pin third-party GitHub Actions to immutable commit SHAs and use the minimum token
permission for each job.

**Acceptance criteria**

- A failed CI run still uploads available logs, test reports, and metadata.
- A successful run provides a downloadable build, metadata manifest, and
  checksum.
- All required build, test, coverage, and packaging checks run on Windows; no
  `ubuntu-latest` job is required.
- Required tests and quality checks cannot be bypassed by `continue-on-error`.

### 14. Add code coverage and a durable README badge

Use the modern Microsoft Testing Platform path:

1. Add `Microsoft.Testing.Extensions.CodeCoverage` to test projects.
2. Run xUnit v3 with MTP in Release and collect a Cobertura report from the
   matching portable PDBs.
3. Confirm the final command with the selected SDK/test package version; the
   current MTP convention uses the runner's `--coverage` and
   `--coverage-output-format cobertura` options after the test-runner separator.
4. Use ReportGenerator (not a Python badge generator) to produce:
    - HTML coverage report;
    - Cobertura XML;
    - JSON/text summary;
    - SVG `Badges`.
5. Upload raw coverage and generated reports as CI artifacts on success or
   failure.
6. Combine Windows unit and integration coverage into one report if the collector
   can merge them reliably; otherwise publish a clearly named Windows coverage
   badge/report.
7. Add an initial threshold only after measuring the first stable baseline. A
   threshold must prevent regressions, not force tests that only chase a number.
8. Do not add a second Debug coverage report. Release coverage is the useful
   signal because it tests the optimized configuration that is shipped.

Artifact publication:

1. On successful default-branch pushes, publish the coverage HTML and badge with
   GitHub Pages, following the durable-badge purpose of the referenced
   Python `publish-gh-pages.yml`. Package and deploy on `windows-latest`; do not
   add a Linux runner merely for static file deployment. Confirm Windows support
   for the pinned Pages action versions before finalizing the workflow.
2. The README badge must point to the stable Pages URL, not a short-lived workflow
   artifact URL.
3. If repository policy forbids Pages, publish only a Gist-hosted badge with an
   explicit, minimum-scope token; otherwise prefer Pages and `GITHUB_TOKEN`.
4. Use the official Pages artifact/deployment actions with least-privilege
   `pages: write` and `id-token: write` permissions.
5. Publish the badge only after tests and ReportGenerator succeed so it never
   advertises a partial or failed run.
6. Include coverage history or a trend badge only if retaining history does not
   make the Pages artifacts noisy; history is optional.

**Acceptance criteria**

- The README renders a durable coverage badge.
- The linked coverage report is browsable and corresponds to the latest
  successful default-branch run.
- Coverage collection and Pages publication succeed in the same Windows-only
  runner policy as the product build.
- Pull-request runs upload coverage artifacts but do not overwrite the
  default-branch badge.

### 15. Add tag-derived versioning with MinVer

1. Add MinVer as a private build-time package.
2. Adopt SemVer tags of the form `vX.Y.Z` and `vX.Y.Z-prerelease`.
3. Configure `MinVerTagPrefix` as `v`.
4. Remove manually maintained version numbers from production project files.
5. Ensure `--version` uses assembly/package informational version generated by
   MinVer.
6. Use full-history checkout in CI so MinVer can read tags.
7. Test exact-tag, post-tag commit, no-tag, and prerelease-tag cases.
8. Verify `dotnet msbuild -getProperty:Version -getProperty:PackageVersion
   -getProperty:InformationalVersion` during CI and compare it with the pushed
   release tag.

**Acceptance criteria**

- Building the same tagged commit produces the same release version.
- A development build cannot masquerade as a release version.

### 16. Add automatic GitHub releases for version tags

Trigger `release.yml` for tags matching `v*`.

Steps:

1. Validate the tag is SemVer after removing the `v` prefix.
2. Checkout full history and tags.
3. Reuse the verified CI workflow or require its equivalent build/test jobs.
4. Confirm MinVer's version exactly matches the tag.
5. Publish the optimized Release build as a self-contained `win-x64` single-file
   executable with portable symbols:

```powershell
dotnet publish `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:DebugSymbols=true `
  -p:DebugType=portable
```

Keep the portable PDB side-by-side in the publish output for staging, but do not
embed it in the executable. `DebugType=portable` with a Release optimization
profile is the intended release/debugging combination.

6. Smoke-test the published executable's `--help` and `--version`.
7. Stage a normal user ZIP without PDB files and a separate symbols ZIP containing
   the matching PDBs. The runtime package stays small and clean; developers and
   crash investigators can opt into the symbols archive.
8. Include these release assets:
    - product ZIP without PDBs;
    - `-symbols-win-x64.zip` containing portable PDBs;
    - SHA-256 checksum file;
    - `build-metadata.json`;
    - `LICENSE`;
    - `README.md`;
    - `THIRD_PARTY_NOTICES`;
    - tagged source/archive link.
9. Verify the PDB-to-binary association from the final publish output. Record and
   checksum each PDB with its matching executable/DLL, and retain Source Link
   metadata.
10. Generate release notes from merged pull requests with GitHub CLI, or use the
    GitHub release-notes API.
11. Mark prerelease tags as prereleases.
12. Fail if the release or any expected asset already exists; do not overwrite an
    immutable release silently.
13. Grant `contents: write` only to the release job.
14. Retain normal CI artifacts even if release creation fails.

**Acceptance criteria**

- Pushing a valid `vX.Y.Z` tag creates one complete, reproducible release.
- Every binary has a checksum, matching portable PDB where available, and
  matching build/version metadata.
- The normal user ZIP does not contain PDBs, while the separate symbols ZIP
  contains exactly the PDBs needed for that binary build.
- The release PDB resolves tagged source through Source Link and contains no
  unacceptable local path information.
- A failed release never exposes an incomplete release as successful.

### 17. Migrate external consumers separately

After the standalone tool has one validated release:

1. Update each consumer to download or build the release artifact independently.
2. Replace JSON argument transport with `ArgumentList` and the `--` delimiter.
3. Keep consumer-specific path discovery, package setup, and output validation
   outside the wrapper.
4. Pin the wrapper release or checksum.
5. Add a consumer-level smoke test before removing its old invocation path.
6. Do not keep AircraftMod compatibility names, examples, or scripts in the
   standalone repository.

## Dependency order and suggested delivery slices

1. Extract, relicense, pin toolchain.
2. Add System.CommandLine and exact forwarding tests.
3. Split the decoder and quoting components; add unit tests.
4. Add Job Object and two-stage Ctrl+C process cleanup.
5. Compare behavior against UDK.com/UDK.exe with PDBs and close parity gaps.
6. Add synthetic Windows E2E tests.
7. Rewrite README and technical docs.
8. Add CI, retained artifacts/logs/metadata, and coverage.
9. Add MinVer and GitHub releases.
10. Create the first prerelease, run a manual real-target smoke test, then migrate
    consumers.

Suggested pull-request boundaries:

1. Repository extraction, naming, AGPL, and SDK baseline.
2. CLI migration, parser tests, quoting tests, and decoder tests.
3. Process lifecycle, Job Object, Ctrl+C behavior, and synthetic E2E tests.
4. UDK.com parity evidence and technical documentation.
5. CI, coverage badge, build metadata, MinVer, and releases.

## Final verification matrix

Run from a clean clone:

```powershell
dotnet restore --locked-mode
dotnet format --verify-no-changes
dotnet build --configuration Release -warnaserror
dotnet test --configuration Release --no-build
dotnet publish --configuration Release --runtime win-x64 --self-contained true
git grep -i -E 'AircraftMod|build\.ps1'
```

Additional gates:

- Run Windows E2E tests at least several consecutive times.
- Prove forced shutdown leaves no child or descendant.
- Run argument round-trip tests against `CommandLineToArgvW`.
- Compare final protocol behavior with the UDK.com parity matrix.
- Verify `--help`, `--version`, and the exact documented command from a release
  ZIP.
- Inspect the generated source and binary release for the AGPL license,
  third-party notices, manifest, and checksums.
- Verify README badge and coverage report links after the first successful
  default-branch CI run.
- Verify the Release executable runs with its PDB absent, then verify the matching
  PDB can open and resolve symbols for the same executable in a debugger.
- Verify no generated binary, PDB, test output, log, or proprietary file is
  committed.

## Stopping criteria

Implementation is complete only when:

- the standalone repository contains no AircraftMod-specific code or docs;
- the CLI forwards arguments through `System.CommandLine` and `--` exactly;
- review findings and forced-cleanup requirements are covered by tests;
- behavior has a documented UDK.com comparison;
- README use is understandable without technical background;
- technical details are available under `docs/`;
- CI retains artifacts, logs, coverage, and a metadata manifest;
- CI tests the optimized Release build, with portable PDBs retained as a separate
  symbol artifact rather than embedded into the end-user executable;
- the README has a working coverage badge;
- MinVer produces the version from tags; and
- a tag-triggered GitHub release produces a licensed, checksummed `win-x64`
  package that passes its published smoke test.

