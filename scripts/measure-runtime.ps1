[CmdletBinding()]
param(
    [ValidateRange(10, 7200)][int]$SecondsPerPhase = 60,
    [string]$OutputPath = (Join-Path (Split-Path $PSScriptRoot) 'artifacts/validation/runtime.json')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$hostProject = Join-Path $root 'tests/MyFinder.Scenarios/MyFinder.Scenarios.csproj'
& dotnet build $hostProject -c Release --nologo -p:RestoreLockedMode=true | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Scenario host build failed.' }
$hostDll = Join-Path $root 'tests/MyFinder.Scenarios/bin/Release/net10.0-windows10.0.19041.0/MyFinder.Scenarios.dll'
$storage = & dotnet $hostDll storage | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Native storage inventory failed.' }
$network = @([System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() | Where-Object OperationalStatus -eq Up | Select-Object -First 8 | ForEach-Object { 'network:' + ([Guid]$_.Id).ToString('D') })
$resources = @($storage.Readings | Where-Object { $_.Resource.Kind -eq 3 } | Select-Object -First 16) + @($storage.Readings | Where-Object { $_.Resource.Kind -eq 4 } | Select-Object -First 16)
$monitored = @('cpu', 'memory') + $network + @($resources | ForEach-Object { $_.Resource.Id })
$directory = Split-Path ([IO.Path]::GetFullPath($OutputPath))
New-Item $directory -ItemType Directory -Force | Out-Null
$profile = Join-Path $directory ('measurement-profile-' + [Guid]::NewGuid().ToString('N'))
New-Item $profile -ItemType Directory | Out-Null
@{ Version=1; Retreat=$false; NetworkSelectionMade=$true; StorageSelectionMade=$true; Monitored=$monitored; MetricOrder=@('cpu','memory'); RetentionMinutes=60 } | ConvertTo-Json -Depth 5 | Set-Content "$profile/preferences.json" -Encoding UTF8

function Find-MeasurementWindow([int]$ProcessId, [string]$Title) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Title)))
    [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
}
function Wait-MeasurementWindow([Diagnostics.Process]$Process, [string]$Title) {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $Process.Refresh(); if ($Process.HasExited) { throw 'Measurement application exited early.' }
        $window = Find-MeasurementWindow $Process.Id $Title
        if ($window) { return $window }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Measurement window unavailable: $Title"
}
function Invoke-MeasurementButton($Window, [string]$Id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $button = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $button) { throw "Measurement control unavailable: $Id" }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
}
function Measure-Phase([Diagnostics.Process]$Process, [string]$Name, [int]$Seconds) {
    $Process.Refresh(); $cpu = $Process.TotalProcessorTime.TotalSeconds
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $points = @()
    while ($timer.Elapsed.TotalSeconds -lt $Seconds) {
        Start-Sleep -Seconds 1
        $Process.Refresh(); if ($Process.HasExited) { throw "Application exited during $Name." }
        $points += [ordered]@{ Elapsed=$timer.Elapsed.TotalSeconds; PrivateBytes=$Process.PrivateMemorySize64; Handles=$Process.HandleCount; CpuSeconds=$Process.TotalProcessorTime.TotalSeconds }
    }
    [ordered]@{
        Phase=$Name; Seconds=$timer.Elapsed.TotalSeconds
        TotalMachineCpuPercent=100 * ($Process.TotalProcessorTime.TotalSeconds - $cpu) / $timer.Elapsed.TotalSeconds / [Environment]::ProcessorCount
        PrivateBytesPeak=($points.PrivateBytes | Measure-Object -Maximum).Maximum
        HandlesFirst=$points[0].Handles; HandlesLast=$points[-1].Handles
        Points=$points
    }
}

$process = Start-Process "$root/artifacts/app/MyFinder.exe" -ArgumentList '--profile', ('"' + $profile + '"'), '--settings' -PassThru
try {
    $settings = Wait-MeasurementWindow $process 'MyFinder - Settings'
    $panel = Wait-MeasurementWindow $process MyFinder
    Start-Sleep -Seconds 5
    $settings.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 300
    $closed = Measure-Phase $process 'Panel visible, details closed' $SecondsPerPhase
    Invoke-MeasurementButton $panel Inspect_cpu
    $details = Wait-MeasurementWindow $process 'MyFinder - Details'
    $open = Measure-Phase $process 'CPU details open' $SecondsPerPhase
    $details.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 300
    Invoke-MeasurementButton $panel OpenSettings
    $settings = Wait-MeasurementWindow $process 'MyFinder - Settings'
    $statusCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SettingsPage_Status')
    $statusPage = $settings.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCondition)
    if (-not $statusPage) {
        Invoke-MeasurementButton $settings TogglePaneButton
        $statusPage = $settings.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCondition)
    }
    if (-not $statusPage) {
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)))
        foreach ($popup in $windows) {
            $statusPage = $popup.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCondition)
            if ($statusPage) { break }
        }
    }
    if (-not $statusPage) { throw 'Measurement Status page unavailable.' }
    $statusPage.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 300
    Invoke-MeasurementButton $settings ExitMyFinder
    if (-not $process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw 'Measurement application did not exit cleanly.' }
    if (Test-Path "$profile/last-error.txt") { throw 'Measurement application recorded an error.' }
    $report = [ordered]@{
        Time=[DateTimeOffset]::Now.ToString('O'); OS=[Environment]::OSVersion.Version.ToString(); LogicalProcessors=[Environment]::ProcessorCount
        MonitoredResourceCount=$monitored.Count; Adapters=$network.Count; Volumes=@($resources | Where-Object { $_.Resource.Kind -eq 3 }).Count; Devices=@($resources | Where-Object { $_.Resource.Kind -eq 4 }).Count
        SecondsPerPhase=$SecondsPerPhase; Phases=@($closed,$open)
        Interpretation='Record lock/occlusion and actual hardware alongside this report. Short runs do not qualify a two-retention-window soak or unoccluded rendering cost.'
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content $OutputPath -Encoding UTF8
    Write-Output ([IO.Path]::GetFullPath($OutputPath))
} finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
    Remove-Item $profile -Recurse -Force
}
