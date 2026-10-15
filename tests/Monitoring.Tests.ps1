BeforeAll {
    $root = Split-Path $PSScriptRoot
    $hostProject = Join-Path $PSScriptRoot 'MyFinder.Scenarios/MyFinder.Scenarios.csproj'
    & dotnet build $hostProject -c Release --nologo -p:RestoreLockedMode=true | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Scenario host build failed.' }
    $hostDll = Join-Path $PSScriptRoot 'MyFinder.Scenarios/bin/Release/net10.0-windows10.0.19041.0/MyFinder.Scenarios.dll'
    function Invoke-Scenario([string]$Name, [string]$Directory) {
        $output = & dotnet $hostDll $Name $Directory
        if ($LASTEXITCODE -ne 0) { throw "Scenario failed: $Name" }
        $output | ConvertFrom-Json
    }
}
Describe 'Desktop presentation policy' {
    It 'preserves explicit intent, lock state, display preference and fullscreen scope' {
        $r = Invoke-Scenario policy
        $r.HiddenDuringFullscreen | Should -BeFalse
        $r.HiddenAfterFullscreen | Should -BeFalse
        $r.ShowDuringFullscreen | Should -BeFalse
        $r.Restore | Should -BeTrue
        $r.RetreatDisabled | Should -BeTrue
        $r.Locked | Should -BeFalse
        $r.Preferred | Should -Be secondary
        $r.Fallback | Should -Be primary
        $r.Restored | Should -Be secondary
        $r.Small.Width | Should -Be 200
        $r.Small.Height | Should -Be 160
        $r.Negative.X | Should -Be -200
        $r.Negative.Bottom | Should -Be 984
        $r.Maximized | Should -BeFalse
        $r.Borderless | Should -BeTrue
        $r.OtherDisplay | Should -BeFalse
    }
}
Describe 'Observations, rates and bounded history' {
    It 'uses actual elapsed intervals and excludes counter resets and gaps from totals' {
        $r = Invoke-Scenario rates
        $r.First | Should -Be WarmingUp
        $r.Second | Should -Be 1000
        $r.Third | Should -Be 1000
        $r.Zero | Should -Be 0
        $r.Reset | Should -Be WarmingUp
        $r.Resume | Should -Be WarmingUp
        $r.Total | Should -Be 3000
        $r.CoverageSeconds | Should -Be 4
        $r.Segment | Should -Be 3
    }
    It 'bounds churn and preserves independent histories, gaps and extrema through drawing reduction' {
        $r = Invoke-Scenario history
        $r.EstimatedBytes | Should -BeLessOrEqual 4096
        $r.Independent | Should -BeGreaterThan 0
        $r.First | Should -BeGreaterOrEqual 89
        $r.Empty | Should -Be 0
        $r.Segments | Should -Be 3
        $r.Spike | Should -Be 100
        $r.Reduced | Should -BeLessThan 150
    }
    It 'retains valid choices and atomically persists the last change without overwriting a newer schema' {
        $r = Invoke-Scenario preferences (Join-Path $TestDrive 'preferences')
        $r.Shown | Should -BeFalse
        $r.Scale | Should -Be 1
        $r.Theme | Should -Be Dark
        $r.LastRange | Should -Be 5
        $r.AllSaved | Should -BeTrue
        $r.NewerReadOnly | Should -BeTrue
        $r.NewerSaved | Should -BeFalse
        $r.NewerPreserved | Should -BeTrue
        $r.InvalidVersionSurvived | Should -BeTrue
        $r.NullUsesDefault | Should -BeTrue
        $r.CorruptPreserved | Should -BeTrue
        $r.SaveFailureVisible | Should -BeTrue
    }
    It 'expires retained history while all monitoring is disabled without resurrecting it' {
        $r = Invoke-Scenario expiry
        $r.Before | Should -Be 1
        $r.After | Should -Be 0
        $r.Restored | Should -Be 0
    }
    It 'continues unrelated real collection and marks a monitoring selection boundary' {
        $r = Invoke-Scenario session
        $r.Before | Should -BeGreaterThan 1
        $r.After | Should -BeGreaterThan $r.Before
        $r.Cpu | Should -BeGreaterThan $r.After
        $r.Paused | Should -Be NotMonitored
        $r.Boundary | Should -BeTrue
    }
}
Describe 'Native CPU and memory collection' {
    It 'collects real utilization and a coherent non-overlapping physical-memory partition' {
        $r = Invoke-Scenario system
        $r.Cpu | Should -Not -BeNullOrEmpty
        $r.Cpu | Should -BeGreaterOrEqual 0
        $r.Cpu | Should -BeLessOrEqual 100
        $r.Logical | Should -BeGreaterThan 0
        $r.Total | Should -BeGreaterThan 0
        ($r.Used + $r.Available) | Should -Be $r.Total
        $r.Processes | Should -BeGreaterThan 0
        $r.First | Should -Be WarmingUp
        $r.CpuFacts.Model | Should -Not -BeNullOrEmpty
        [int]$r.CpuFacts.'Physical cores' | Should -BeGreaterThan 0
        [int]$r.MemoryFacts.'Populated modules' | Should -BeGreaterThan 0
    }
}
