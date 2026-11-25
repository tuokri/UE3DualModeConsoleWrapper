# Testing

## Test traits

All tests have class-level traits for test level and behavior area:

| Trait key  | Values                                                  |
|------------|---------------------------------------------------------|
| `Category` | `Unit`, `Integration`                                   |
| `Area`     | `CLI`, `CommandLine`, `Output`, `Lifecycle`, `Protocol` |
| `Status`   | `TODO` when the test is an unfinished skeleton          |

`Category` describes how broadly the test runs. `Area` describes the subsystem
or behavior under test. The repository is Windows-only, so a `Platform=Windows`
trait would be redundant.

The existing `Status=TODO` trait is independent of category and area. A test
without that trait is expected to pass.

## Current class mapping

| Test class                         | Category      | Area          |
|------------------------------------|---------------|---------------|
| `ProgramCommandLineTests`          | `Unit`        | `CLI`         |
| `NativeProcessCommandLineTests`    | `Unit`        | `CommandLine` |
| `DualModeOutputDecoderTests`       | `Unit`        | `Output`      |
| `DualModeLauncherLifecycleTests`   | `Unit`        | `Lifecycle`   |
| `DualModeProtocolIntegrationTests` | `Integration` | `Protocol`    |

## Common filters

Run all unit tests:

```powershell
dotnet test --configuration Release -- --filter-trait "Category=Unit"
```

Run all integration tests:

```powershell
dotnet test --configuration Release -- --filter-trait "Category=Integration"
```

When filtering a trait present in only one test project, the other project may
report zero matching tests. `dotnet test` treats this as a successful solution
run as long as another project ran matching tests.

Run output tests:

```powershell
dotnet test --configuration Release -- --filter-trait "Area=Output"
```

Run every currently implemented test:

```powershell
dotnet test --configuration Release -- --filter-not-trait "Status=TODO"
```

Run only unfinished test skeletons:

```powershell
dotnet test --configuration Release -- --filter-trait "Status=TODO"
```
