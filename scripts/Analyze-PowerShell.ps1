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
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$analyzerCommand = Get-Command -Name Invoke-ScriptAnalyzer -ErrorAction SilentlyContinue
if ($null -eq $analyzerCommand)
{
    throw 'PSScriptAnalyzer is required. Ensure Invoke-ScriptAnalyzer is available in PowerShell.'
}

$diagnostics = @(Invoke-ScriptAnalyzer -Path $PSScriptRoot -Recurse)
if ($diagnostics.Count -eq 0)
{
    Write-Output 'No PowerShell analyzer findings.'
    exit 0
}

foreach ($diagnostic in $diagnostics)
{
    [Console]::Error.WriteLine(
        "$($diagnostic.ScriptName):$($diagnostic.Line): " +
        "$($diagnostic.Severity) $($diagnostic.RuleName): $($diagnostic.Message)"
    )
}

exit 1
