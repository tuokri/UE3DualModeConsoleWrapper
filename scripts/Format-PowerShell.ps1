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

$formatterCommand = Get-Command -Name Invoke-Formatter -ErrorAction SilentlyContinue
if ($null -eq $formatterCommand)
{
    throw 'PSScriptAnalyzer is required. Ensure Invoke-Formatter is available in PowerShell.'
}

$scripts = @(Get-ChildItem -Path $PSScriptRoot -Filter '*.ps1' -File -Recurse)
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

foreach ($script in $scripts)
{
    $original = [System.IO.File]::ReadAllText($script.FullName)
    $formatted = Invoke-Formatter -ScriptDefinition $original -Settings CodeFormattingAllman

    if ($original -cne $formatted)
    {
        [System.IO.File]::WriteAllText($script.FullName, $formatted, $utf8NoBom)
        Write-Output "Formatted $($script.FullName)"
    }
}
