[CmdletBinding()]
param([switch]$Install)

$ErrorActionPreference = 'Stop'
Import-Module Pester -RequiredVersion 5.7.1
$root = Split-Path $PSScriptRoot
New-Item (Join-Path $root 'TestResults') -ItemType Directory -Force | Out-Null
$config = New-PesterConfiguration
$config.Run.Path = Join-Path $root 'tests'
$config.Run.PassThru = $true
$config.Output.Verbosity = 'Detailed'
$config.TestResult.Enabled = $true
$config.TestResult.OutputPath = Join-Path $root 'TestResults/results.xml'
$config.TestResult.OutputFormat = 'JUnitXml'
if (-not $Install) { $config.Filter.ExcludeTag = @('Install') }
$result = Invoke-Pester -Configuration $config
if ($result.Result -ne 'Passed' -or $result.PassedCount -eq 0) { throw 'Tests failed or no tests ran.' }
