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
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    public static Rect Bounds(IntPtr hwnd) { Rect r; GetWindowRect(hwnd, out r); return r; }
    [DllImport("user32.dll")] public static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    public static string ProcessName(IntPtr hwnd) { uint process; GetWindowThreadProcessId(hwnd, out process); return System.Diagnostics.Process.GetProcessById((int)process).ProcessName; }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
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
            if ($window) { return $window }
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
    function Stop-TestApp([Diagnostics.Process]$Process) {
        try {
            if (-not $Process.HasExited) {
                $settings = Find-AppWindow $Process.Id 'MyFinder - Settings'
                if ($settings) { Invoke-AppButton $settings ExitMyFinder }
                if (-not $Process.WaitForExit(10000)) { $Process.Kill(); $Process.WaitForExit() }
            }
        } finally { $Process.Dispose() }
    }
}

Describe 'Published monitoring application' {
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
            $texts = $details.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
            ($texts -join "`n") | Should -Match 'Volume capacity'
            ($texts -join "`n") | Should -Match 'Caller available'
            Invoke-AppButton $details DetailSettings
            $settings = Wait-AppWindow $process 'MyFinder - Settings'; Invoke-AppButton $settings RefreshResources
            Invoke-AppButton $settings ('InspectResource_' + $ids[1])
            $details = Wait-AppWindow $process 'MyFinder - Details'
            $texts = $details.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
            ($texts -join "`n") | Should -Match ([regex]::Escape($ids[1]))
            Invoke-AppButton $details StorageOverview
            $texts = $details.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
            ($texts -join "`n") | Should -Match 'All monitored volumes and devices'
            Invoke-AppButton $details DetailSettings
            $settings = Wait-AppWindow $process 'MyFinder - Settings'
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
            $settings = Wait-AppWindow $process 'MyFinder - Settings'; Invoke-AppButton $settings ExitMyFinder
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
            Invoke-AppButton $settings RefreshResources
            Invoke-AppButton $settings ('InspectResource_' + $ids[1])
            $details = Wait-AppWindow $process 'MyFinder - Details'
            $texts = $details.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
            ($texts -join "`n") | Should -Match ([regex]::Escape($ids[1]))
            $panel.Current.BoundingRectangle.Width | Should -Be $bounds.Width
            $panel.Current.BoundingRectangle.Height | Should -Be $bounds.Height
            Invoke-AppButton $details DetailSettings
            $settings = Wait-AppWindow $process 'MyFinder - Settings'
            Invoke-AppButton $settings HidePanel
            Invoke-AppButton $settings ShowPanel
            Invoke-AppButton $settings ExitMyFinder
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
            $statusCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MonitoringStatus')
            $status = $settings.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCondition)
            $before = [int]([regex]::Match($status.Current.Name, '([\d,]+) retained observations').Groups[1].Value.Replace(',',''))
            Invoke-AppButton $settings HidePanel
            [MyFinderDesktopTest]::IsWindowVisible($panelHandle) | Should -BeFalse
            $process.HasExited | Should -BeFalse
            Start-Sleep -Seconds 2
            $after = [int]([regex]::Match($status.Current.Name, '([\d,]+) retained observations').Groups[1].Value.Replace(',',''))
            $after | Should -BeGreaterThan $before
            Invoke-AppButton $settings ShowPanel
            $panel = Wait-AppWindow $process MyFinder
            [MyFinderDesktopTest]::IsWindowVisible($panelHandle) | Should -BeTrue
            Invoke-AppButton $panel Inspect_cpu
            $details = Wait-AppWindow $process 'MyFinder - Details'
            Start-Sleep -Seconds 2
            $texts = $details.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
            ($texts -join "`n") | Should -Match 'Current snapshot'
            Invoke-AppButton $details CloseDetails
            $process.Refresh(); $process.HasExited | Should -BeFalse
            Invoke-AppButton $panel OpenSettings
            $settings = Wait-AppWindow $process 'MyFinder - Settings'
            Invoke-AppButton $settings ExitMyFinder
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
                    Invoke-AppButton $settings ExitMyFinder
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
            $appearance = ($settings.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }) -join ' '
            Invoke-AppButton $settings CloseSettings
            $nativeBounds = [MyFinderDesktopTest]::Bounds([IntPtr]$panel.Current.NativeWindowHandle)
            $bounds = New-Object System.Drawing.Rectangle($nativeBounds.Left, $nativeBounds.Top, ($nativeBounds.Right-$nativeBounds.Left), ($nativeBounds.Bottom-$nativeBounds.Top))
            $underlay.FormBorderStyle = 'None'; $underlay.ShowInTaskbar = $false; $underlay.TopMost = $true
            $underlay.StartPosition = 'Manual'
            $underlay.Bounds = New-Object System.Drawing.Rectangle([int]$bounds.X, [int]$bounds.Y, [int]($bounds.Width+12), [int]($bounds.Height+12))
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
                # Check an exposed owned-underlay pixel before attributing the panel's alpha response.
                $underlayPoint = New-Object MyFinderDesktopTest+Point([int]($bounds.Right+4), [int]($bounds.Y+4))
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
            Invoke-AppButton $settings ExitMyFinder
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
            Invoke-AppButton $settings ExitMyFinder
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
