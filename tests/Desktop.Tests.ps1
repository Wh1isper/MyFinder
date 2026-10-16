BeforeAll {
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
    if (-not ('MyFinderDesktopTest' -as [type])) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MyFinderDesktopTest {
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    public static Rect ClientBounds(IntPtr hwnd) { Rect r; GetClientRect(hwnd, out r); var p = new Point(0, 0); ClientToScreen(hwnd, ref p); return new Rect { Left=p.X, Top=p.Y, Right=p.X+r.Right, Bottom=p.Y+r.Bottom }; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    public static Rect Bounds(IntPtr hwnd) { Rect r; GetWindowRect(hwnd, out r); return r; }
    [DllImport("user32.dll")] public static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    public static string ProcessName(IntPtr hwnd) { uint process; GetWindowThreadProcessId(hwnd, out process); return System.Diagnostics.Process.GetProcessById((int)process).ProcessName; }
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hwnd, System.Text.StringBuilder name, int count);
    public static string Describe(IntPtr hwnd) { var n=new System.Text.StringBuilder(256); GetClassNameW(hwnd,n,256); var r=Bounds(hwnd); return string.Format("{0} visible={1} dpi={2} rect={3},{4},{5},{6}", n,IsWindowVisible(hwnd),GetDpiForWindow(hwnd),r.Left,r.Top,r.Right,r.Bottom); }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; public Point(int x, int y) { X=x; Y=y; } }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
}
'@
    }
    $root = Split-Path $PSScriptRoot
    function Find-AppWindow([int]$ProcessId, [string]$Title) {
        $condition = New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId)),
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Title)))
        [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
    }
    function Wait-AppWindow([Diagnostics.Process]$Process, [string]$Title) {
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        do {
            $Process.Refresh()
            if ($Process.HasExited) { throw "Application exited with $($Process.ExitCode) before '$Title'." }
            $window = Find-AppWindow $Process.Id $Title
            if ($window) {
                $readyId = switch ($Title) { 'MyFinder' { 'MovePanel' }; 'MyFinder - Settings' { 'TogglePanelVisibility' }; 'MyFinder - Details' { 'DetailHeading' } }
                if (-not $readyId -or (Find-AppElement $window $readyId)) { return $window }
            }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        $visible = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $Process.Id))) | ForEach-Object { $_.Current.Name }
        throw "Window did not appear: $Title. Present: $($visible -join ', ')"
    }
    function Invoke-AppButton($Window, [string]$Id) {
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
        $button = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if (-not $button) { throw "Button missing: $Id" }
        $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 300
    }
    function Close-AppWindow($Window) {
        $handle = [IntPtr]$Window.Current.NativeWindowHandle
        $Window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        Start-Sleep -Milliseconds 300
        [MyFinderDesktopTest]::IsWindowVisible($handle) | Should -BeFalse
    }
    function Exit-TestApp($Settings) {
        Select-SettingsPage $Settings Status
        Invoke-AppButton $Settings ExitMyFinder
    }
    function Set-PanelVisibility($Settings, [bool]$Shown) {
        $button = Find-AppElement $Settings TogglePanelVisibility
        $button.Current.Name | Should -Be $(if ($Shown) { 'Show panel' } else { 'Hide panel' })
        Invoke-AppButton $Settings TogglePanelVisibility
        (Find-AppElement $Settings TogglePanelVisibility).Current.Name | Should -Be $(if ($Shown) { 'Hide panel' } else { 'Show panel' })
        (Find-AppElement $Settings MonitoringStatus).Current.Name | Should -Match $(if ($Shown) { '^Panel visible$' } else { '^Panel hidden.*Monitoring continues$' })
    }
    function Select-SettingsPage($Window, [string]$Page) {
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, ('SettingsPage_' + $Page))
        $item = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if (-not $item) {
            Invoke-AppButton $Window TogglePaneButton
            $item = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        }
        if (-not $item) {
            $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $Window.Current.ProcessId)))
            foreach ($popup in $windows) {
                $item = $popup.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
                if ($item) { break }
            }
        }
        if (-not $item) {
            $ids = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.AutomationId } | Where-Object { $_ }
            throw "Settings page missing: $Page at width $($Window.Current.BoundingRectangle.Width). IDs: $($ids -join ', ')"
        }
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 300
    }
    function Find-AppElement($Window, [string]$Id) {
        $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)))
    }
    function Select-AppChoice($Window, [string]$Name, [string]$Value) {
        $combo = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)))))
        if (-not $combo) { throw "Choice missing: $Name" }
        $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Start-Sleep -Milliseconds 100
        $item = $combo.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Value)))
        if (-not $item) { throw "Choice value missing: $Name / $Value" }
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 300
        # Theme changes rebuild native peers; wait for the replacement rather than reusing the old peer.
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        do {
            $windowNow = Find-AppWindow $Window.Current.ProcessId $Window.Current.Name
            $combo = $windowNow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.AndCondition(
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)))))
            if (-not $combo) { Start-Sleep -Milliseconds 100 }
        } while (-not $combo -and [DateTime]::UtcNow -lt $deadline)
        if (-not $combo) {
            $names = $windowNow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
            throw "Choice disappeared after selection: $Name. UI: $($names -join ' | ')"
        }
        $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
        Start-Sleep -Milliseconds 100
    }
    function Resize-TestWindow($Window, [int]$Width, [int]$Height) {
        $handle = [IntPtr]$Window.Current.NativeWindowHandle
        $work = [System.Windows.Forms.Screen]::FromHandle($handle).WorkingArea
        $dpi = [MyFinderDesktopTest]::GetDpiForWindow($handle) / 96.0
        [MyFinderDesktopTest]::SetWindowPos($handle, [IntPtr]::Zero, ($work.X+8), ($work.Y+80), [Math]::Min([int]($Width*$dpi),$work.Width-16), [Math]::Min([int]($Height*$dpi),$work.Height-88), 0x14) | Out-Null
        Start-Sleep -Milliseconds 500
    }
    function Save-OwnedWindow($Window, [string]$Name) {
        $handle = [IntPtr]$Window.Current.NativeWindowHandle
        [MyFinderDesktopTest]::SetWindowPos($handle, [IntPtr](-1), 0, 0, 0, 0, 0x13) | Out-Null
        Start-Sleep -Milliseconds 300
        # Native window bounds include invisible resize borders; capture the application's client area only.
        $r = [MyFinderDesktopTest]::ClientBounds($handle)
        $width = $r.Right-$r.Left; $height = $r.Bottom-$r.Top
        foreach ($p in @(@(12,12), @(($width-13),12), @(12,($height-13)), @(($width-13),($height-13)), @([int]($width/2),[int]($height/2)))) {
            $hit = [MyFinderDesktopTest]::GetAncestor([MyFinderDesktopTest]::WindowFromPoint((New-Object MyFinderDesktopTest+Point(($r.Left+$p[0]),($r.Top+$p[1])))), 2)
            if ([MyFinderDesktopTest]::Describe($hit) -match '^LockScreenBackstopFrame ' -or [MyFinderDesktopTest]::ProcessName($hit) -eq 'LockApp') { return $false }
            $hit.ToInt64() | Should -Be $handle.ToInt64() -Because "only the owned application rectangle may be captured; hit=$([MyFinderDesktopTest]::Describe($hit)); process=$([MyFinderDesktopTest]::ProcessName($hit)); target=$([MyFinderDesktopTest]::Describe($handle)); point=$($p -join ','); foreground=$([MyFinderDesktopTest]::Describe([MyFinderDesktopTest]::GetForegroundWindow()))"
        }
        $image = New-Object System.Drawing.Bitmap($width,$height); $graphics = [System.Drawing.Graphics]::FromImage($image)
        try {
            $graphics.CopyFromScreen($r.Left,$r.Top,0,0,$image.Size)
            New-Item "$root/TestResults" -ItemType Directory -Force | Out-Null
            $image.Save("$root/TestResults/$Name.png")
        } finally { $graphics.Dispose(); $image.Dispose() }
        return $true
    }
    function Wait-AppText($Window, [string]$Pattern) {
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        $handle = [IntPtr]$Window.Current.NativeWindowHandle
        do {
            if (-not [MyFinderDesktopTest]::IsWindowVisible($handle)) { throw "Window dismissed before '$Pattern'. Foreground: $([MyFinderDesktopTest]::Describe([MyFinderDesktopTest]::GetForegroundWindow()))" }
            $fresh = Find-AppWindow $Window.Current.ProcessId $Window.Current.Name
            $texts = $fresh.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
            if (($texts -join "`n") -match $Pattern) { return $texts }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        ($texts -join "`n") | Should -Match $Pattern
    }
    function Send-AppKeys($Window, $Element, [string]$Keys) {
        $Element.SetFocus(); Start-Sleep -Milliseconds 100
        $handle = [IntPtr]$Window.Current.NativeWindowHandle
        if ([MyFinderDesktopTest]::GetForegroundWindow() -ne $handle) { throw 'Keyboard input requires the owned application in the foreground.' }
        [System.Windows.Forms.SendKeys]::SendWait($Keys)
        Start-Sleep -Milliseconds 200
    }
    function Stop-TestApp([Diagnostics.Process]$Process) {
        try {
            if (-not $Process.HasExited) {
                $settings = Find-AppWindow $Process.Id 'MyFinder - Settings'
                if ($settings) { Exit-TestApp $settings }
                $Process.WaitForExit(10000) | Out-Null
            }
        } finally {
            if (-not $Process.HasExited) { $Process.Kill(); $Process.WaitForExit() }
            $Process.Dispose()
        }
    }
}

