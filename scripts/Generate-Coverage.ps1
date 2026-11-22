# Copyright (C) 2026 Tuomo Kriikkula
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU Affero General Public License as published
# by the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU Affero General Public License for more details.
#
# You should have received a copy of the GNU Affero General Public License
# along with this program.  If not, see <https://www.gnu.org/licenses/>.

[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [string]$Configuration = 'Release',
    [string]$ArtifactsDirectory = 'artifacts'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..')).Path
$artifactsPath = Join-Path -Path $repositoryRoot -ChildPath $ArtifactsDirectory
$rawCoveragePath = Join-Path -Path $artifactsPath -ChildPath 'coverage/raw'
$reportPath = Join-Path -Path $artifactsPath -ChildPath 'coverage/report'
$unitReport = Join-Path -Path $rawCoveragePath -ChildPath 'unit.cobertura.xml'
$integrationReport = Join-Path -Path $rawCoveragePath -ChildPath 'integration.cobertura.xml'
$testProjects = @(
    @{
        Project = 'tests\UE3DualModeConsoleWrapper.UnitTests\UE3DualModeConsoleWrapper.UnitTests.csproj'
        Report  = $unitReport
        Name    = 'unit'
    },
    @{
        Project = 'tests\UE3DualModeConsoleWrapper.IntegrationTests\UE3DualModeConsoleWrapper.IntegrationTests.csproj'
        Report  = $integrationReport
        Name    = 'integration'
    }
)

Push-Location -Path $repositoryRoot
try
{
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet tool restore failed with exit code $LASTEXITCODE."
    }

    if (-not $SkipBuild)
    {
        & dotnet restore UE3DualModeConsoleWrapper.slnx --locked-mode
        if ($LASTEXITCODE -ne 0)
        {
            throw "dotnet restore failed with exit code $LASTEXITCODE."
        }

        $buildArguments = @(
            'build'
            'UE3DualModeConsoleWrapper.slnx'
            '--configuration', $Configuration
            '--no-restore'
            '-warnaserror'
        )
        & dotnet @buildArguments
        if ($LASTEXITCODE -ne 0)
        {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    }

    New-Item -ItemType Directory -Force -Path $rawCoveragePath | Out-Null
    if (Test-Path -LiteralPath $reportPath)
    {
        Remove-Item -LiteralPath $reportPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $reportPath | Out-Null
    Remove-Item -LiteralPath $unitReport, $integrationReport -Force -ErrorAction SilentlyContinue

    $testExitCodes = @()
    foreach ($test in $testProjects)
    {
        $testArguments = @(
            'run'
            '--project', $test.Project
            '--configuration', $Configuration
            '--no-build'
            '--'
            '--coverage'
            '--coverage-output-format', 'cobertura'
            '--coverage-output', $test.Report
        )
        & dotnet @testArguments
        $testExitCodes += $LASTEXITCODE
    }

    $missingReports = @(
        $testProjects |
            Where-Object { -not (Test-Path -LiteralPath $_.Report) } |
            ForEach-Object { $_.Name }
    )
    if ($missingReports.Count -gt 0)
    {
        throw "Coverage files were not generated for: $( $missingReports -join ', ' )."
    }

    $reportGeneratorArguments = @(
        'reportgenerator'
        "-reports:$( $testProjects.Report -join ';' )"
        "-targetdir:$reportPath"
        '-reporttypes:Html;Cobertura;JsonSummary;TextSummary;Badges'
    )
    & dotnet @reportGeneratorArguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "ReportGenerator failed with exit code $LASTEXITCODE."
    }

    $failedTests = @(
        for ($index = 0; $index -lt $testExitCodes.Count; $index++)
        {
            if ($testExitCodes[$index] -ne 0)
            {
                $testProjects[$index].Name
            }
        }
    )
    if ($failedTests.Count -gt 0)
    {
        throw "Coverage was generated, but test suites failed: $( $failedTests -join ', ' )."
    }
}
finally
{
    Pop-Location
}
