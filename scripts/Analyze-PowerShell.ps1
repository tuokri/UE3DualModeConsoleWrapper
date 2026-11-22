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
    [ValidateSet('Default', 'GitHub')]
    [string]$OutputFormat = 'Default'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$analyzerCommand = Get-Command -Name Invoke-ScriptAnalyzer -ErrorAction SilentlyContinue
if ($null -eq $analyzerCommand)
{
    throw 'PSScriptAnalyzer is required. Ensure Invoke-ScriptAnalyzer is available in PowerShell.'
}

$repositoryRoot = (Resolve-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..')).Path
$diagnostics = @(Invoke-ScriptAnalyzer -Path $PSScriptRoot -Recurse)
if ($diagnostics.Count -eq 0)
{
    Write-Output 'No PowerShell analyzer findings.'
    exit 0
}

function ConvertTo-GitHubCommandValue
{
    param([AllowNull()][string]$Value)

    return ([string]$Value).
    Replace('%', '%25').
    Replace("`r", '%0D').
    Replace("`n", '%0A')
}

function ConvertTo-GitHubCommandProperty
{
    param([AllowNull()][string]$Value)

    return (ConvertTo-GitHubCommandValue -Value $Value).
    Replace(':', '%3A').
    Replace(',', '%2C')
}

function Get-GitHubAnnotationKind
{
    param([string]$Severity)

    if ($Severity -eq 'Error' -or $Severity -eq 'ParseError')
    {
        return 'error'
    }

    if ($Severity -eq 'Warning')
    {
        return 'warning'
    }

    return 'notice'
}

function Get-DiagnosticPath
{
    param([AllowNull()][string]$ScriptPath)

    if ([string]::IsNullOrWhiteSpace($ScriptPath))
    {
        return ''
    }

    $relativePath = [System.IO.Path]::GetRelativePath($repositoryRoot, $ScriptPath)
    if (-not $relativePath.StartsWith('..') -and -not [System.IO.Path]::IsPathRooted($relativePath))
    {
        return $relativePath.Replace('\', '/')
    }

    return $ScriptPath.Replace('\', '/')
}

function Get-DiagnosticRange
{
    param($Diagnostic)

    $line = [Math]::Max(1, [int]$Diagnostic.Line)
    $column = [Math]::Max(1, [int]$Diagnostic.Column)
    $endLine = $line
    $endColumn = $column

    if ($null -ne $Diagnostic.Extent)
    {
        $line = [Math]::Max(1, [int]$Diagnostic.Extent.StartLineNumber)
        $column = [Math]::Max(1, [int]$Diagnostic.Extent.StartColumnNumber)
        $endLine = [Math]::Max($line, [int]$Diagnostic.Extent.EndLineNumber)
        $endColumn = [Math]::Max(
            $column,
            [int]$Diagnostic.Extent.EndColumnNumber - 1
        )
    }

    return @($line, $column, $endLine, $endColumn)
}

foreach ($diagnostic in $diagnostics)
{
    if ($OutputFormat -eq 'GitHub')
    {
        $kind = Get-GitHubAnnotationKind -Severity $diagnostic.Severity
        $path = ConvertTo-GitHubCommandProperty -Value (Get-DiagnosticPath -ScriptPath $diagnostic.ScriptPath)
        $title = ConvertTo-GitHubCommandProperty -Value $diagnostic.RuleName
        $message = ConvertTo-GitHubCommandValue -Value $diagnostic.Message
        $range = @(Get-DiagnosticRange -Diagnostic $diagnostic)

        Write-Output (
            "::$kind file=$path,line=$($range[0]),col=$($range[1])," +
            "endLine=$($range[2]),endColumn=$($range[3]),title=$title::$message"
        )
    }
    else
    {
        [Console]::Error.WriteLine(
            "$($diagnostic.ScriptName):$($diagnostic.Line): " +
            "$($diagnostic.Severity) $($diagnostic.RuleName): $($diagnostic.Message)"
        )
    }
}

exit 1