Describe 'Published monitoring application' {
    It 'navigates Fluent settings and reflows details without changing monitoring or pins' {
        $oldDpi = [MyFinderDesktopTest]::SetThreadDpiAwarenessContext([IntPtr](-4))
        $profile = Join-Path $TestDrive 'modern-ui'; New-Item $profile -ItemType Directory | Out-Null
        @{ Version=1; Retreat=$false; NetworkSelectionMade=$true; StorageSelectionMade=$true; Theme='Light'; MetricOrder=@('cpu','memory'); Monitored=@('cpu','memory') } | ConvertTo-Json | Set-Content "$profile/preferences.json" -Encoding UTF8
        $process = Start-Process "$root/artifacts/app/MyFinder.exe" -ArgumentList '--profile', ('"'+$profile+'"'), '--settings' -PassThru
        try {
            $settings = Wait-AppWindow $process 'MyFinder - Settings'; $panel = Wait-AppWindow $process MyFinder
            $initialChoices = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox))) | ForEach-Object {
                    $_.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection() | ForEach-Object { $_.Current.Name }
                }
            $initialChoices | Should -Contain 'Horizontal'; $initialChoices | Should -Contain 'Balanced'; $initialChoices | Should -Contain 'Light'
            $panelDpi = [MyFinderDesktopTest]::GetDpiForWindow([IntPtr]$panel.Current.NativeWindowHandle) / 96.0
            $panel.Current.BoundingRectangle.Width / $panelDpi | Should -BeLessOrEqual 260
            $panel.Current.BoundingRectangle.Height / $panelDpi | Should -BeLessOrEqual 64
            foreach ($width in @(1050,560)) {
                Resize-TestWindow $settings $width 720
                foreach ($page in @('Appearance','Placement','Resources','History','Status')) {
                    Write-Host "Settings width=$width page=$page"
                    Select-SettingsPage $settings $page
                    if ($page -eq 'Status') { Find-AppElement $settings ExitMyFinder | Should -Not -BeNullOrEmpty }
                    else { Find-AppElement $settings ExitMyFinder | Should -BeNullOrEmpty }
                    (Find-AppElement $settings TogglePanelVisibility).Current.IsOffscreen | Should -BeFalse
                    (Find-AppElement $settings TogglePanelVisibility).Current.Name | Should -Be 'Hide panel'
                    (Find-AppElement $settings MonitoringStatus).Current.Name | Should -Be 'Panel visible'
                }
            }
            Select-SettingsPage $settings Appearance
            Select-AppChoice $settings Appearance Dark
            Invoke-AppButton $panel Inspect_memory
            $details = Wait-AppWindow $process 'MyFinder - Details'
            $detailDpi = [MyFinderDesktopTest]::GetDpiForWindow([IntPtr]$details.Current.NativeWindowHandle) / 96.0
            $details.Current.BoundingRectangle.Width / $detailDpi | Should -BeLessOrEqual 760
            $details.Current.BoundingRectangle.Height / $detailDpi | Should -BeLessOrEqual 640
            foreach ($width in @(1050,520)) {
                Resize-TestWindow $details $width 720
                (Find-AppElement $details DetailSettings).Current.IsOffscreen | Should -BeFalse
                $rangeBefore = (Find-AppElement $details DetailRange).Current.BoundingRectangle
                (Find-AppElement $details DetailMetric).Current.BoundingRectangle.Top | Should -Be $rangeBefore.Top
                if ($width -eq 520) {
                    (Find-AppElement $details DetailResource).Current.BoundingRectangle.Bottom | Should -BeLessThan $rangeBefore.Top
                }
                $readings = Find-AppElement $details DetailReadings
                $readings.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1,100)
                Start-Sleep -Milliseconds 200
                (Find-AppElement $details DetailRange).Current.BoundingRectangle.Top | Should -Be $rangeBefore.Top
                (Find-AppElement $details DetailRange).Current.IsOffscreen | Should -BeFalse
                (Find-AppElement $details DetailQuality).Current.IsOffscreen | Should -BeFalse
                $readings.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1,0)
                (Find-AppElement $details DetailHeading).Current.BoundingRectangle.Right | Should -BeLessOrEqual $details.Current.BoundingRectangle.Right
                $texts = $details.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
                ($texts -join "`n") | Should -Match 'Physical memory'
                ($texts -join "`n") | Should -Match 'Current observations'
                ($texts -join "`n") | Should -Match 'Resource facts'
                ($texts -join "`n") | Should -Match 'Recent history'
                if ($width -eq 1050) {
                    $rail = Find-AppElement $details InspectRail_cpu
                    $rail | Should -Not -BeNullOrEmpty
                    $rail.Current.IsOffscreen | Should -BeFalse
                    if ($rail) {
                        $rail.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300
                        (Find-AppElement $details DetailHeading).Current.Name | Should -Be 'CPU'
                        $memory = Find-AppElement $details InspectRail_memory
                        $memory.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300
                    }
                }
            }
            Invoke-AppButton $details DetailSettings
            $settings = Wait-AppWindow $process 'MyFinder - Settings'
            [MyFinderDesktopTest]::IsWindowVisible([IntPtr]$details.Current.NativeWindowHandle) | Should -BeFalse
            Exit-TestApp $settings; $process.WaitForExit(10000) | Should -BeTrue
            $saved = Get-Content "$profile/preferences.json" -Raw | ConvertFrom-Json
            $saved.Theme | Should -Be 'Dark'; @($saved.Monitored).Count | Should -Be 2
            $saved.Monitored | Should -Contain 'cpu'; $saved.Monitored | Should -Contain 'memory'
            @($saved.Pins.PSObject.Properties).Count | Should -Be 0
            Test-Path "$profile/last-error.txt" | Should -BeFalse
        } finally { Stop-TestApp $process; [MyFinderDesktopTest]::SetThreadDpiAwarenessContext($oldDpi) | Out-Null }
    }
    It 'separates compact inclusion and ordering and explains inspection while hidden' {
        $profile = Join-Path $TestDrive 'settings-actions'; New-Item $profile -ItemType Directory | Out-Null
        @{ Version=1; Retreat=$false; NetworkSelectionMade=$true; StorageSelectionMade=$true; MetricOrder=@('cpu','memory'); Monitored=@('cpu','memory') } | ConvertTo-Json | Set-Content "$profile/preferences.json" -Encoding UTF8
        $process = Start-Process "$root/artifacts/app/MyFinder.exe" -ArgumentList '--profile', ('"'+$profile+'"'), '--settings' -PassThru
        try {
            $settings = Wait-AppWindow $process 'MyFinder - Settings'
            (Find-AppElement $settings MoveMetricFirst_cpu).Current.IsEnabled | Should -BeFalse
            (Find-AppElement $settings MoveMetricFirst_network).Current.IsEnabled | Should -BeFalse
            Invoke-AppButton $settings MoveMetricFirst_memory
            (Find-AppElement $settings CompactMetricOrder).Current.Name | Should -Match 'Memory.*CPU$'
            (Find-AppElement $settings ShowMetric_network).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString() | Should -Be 'Off'
            (Find-AppElement $settings ShowMetric_memory).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
            Start-Sleep -Milliseconds 300
            (Find-AppElement $settings MoveMetricFirst_memory).Current.IsEnabled | Should -BeFalse
            (Find-AppElement $settings CompactMetricOrder).Current.Name | Should -Be 'Panel order: CPU'
            Select-SettingsPage $settings Resources
            (Find-AppElement $settings InspectResource_cpu).Current.IsEnabled | Should -BeTrue
            (Find-AppElement $settings InspectResource_memory).Current.IsEnabled | Should -BeTrue
            Set-PanelVisibility $settings $false
            (Find-AppElement $settings InspectResource_cpu).Current.IsEnabled | Should -BeFalse
            (Find-AppElement $settings InspectResource_memory).Current.IsEnabled | Should -BeFalse
            (Wait-AppText $settings 'Show the panel to inspect resources') | Out-Null
            Set-PanelVisibility $settings $true
            Invoke-AppButton $settings InspectResource_memory
            $details = Wait-AppWindow $process 'MyFinder - Details'
            Close-AppWindow $details
            $process.HasExited | Should -BeFalse
            $panel = Wait-AppWindow $process MyFinder; Invoke-AppButton $panel OpenSettings
            $settings = Wait-AppWindow $process 'MyFinder - Settings'; Exit-TestApp $settings
            $process.WaitForExit(10000) | Should -BeTrue
            $saved = Get-Content "$profile/preferences.json" -Raw | ConvertFrom-Json
            @($saved.MetricOrder).Count | Should -Be 1; $saved.MetricOrder[0] | Should -Be 'cpu'
            @($saved.Monitored).Count | Should -Be 2; $saved.Monitored | Should -Contain 'memory'
            @($saved.Pins.PSObject.Properties).Count | Should -Be 0
        } finally { Stop-TestApp $process }
    }
    It 'holds chart observations only on explicit inspection and returns to live' {
        $profile = Join-Path $TestDrive 'chart-actions'; New-Item $profile -ItemType Directory | Out-Null
        @{ Version=1; Retreat=$false; NetworkSelectionMade=$true; StorageSelectionMade=$true; Monitored=@('cpu','memory') } | ConvertTo-Json | Set-Content "$profile/preferences.json" -Encoding UTF8
        $process = Start-Process "$root/artifacts/app/MyFinder.exe" -ArgumentList '--profile', ('"'+$profile+'"'), '--settings' -PassThru
        try {
            $settings = Wait-AppWindow $process 'MyFinder - Settings'; Close-AppWindow $settings
            $panel = Wait-AppWindow $process MyFinder; Start-Sleep -Seconds 3
            Invoke-AppButton $panel Inspect_memory; $details = Wait-AppWindow $process 'MyFinder - Details'
            $chart = Find-AppElement $details HistoryChart
            (Find-AppElement $details HistoryInspection).Current.Name | Should -Match '^Live'
            Find-AppElement $details BackToLive | Should -BeNullOrEmpty
            Send-AppKeys $details $chart '{LEFT}'
            (Find-AppElement $details HistoryInspection).Current.Name | Should -Match 'Historical inspection.*\nIn use:.*GiB.*Up to date'
            $held = (Find-AppElement $details HistoryInspection).Current.Name
            Start-Sleep -Seconds 2
            (Find-AppElement $details HistoryInspection).Current.Name | Should -Be $held
            Invoke-AppButton $details BackToLive
            (Find-AppElement $details HistoryInspection).Current.Name | Should -Match '^Live'
            Send-AppKeys $details $chart '{HOME}'; Send-AppKeys $details $chart '{END}'
            (Find-AppElement $details HistoryInspection).Current.Name | Should -Match '^Live'
            Send-AppKeys $details $chart '{LEFT}'
            Select-AppChoice $details 'History range in minutes' '1'
            (Find-AppElement $details HistoryInspection).Current.Name | Should -Match '^Live'
            Send-AppKeys $details $chart '{ESC}'
            [MyFinderDesktopTest]::IsWindowVisible([IntPtr]$details.Current.NativeWindowHandle) | Should -BeFalse
            Invoke-AppButton $panel OpenSettings; $settings = Wait-AppWindow $process 'MyFinder - Settings'
            Exit-TestApp $settings; $process.WaitForExit(10000) | Should -BeTrue
        } finally { Stop-TestApp $process }
    }
    It 'limits address reveal to its resource and lifetime without changing monitoring' {
        $adapter = [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() | Where-Object { $_.OperationalStatus -eq 'Up' -and $_.Id -match '^\{?[0-9a-fA-F-]{36}\}?$' } | Select-Object -First 1
        if (-not $adapter) { Set-ItResult -Skipped -Because 'A native active adapter is required.'; return }
        $id = 'network:' + ([Guid]$adapter.Id).ToString('D')
        $profile = Join-Path $TestDrive 'privacy-actions'; New-Item $profile -ItemType Directory | Out-Null
        @{ Version=1; Retreat=$false; NetworkSelectionMade=$true; StorageSelectionMade=$true; MetricOrder=@('cpu','network'); Monitored=@('cpu','memory',$id); Pins=@{network=$id} } | ConvertTo-Json -Depth 5 | Set-Content "$profile/preferences.json" -Encoding UTF8
        $process = Start-Process "$root/artifacts/app/MyFinder.exe" -ArgumentList '--profile', ('"'+$profile+'"'), '--settings' -PassThru
        try {
            $settings = Wait-AppWindow $process 'MyFinder - Settings'; Close-AppWindow $settings
            $panel = Wait-AppWindow $process MyFinder; Start-Sleep -Seconds 2
            Invoke-AppButton $panel Inspect_network; $details = Wait-AppWindow $process 'MyFinder - Details'
            (Find-AppElement $details AddressPrivacyStatus).Current.Name | Should -Be 'Addresses are hidden.'
            Invoke-AppButton $details RevealAddresses
            (Wait-AppText $details 'Visible for 30 seconds') | Out-Null
            (Find-AppElement $details RevealAddresses).Current.Name | Should -Be 'Hide addresses'
            (Find-AppElement $details RevealedAddresses).Current.Name.Length | Should -BeGreaterThan 0
            Invoke-AppButton $details RevealAddresses
            (Find-AppElement $details RevealedAddresses).Current.Name | Should -BeNullOrEmpty
            Invoke-AppButton $details RevealAddresses
            (Wait-AppText $details 'Visible for 30 seconds') | Out-Null
            Start-Sleep -Seconds 31
            (Find-AppElement $details AddressPrivacyStatus).Current.Name | Should -Be 'Addresses are hidden.'
            (Find-AppElement $details RevealedAddresses).Current.Name | Should -BeNullOrEmpty
            Invoke-AppButton $details RevealAddresses
            Select-AppChoice $details 'Inspect resource without changing monitoring' 'CPU'
            Close-AppWindow $details
            Invoke-AppButton $panel Inspect_network; $details = Wait-AppWindow $process 'MyFinder - Details'
            (Find-AppElement $details AddressPrivacyStatus).Current.Name | Should -Be 'Addresses are hidden.'
            (Find-AppElement $details RevealedAddresses).Current.Name | Should -BeNullOrEmpty
            Invoke-AppButton $details DetailSettings; $settings = Wait-AppWindow $process 'MyFinder - Settings'; Exit-TestApp $settings
            $process.WaitForExit(10000) | Should -BeTrue
            $saved = Get-Content "$profile/preferences.json" -Raw | ConvertFrom-Json
            $saved.Pins.network | Should -Be $id; $saved.Monitored | Should -Contain $id
        } finally { Stop-TestApp $process }
    }
    It 'captures owned light and dark UI surfaces for visual review' {
        $oldDpi = [MyFinderDesktopTest]::SetThreadDpiAwarenessContext([IntPtr](-4))
        $profile = Join-Path $TestDrive 'visual-ui'; New-Item $profile -ItemType Directory | Out-Null
        @{ Version=1; Retreat=$false; NetworkSelectionMade=$true; StorageSelectionMade=$true; Theme='Light'; MetricOrder=@('cpu','memory'); Monitored=@('cpu','memory') } | ConvertTo-Json | Set-Content "$profile/preferences.json" -Encoding UTF8
        $process = Start-Process "$root/artifacts/app/MyFinder.exe" -ArgumentList '--profile', ('"'+$profile+'"'), '--settings' -PassThru
        try {
            $settings = Wait-AppWindow $process 'MyFinder - Settings'; $panel = Wait-AppWindow $process MyFinder
            foreach ($theme in @('Light','Dark')) {
                Select-SettingsPage $settings Appearance; Select-AppChoice $settings Appearance $theme
                Resize-TestWindow $settings 1050 720
                if (-not (Save-OwnedWindow $settings "ui-settings-$theme")) { Set-ItResult -Skipped -Because 'LockApp occludes the owned UI; screenshots require an unlocked designated session.'; return }
                (Find-AppElement $settings SettingsBody).GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1,100)
                Start-Sleep -Milliseconds 200
                if (-not (Save-OwnedWindow $settings "ui-settings-metrics-$theme")) { Set-ItResult -Skipped -Because 'LockApp occludes the owned UI.'; return }
                foreach ($page in @('Placement','Resources','History','Status')) {
                    Select-SettingsPage $settings $page
                    if (-not (Save-OwnedWindow $settings "ui-settings-$page-$theme")) { Set-ItResult -Skipped -Because 'LockApp occludes the owned UI.'; return }
                }
                Select-SettingsPage $settings Appearance; Resize-TestWindow $settings 560 720
                if (-not (Save-OwnedWindow $settings "ui-settings-narrow-$theme")) { Set-ItResult -Skipped -Because 'LockApp occludes the owned UI.'; return }
                Close-AppWindow $settings
                if (-not (Save-OwnedWindow $panel "ui-panel-$theme")) { Set-ItResult -Skipped -Because 'LockApp occludes the owned panel.'; return }
                Invoke-AppButton $panel Inspect_memory; $details = Wait-AppWindow $process 'MyFinder - Details'; Start-Sleep -Seconds 2
                if (-not (Save-OwnedWindow $details "ui-details-default-$theme")) { Set-ItResult -Skipped -Because 'LockApp occludes the owned details.'; return }
                Resize-TestWindow $details 1050 720
                if (-not (Save-OwnedWindow $details "ui-details-$theme")) { Set-ItResult -Skipped -Because 'LockApp occludes the owned details.'; return }
                Resize-TestWindow $details 520 720
                if (-not (Save-OwnedWindow $details "ui-details-narrow-$theme")) { Set-ItResult -Skipped -Because 'LockApp occludes the owned details.'; return }
                Invoke-AppButton $details DetailSettings; $settings = Wait-AppWindow $process 'MyFinder - Settings'
            }
            Exit-TestApp $settings; $process.WaitForExit(10000) | Should -BeTrue
        } finally { Stop-TestApp $process; [MyFinderDesktopTest]::SetThreadDpiAwarenessContext($oldDpi) | Out-Null }
    }
    It 'captures four-metric panels across styles orientations and themes' {
        $oldDpi = [MyFinderDesktopTest]::SetThreadDpiAwarenessContext([IntPtr](-4))
        $profile = Join-Path $TestDrive 'four-metric-ui'; New-Item $profile -ItemType Directory | Out-Null
        $adapter = [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() | Where-Object { $_.OperationalStatus -eq 'Up' -and $_.Id -match '^\{?[0-9a-fA-F-]{36}\}?$' } | Select-Object -First 1
        $volume = Get-CimInstance Win32_Volume | Where-Object { $_.DriveLetter -eq $env:SystemDrive } | Select-Object -First 1
        $pins = @{}; $monitored = @('cpu','memory')
        if ($adapter) { $pins.network = 'network:' + ([Guid]$adapter.Id).ToString('D'); $monitored += $pins.network }
        if ($volume) { $pins.storage = 'volume:' + $volume.DeviceID.ToLowerInvariant(); $monitored += $pins.storage }
        @{ Version=1; Retreat=$false; NetworkSelectionMade=$true; StorageSelectionMade=$true; MetricOrder=@('cpu','memory','network','storage'); Monitored=$monitored; Pins=$pins } | ConvertTo-Json -Depth 5 | Set-Content "$profile/preferences.json" -Encoding UTF8
        $process = Start-Process "$root/artifacts/app/MyFinder.exe" -ArgumentList '--profile', ('"'+$profile+'"'), '--settings' -PassThru
        try {
            $settings = Wait-AppWindow $process 'MyFinder - Settings'; $panel = Wait-AppWindow $process MyFinder
            Start-Sleep -Seconds 3
            foreach ($theme in @('Light','Dark')) {
                foreach ($style in @('Balanced','Minimal','Capsules')) {
                    foreach ($orientation in @('Horizontal','Vertical')) {
                        Select-SettingsPage $settings Appearance
                        Select-AppChoice $settings Appearance $theme; Select-AppChoice $settings Style $style; Select-AppChoice $settings Orientation $orientation
                        Close-AppWindow $settings
                        if (-not (Save-OwnedWindow $panel "ui-four-$theme-$style-$orientation")) { Set-ItResult -Skipped -Because 'LockApp occludes the owned panel.'; return }
                        $bounds = $panel.Current.BoundingRectangle
                        foreach ($metric in @('cpu','memory','network','storage')) {
                            $tile = Find-AppElement $panel ('Inspect_' + $metric)
                            $tile.Current.IsOffscreen | Should -BeFalse
                            $tile.Current.BoundingRectangle.Right | Should -BeLessOrEqual $bounds.Right
                            $tile.Current.BoundingRectangle.Bottom | Should -BeLessOrEqual $bounds.Bottom
                        }
                        Invoke-AppButton $panel OpenSettings; $settings = Wait-AppWindow $process 'MyFinder - Settings'
                    }
                }
            }
            Exit-TestApp $settings; $process.WaitForExit(10000) | Should -BeTrue
        } finally { Stop-TestApp $process; [MyFinderDesktopTest]::SetThreadDpiAwarenessContext($oldDpi) | Out-Null }
    }
    It 'keeps a fixed storage tile while inspecting, monitoring and comparing separate resources' {
        $volumes = @(Get-CimInstance Win32_Volume | Where-Object { $_.FileSystem -eq 'NTFS' } | Select-Object -First 3)
        if ($volumes.Count -lt 2) { Set-ItResult -Skipped -Because 'Two native NTFS volumes are required for independent storage UI inspection.'; return }
        $ids = @($volumes | ForEach-Object { 'volume:' + $_.DeviceID.ToLowerInvariant() })
        $profile = Join-Path $TestDrive 'storage-ui'; New-Item $profile -ItemType Directory | Out-Null
        @{ Version=1; Retreat=$false; NetworkSelectionMade=$true; StorageSelectionMade=$true; MetricOrder=@('cpu','storage'); Monitored=@('cpu','memory')+$ids; Pins=@{storage=$ids[0]} } | ConvertTo-Json -Depth 5 | Set-Content "$profile/preferences.json" -Encoding UTF8
        $process = Start-Process "$root/artifacts/app/MyFinder.exe" -ArgumentList '--profile', ('"' + $profile + '"'), '--settings' -PassThru
        try {
            $settings = Wait-AppWindow $process 'MyFinder - Settings'; $panel = Wait-AppWindow $process MyFinder
            Start-Sleep -Seconds 3
            $bounds = $panel.Current.BoundingRectangle
            Invoke-AppButton $panel Inspect_storage
            $details = Wait-AppWindow $process 'MyFinder - Details'
            $texts = Wait-AppText $details 'Volume capacity'
            ($texts -join "`n") | Should -Match 'Volume capacity'
            ($texts -join "`n") | Should -Match 'Caller available'
            Invoke-AppButton $details DetailSettings
            $settings = Wait-AppWindow $process 'MyFinder - Settings'; Select-SettingsPage $settings Resources; Invoke-AppButton $settings RefreshResources
            Invoke-AppButton $settings ('InspectResource_' + $ids[1])
            $details = Wait-AppWindow $process 'MyFinder - Details'
            $texts = $details.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
            ($texts -join "`n") | Should -Match ([regex]::Escape($ids[1]))
            Select-AppChoice $details 'Inspect resource without changing monitoring' 'Storage overview'
            # Invoke queues UI work; observe the resulting page without invoking again or reopening it.
            $texts = Wait-AppText $details 'All monitored volumes and devices'
            ($texts -join "`n") | Should -Match 'All monitored volumes and devices'
            Invoke-AppButton $details DetailSettings
            $settings = Wait-AppWindow $process 'MyFinder - Settings'
            Select-SettingsPage $settings Resources
            $toggles = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox)))
            $compare = @($toggles | Where-Object { $_.Current.Name -like 'Compare Disk *' } | Select-Object -First 2)
            $compare.Count | Should -BeGreaterThan 0
            foreach ($toggle in $compare) { $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
            foreach ($toggle in @($toggles | Where-Object { $_.Current.Name -like 'Monitor Disk *' } | Select-Object -First 2)) { $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
            Invoke-AppButton $settings OpenStorageComparison
            $details = Wait-AppWindow $process 'MyFinder - Details'; Start-Sleep -Seconds 3
            $texts = $details.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
            ($texts -join "`n") | Should -Match 'Explicit comparison subset'
            ($texts -join "`n") | Should -Match 'Read.*Write'
            $panel.Current.BoundingRectangle.Width | Should -Be $bounds.Width
            $panel.Current.BoundingRectangle.Height | Should -Be $bounds.Height
            Invoke-AppButton $details DetailSettings
            $settings = Wait-AppWindow $process 'MyFinder - Settings'; Exit-TestApp $settings
            $process.WaitForExit(10000) | Should -BeTrue
            $saved = Get-Content "$profile/preferences.json" -Raw | ConvertFrom-Json
            $saved.Pins.storage | Should -Be $ids[0]
            @($saved.StorageComparison).Count | Should -Be $compare.Count
            foreach ($id in $ids) { $saved.Monitored | Should -Contain $id }
            Test-Path "$profile/last-error.txt" | Should -BeFalse
        } finally { Stop-TestApp $process }
    }
    It 'shows live network trends and keeps the compact pin while inspecting another adapter' {
        $adapters = @([System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() | Where-Object { $_.OperationalStatus -eq 'Up' -and $_.Id -match '^\{?[0-9a-fA-F-]{36}\}?$' } | Select-Object -First 2)
        if ($adapters.Count -lt 2) { Set-ItResult -Skipped -Because 'Two active native adapters are required for real independent inspection.'; return }
        $ids = @($adapters | ForEach-Object { 'network:' + ([Guid]$_.Id).ToString('D') })
        $profile = Join-Path $TestDrive 'network-ui'; New-Item $profile -ItemType Directory | Out-Null
        @{ Version=1; Retreat=$false; NetworkSelectionMade=$true; MetricOrder=@('cpu','network'); Monitored=@('cpu','memory')+$ids; Pins=@{network=$ids[0]} } | ConvertTo-Json -Depth 5 | Set-Content "$profile/preferences.json" -Encoding UTF8
        $process = Start-Process "$root/artifacts/app/MyFinder.exe" -ArgumentList '--profile', ('"' + $profile + '"'), '--settings' -PassThru
        try {
            $settings = Wait-AppWindow $process 'MyFinder - Settings'
            $panel = Wait-AppWindow $process MyFinder
            Start-Sleep -Seconds 2
            $bounds = $panel.Current.BoundingRectangle
            Invoke-AppButton $panel Inspect_network
            $details = Wait-AppWindow $process 'MyFinder - Details'
            $texts = $details.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
            ($texts -join "`n") | Should -Match 'Receive.*Send'
            ($texts -join "`n") | Should -Match 'Receive coverage'
            ($texts -join "`n") | Should -Not -Match 'Current unicast addresses:|Physical address:'
            Invoke-AppButton $details DetailSettings
            $settings = Wait-AppWindow $process 'MyFinder - Settings'
            Select-SettingsPage $settings Resources
            Invoke-AppButton $settings RefreshResources
            Invoke-AppButton $settings ('InspectResource_' + $ids[1])
            $details = Wait-AppWindow $process 'MyFinder - Details'
            $texts = $details.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
            ($texts -join "`n") | Should -Match ([regex]::Escape($ids[1]))
            $panel.Current.BoundingRectangle.Width | Should -Be $bounds.Width
            $panel.Current.BoundingRectangle.Height | Should -Be $bounds.Height
            Invoke-AppButton $details DetailSettings
            $settings = Wait-AppWindow $process 'MyFinder - Settings'
            Set-PanelVisibility $settings $false
            Set-PanelVisibility $settings $true
            Exit-TestApp $settings
            $process.WaitForExit(10000) | Should -BeTrue
            $saved = Get-Content "$profile/preferences.json" -Raw | ConvertFrom-Json
            $saved.Pins.network | Should -Be $ids[0]
            $saved.Monitored | Should -Contain $ids[1]
            Test-Path "$profile/last-error.txt" | Should -BeFalse
        } finally { Stop-TestApp $process }
    }
    It 'uses real controls for hide/show, detail dismissal, continued collection and explicit exit' {
        $profile = Join-Path $TestDrive 'desktop'
        New-Item $profile -ItemType Directory | Out-Null
        '{"Version":1,"Retreat":false}' | Set-Content "$profile/preferences.json" -Encoding UTF8
        $process = Start-Process "$root/artifacts/app/MyFinder.exe" -ArgumentList '--profile', ('"' + $profile + '"'), '--settings' -PassThru
        try {
            $settings = Wait-AppWindow $process 'MyFinder - Settings'
            $panel = Wait-AppWindow $process MyFinder
            $panelHandle = [IntPtr]$panel.Current.NativeWindowHandle
            Select-SettingsPage $settings Status
            $statusCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MonitoringDiagnostics')
            $status = $settings.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCondition)
            $before = [int]([regex]::Match($status.Current.Name, '([\d,]+) retained observations').Groups[1].Value.Replace(',',''))
            Set-PanelVisibility $settings $false
            [MyFinderDesktopTest]::IsWindowVisible($panelHandle) | Should -BeFalse
            $process.HasExited | Should -BeFalse
            Start-Sleep -Seconds 2
            $after = [int]([regex]::Match($status.Current.Name, '([\d,]+) retained observations').Groups[1].Value.Replace(',',''))
            $after | Should -BeGreaterThan $before
            Set-PanelVisibility $settings $true
            $panel = Wait-AppWindow $process MyFinder
            [MyFinderDesktopTest]::IsWindowVisible($panelHandle) | Should -BeTrue
            Invoke-AppButton $panel Inspect_cpu
            $details = Wait-AppWindow $process 'MyFinder - Details'
            Start-Sleep -Seconds 2
            $texts = $details.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
            ($texts -join "`n") | Should -Match 'Current snapshot'
            Close-AppWindow $details
            $process.Refresh(); $process.HasExited | Should -BeFalse
            Invoke-AppButton $panel OpenSettings
            $settings = Wait-AppWindow $process 'MyFinder - Settings'
            Exit-TestApp $settings
            $process.WaitForExit(10000) | Should -BeTrue
            $process.ExitCode | Should -Be 0
            Test-Path "$profile/preferences.json" | Should -BeTrue
            Test-Path "$profile/last-error.txt" | Should -BeFalse
        }
        finally { Stop-TestApp $process }
    }
    It 'fits real controls in all styles and orientations with passive panel activation' {
        foreach ($style in @('Balanced', 'Minimal', 'Capsules')) {
            foreach ($orientation in @('Horizontal', 'Vertical')) {
                $profile = Join-Path $TestDrive "$style-$orientation"
                New-Item $profile -ItemType Directory | Out-Null
                @{ Version=1; Style=$style; Orientation=$orientation; Theme='Dark'; Scale=1.4; Retreat=$false; NetworkSelectionMade=$true; StorageSelectionMade=$true } | ConvertTo-Json | Set-Content "$profile/preferences.json" -Encoding UTF8
                $process = Start-Process "$root/artifacts/app/MyFinder.exe" -ArgumentList '--profile', ('"' + $profile + '"'), '--settings' -PassThru
                try {
                    $settings = Wait-AppWindow $process 'MyFinder - Settings'
                    $panel = Wait-AppWindow $process MyFinder
                    Start-Sleep -Milliseconds 800
                    $bounds = $panel.Current.BoundingRectangle
                    $buttons = $panel.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
                    $buttons.Count | Should -Be 4
                    foreach ($button in $buttons) {
                        $r = $button.Current.BoundingRectangle
                        $r.Left | Should -BeGreaterOrEqual ($bounds.Left - 1)
                        $r.Top | Should -BeGreaterOrEqual ($bounds.Top - 1)
                        $r.Right | Should -BeLessOrEqual ($bounds.Right + 1)
                        $r.Bottom | Should -BeLessOrEqual ($bounds.Bottom + 1)
                    }
                    ([MyFinderDesktopTest]::GetWindowLongPtrW([IntPtr]$panel.Current.NativeWindowHandle, -20).ToInt64() -band 0x08000000) | Should -Not -Be 0
                    Exit-TestApp $settings
                    $process.WaitForExit(10000) | Should -BeTrue
                } finally { Stop-TestApp $process }
            }
        }
    }
    It 'composes a translucent background while retaining opaque foreground pixels' {
        $oldDpi = [MyFinderDesktopTest]::SetThreadDpiAwarenessContext([IntPtr](-4))
        $profile = Join-Path $TestDrive 'alpha'
        New-Item $profile -ItemType Directory | Out-Null
        # Keep discovery from changing the panel's controls or opening Settings between the two captures.
        '{"Version":1,"Retreat":false,"Theme":"Dark","BackgroundOpacity":0.5,"MetricOrder":[],"NetworkSelectionMade":true,"StorageSelectionMade":true}' | Set-Content "$profile/preferences.json" -Encoding UTF8
        $process = Start-Process "$root/artifacts/app/MyFinder.exe" -ArgumentList '--profile', ('"' + $profile + '"'), '--settings' -PassThru
        $underlay = New-Object System.Windows.Forms.Form
        $images = @()
        try {
            $settings = Wait-AppWindow $process 'MyFinder - Settings'
            $panel = Wait-AppWindow $process MyFinder
            Start-Sleep -Milliseconds 500
            Select-SettingsPage $settings Status
            $appearance = (Find-AppElement $settings MonitoringDiagnostics).Current.Name
            $appearance | Should -Match 'Background opacity:|Solid background:'
            Close-AppWindow $settings
            $nativeBounds = [MyFinderDesktopTest]::Bounds([IntPtr]$panel.Current.NativeWindowHandle)
            $bounds = New-Object System.Drawing.Rectangle($nativeBounds.Left, $nativeBounds.Top, ($nativeBounds.Right-$nativeBounds.Left), ($nativeBounds.Bottom-$nativeBounds.Top))
            $underlay.FormBorderStyle = 'None'; $underlay.ShowInTaskbar = $false; $underlay.TopMost = $true
            $underlay.StartPosition = 'Manual'
            $underlay.Bounds = New-Object System.Drawing.Rectangle([int]$bounds.X, [int]$bounds.Y, [int]($bounds.Width+80), [int]($bounds.Height+80))
            $underlay.Show()
            foreach ($color in @([System.Drawing.Color]::Black, [System.Drawing.Color]::White)) {
                $underlay.BackColor = $color; $underlay.Refresh(); [System.Windows.Forms.Application]::DoEvents()
                [MyFinderDesktopTest]::SetWindowPos($underlay.Handle, [IntPtr](-1), 0, 0, 0, 0, 0x13) | Out-Null
                [MyFinderDesktopTest]::SetWindowPos([IntPtr]$panel.Current.NativeWindowHandle, [IntPtr](-1), 0, 0, 0, 0, 0x13) | Out-Null
                Start-Sleep -Milliseconds 500
                $point = New-Object MyFinderDesktopTest+Point([int]($bounds.X+$bounds.Width/2), [int]($bounds.Y+3))
                $hit = [MyFinderDesktopTest]::GetAncestor([MyFinderDesktopTest]::WindowFromPoint($point), 2)
                if ([MyFinderDesktopTest]::Describe($hit) -match '^LockScreenBackstopFrame ' -or [MyFinderDesktopTest]::ProcessName($hit) -eq 'LockApp') {
                    Set-ItResult -Skipped -Because 'Windows lock screen occludes the desktop; pixel composition requires an unlocked designated test session.'
                    return
                }
                $hit.ToInt64() | Should -Be $panel.Current.NativeWindowHandle -Because "panel=$([MyFinderDesktopTest]::Describe([IntPtr]$panel.Current.NativeWindowHandle)); underlay=$([MyFinderDesktopTest]::Describe($underlay.Handle)); hit=$([MyFinderDesktopTest]::Describe($hit)); context=$([MyFinderDesktopTest]::GetThreadDpiAwarenessContext()); old=$oldDpi"
                # Probe outside the DWM shadow as well as outside the panel's hit rectangle.
                $underlayPoint = New-Object MyFinderDesktopTest+Point([int]($bounds.Right+48), [int]($bounds.Y+4))
                [MyFinderDesktopTest]::GetAncestor([MyFinderDesktopTest]::WindowFromPoint($underlayPoint), 2).ToInt64() | Should -Be $underlay.Handle.ToInt64()
                $probe = New-Object System.Drawing.Bitmap(1,1)
                $probeGraphics = [System.Drawing.Graphics]::FromImage($probe)
                try {
                    $probeGraphics.CopyFromScreen($underlayPoint.X, $underlayPoint.Y, 0, 0, $probe.Size)
                    $probe.GetPixel(0,0).ToArgb() | Should -Be $color.ToArgb() -Because 'the owned underlay must really display the requested color'
                } finally { $probeGraphics.Dispose(); $probe.Dispose() }
                $currentBounds = [MyFinderDesktopTest]::Bounds([IntPtr]$panel.Current.NativeWindowHandle)
                $currentBounds.Left | Should -Be $nativeBounds.Left
                $currentBounds.Top | Should -Be $nativeBounds.Top
                $currentBounds.Right | Should -Be $nativeBounds.Right
                $currentBounds.Bottom | Should -Be $nativeBounds.Bottom
                foreach ($offset in @(@(8,8), @(($bounds.Width-9),8), @(8,($bounds.Height-9)), @(($bounds.Width-9),($bounds.Height-9)))) {
                    $corner = New-Object MyFinderDesktopTest+Point([int]($bounds.X+$offset[0]), [int]($bounds.Y+$offset[1]))
                    [MyFinderDesktopTest]::GetAncestor([MyFinderDesktopTest]::WindowFromPoint($corner), 2).ToInt64() | Should -Be $panel.Current.NativeWindowHandle
                }
                $image = New-Object System.Drawing.Bitmap([int]$bounds.Width, [int]$bounds.Height)
                $graphics = [System.Drawing.Graphics]::FromImage($image)
                try { $graphics.CopyFromScreen([int]$bounds.X, [int]$bounds.Y, 0, 0, $image.Size) } finally { $graphics.Dispose() }
                $images += $image
            }
            New-Item "$root/artifacts/validation/A" -ItemType Directory -Force | Out-Null
            $images[0].Save("$root/artifacts/validation/A/alpha-black.png")
            $images[1].Save("$root/artifacts/validation/A/alpha-white.png")
            New-Item "$root/TestResults" -ItemType Directory -Force | Out-Null
            $images[0].Save("$root/TestResults/alpha-black.png")
            $images[1].Save("$root/TestResults/alpha-white.png")
            $backgroundDifference = [Math]::Abs($images[1].GetPixel([int]($bounds.Width/2), 3).R - $images[0].GetPixel([int]($bounds.Width/2), 3).R)
            $opaquePixels = 0
            for ($y=6; $y -lt $images[0].Height-6; $y++) {
                for ($x=6; $x -lt $images[0].Width-6; $x++) {
                    $a=$images[0].GetPixel($x,$y); $b=$images[1].GetPixel($x,$y)
                    if ($a.R -gt 180 -and $a.G -gt 180 -and $a.B -gt 180 -and [Math]::Abs($a.R-$b.R) -lt 5) { $opaquePixels++ }
                }
            }
            if ($appearance -match 'Solid background:') {
                $backgroundDifference | Should -BeLessThan 8
            } else { $backgroundDifference | Should -BeGreaterThan 60 }
            $opaquePixels | Should -BeGreaterThan 20
            $underlay.Close()
            Invoke-AppButton $panel OpenSettings
            $settings = Wait-AppWindow $process 'MyFinder - Settings'
            Exit-TestApp $settings
            $process.WaitForExit(10000) | Should -BeTrue
        } finally {
            foreach ($image in $images) { $image.Dispose() }
            $underlay.Dispose(); Stop-TestApp $process
            [MyFinderDesktopTest]::SetThreadDpiAwarenessContext($oldDpi) | Out-Null
        }
    }
    It 'preserves hidden intent across restart and prevents a duplicate profile instance' {
        $profile = Join-Path $TestDrive 'hidden'
        New-Item $profile -ItemType Directory | Out-Null
        '{"Version":1,"Shown":false}' | Set-Content "$profile/preferences.json" -Encoding UTF8
        $process = Start-Process "$root/artifacts/app/MyFinder.exe" -ArgumentList '--profile', ('"' + $profile + '"'), '--settings' -PassThru
        $duplicate = $null
        try {
            $settings = Wait-AppWindow $process 'MyFinder - Settings'
            $duplicate = Start-Process "$root/artifacts/app/MyFinder.exe" -ArgumentList '--profile', ('"' + $profile + '"') -PassThru
            $duplicate.WaitForExit(10000) | Should -BeTrue
            $duplicate.ExitCode | Should -Be 0
            $process.Refresh(); $process.HasExited | Should -BeFalse
            Exit-TestApp $settings
            $process.WaitForExit(10000) | Should -BeTrue
            $saved = Get-Content "$profile/preferences.json" -Raw | ConvertFrom-Json
            $saved.Shown | Should -BeFalse
            Test-Path "$profile/last-error.txt" | Should -BeFalse
        }
        finally {
            if ($duplicate) { if (-not $duplicate.HasExited) { $duplicate.Kill(); $duplicate.WaitForExit() }; $duplicate.Dispose() }
            Stop-TestApp $process
        }
    }
}
