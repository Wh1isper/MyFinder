Describe 'Test runner exit contract' {
    BeforeAll { $runner = Join-Path (Split-Path $PSScriptRoot) 'scripts/test.ps1' }

    It 'fails when cleanup fails even if every test assertion passed' {
        Mock Invoke-Pester { [pscustomobject]@{ Result = 'Failed'; PassedCount = 1; FailedCount = 0; FailedBlocksCount = 1 } }
        { & $runner } | Should -Throw '*Tests failed*'
    }

    It 'fails when no tests ran' {
        Mock Invoke-Pester { [pscustomobject]@{ Result = 'Passed'; PassedCount = 0; FailedCount = 0 } }
        { & $runner } | Should -Throw '*no tests ran*'
    }

    It 'succeeds only for a passing nonempty run' {
        Mock Invoke-Pester { [pscustomobject]@{ Result = 'Passed'; PassedCount = 1; FailedCount = 0 } }
        { & $runner } | Should -Not -Throw
    }
}
