Describe 'Published application' {
    It 'opens a real WinUI window and exits when it is closed' {
        $root = Split-Path $PSScriptRoot
        $process = Start-Process "$root/artifacts/app/MyFinder.exe" -PassThru
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(30)
            do {
                Start-Sleep -Milliseconds 100
                $process.Refresh()
            } while (-not $process.HasExited -and $process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
            $process.HasExited | Should -BeFalse -Because 'the application must remain alive until its window is closed'
            $process.MainWindowHandle | Should -Not -Be 0
            $process.MainWindowTitle | Should -BeExactly 'MyFinder'
            $process.CloseMainWindow() | Should -BeTrue
            $process.WaitForExit(10000) | Should -BeTrue
            $process.ExitCode | Should -Be 0
        }
        finally {
            if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
            $process.Dispose()
        }
    }
}
