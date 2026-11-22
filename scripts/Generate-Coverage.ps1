[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [string]$Configuration = 'Release',
    [string]$ArtifactsDirectory = 'artifacts'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactsPath = Join-Path $repositoryRoot $ArtifactsDirectory
$rawCoveragePath = Join-Path $artifactsPath 'coverage/raw'
$reportPath = Join-Path $artifactsPath 'coverage/report'
$unitReport = Join-Path $rawCoveragePath 'unit.cobertura.xml'
$integrationReport = Join-Path $rawCoveragePath 'integration.cobertura.xml'
$testProjects = @(
    @{
        Project = 'tests\UE3DualModeConsoleWrapper.UnitTests\UE3DualModeConsoleWrapper.UnitTests.csproj'
        Report = $unitReport
        Name = 'unit'
    },
    @{
        Project = 'tests\UE3DualModeConsoleWrapper.IntegrationTests\UE3DualModeConsoleWrapper.IntegrationTests.csproj'
        Report = $integrationReport
        Name = 'integration'
    }
)

Push-Location $repositoryRoot
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool restore failed with exit code $LASTEXITCODE."
    }

    if (-not $SkipBuild) {
        & dotnet restore UE3DualModeConsoleWrapper.slnx --locked-mode
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed with exit code $LASTEXITCODE."
        }

        & dotnet build UE3DualModeConsoleWrapper.slnx `
            --configuration $Configuration `
            --no-restore `
            -warnaserror
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    }

    New-Item -ItemType Directory -Force -Path $rawCoveragePath | Out-Null
    if (Test-Path -LiteralPath $reportPath) {
        Remove-Item -LiteralPath $reportPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $reportPath | Out-Null
    Remove-Item -Force -ErrorAction SilentlyContinue $unitReport, $integrationReport

    $testExitCodes = @()
    foreach ($test in $testProjects) {
        & dotnet run `
            --project $test.Project `
            --configuration $Configuration `
            --no-build `
            -- `
            --coverage `
            --coverage-output-format cobertura `
            --coverage-output $test.Report
        $testExitCodes += $LASTEXITCODE
    }

    $missingReports = @(
        $testProjects |
            Where-Object { -not (Test-Path -LiteralPath $_.Report) } |
            ForEach-Object { $_.Name }
    )
    if ($missingReports.Count -gt 0) {
        throw "Coverage files were not generated for: $($missingReports -join ', ')."
    }

    & dotnet reportgenerator `
        "-reports:$($testProjects.Report -join ';')" `
        "-targetdir:$reportPath" `
        '-reporttypes:Html;Cobertura;JsonSummary;TextSummary;Badges'
    if ($LASTEXITCODE -ne 0) {
        throw "ReportGenerator failed with exit code $LASTEXITCODE."
    }

    $failedTests = @(
        for ($index = 0; $index -lt $testExitCodes.Count; $index++) {
            if ($testExitCodes[$index] -ne 0) {
                $testProjects[$index].Name
            }
        }
    )
    if ($failedTests.Count -gt 0) {
        throw "Coverage was generated, but test suites failed: $($failedTests -join ', ')."
    }
}
finally {
    Pop-Location
}
