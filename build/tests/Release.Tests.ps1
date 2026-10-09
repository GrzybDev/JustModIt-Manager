BeforeAll {
    $build = Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $build 'Release.psm1') -Force
}

Describe 'Compare-Version' {
    It 'orders versions by Semantic Versioning precedence' {
        $ordered = '1.0.0-alpha', '1.0.0-alpha.1', '1.0.0-alpha.beta', '1.0.0-beta', '1.0.0-beta.2', '1.0.0-beta.11',
            '1.0.0-rc.1', '1.0.0', '1.0.1', '1.10.0', '2.0.0'
        for ($i = 1; $i -lt $ordered.Count; $i++) {
            Compare-Version $ordered[$i - 1] $ordered[$i] | Should -Be -1
            Compare-Version $ordered[$i] $ordered[$i - 1] | Should -Be 1
        }
        Compare-Version '1.0.0-rc.1' '1.0.0-rc.1' | Should -Be 0
    }
}

Describe 'Confirm-Version' {
    It 'accepts <Version>, which Velopack packs' -TestCases @(
        @{ Version = '0.0.1' }, @{ Version = '0.0.1-ci.1' }, @{ Version = '1.2.3-beta.4' }
    ) {
        { Confirm-Version $Version } | Should -Not -Throw
    }

    It 'refuses <Version>' -TestCases @(
        @{ Version = '0.0.0-ci.2' }, @{ Version = 'v1.2.3' }, @{ Version = '1.2' }, @{ Version = '1.2.03' },
        @{ Version = '1.2.3-alpha.01' }
    ) {
        { Confirm-Version $Version } | Should -Throw
    }
}

Describe 'Get-NextVersion' {
    It '<Bump> <PreRelease> after "<Current>" gives <Expected>' -TestCases @(
        @{ Current = ''; Bump = 'patch'; PreRelease = ''; Expected = '0.0.1' }
        @{ Current = ''; Bump = 'minor'; PreRelease = 'alpha'; Expected = '0.1.0-alpha.1' }
        @{ Current = ''; Bump = 'prerelease'; PreRelease = 'alpha'; Expected = '0.0.1-alpha.1' }
        @{ Current = '1.2.3'; Bump = 'patch'; PreRelease = ''; Expected = '1.2.4' }
        @{ Current = '1.2.3'; Bump = 'minor'; PreRelease = ''; Expected = '1.3.0' }
        @{ Current = '1.2.3'; Bump = 'major'; PreRelease = ''; Expected = '2.0.0' }
        @{ Current = '1.3.0-beta.2'; Bump = 'patch'; PreRelease = ''; Expected = '1.3.0' }
        @{ Current = '1.3.0-beta.2'; Bump = 'minor'; PreRelease = ''; Expected = '1.3.0' }
        @{ Current = '1.3.0-beta.2'; Bump = 'major'; PreRelease = ''; Expected = '2.0.0' }
        @{ Current = '1.3.1-beta.2'; Bump = 'minor'; PreRelease = ''; Expected = '1.4.0' }
        @{ Current = '2.0.0-rc.1'; Bump = 'major'; PreRelease = ''; Expected = '2.0.0' }
        @{ Current = '1.2.3'; Bump = 'minor'; PreRelease = 'beta'; Expected = '1.3.0-beta.1' }
        @{ Current = '1.3.0-beta.2'; Bump = 'patch'; PreRelease = 'alpha'; Expected = '1.3.1-alpha.1' }
        @{ Current = '1.2.0-alpha.3'; Bump = 'prerelease'; PreRelease = ''; Expected = '1.2.0-alpha.4' }
        @{ Current = '1.2.0-alpha.3'; Bump = 'prerelease'; PreRelease = 'alpha'; Expected = '1.2.0-alpha.4' }
        @{ Current = '1.2.0-alpha.3'; Bump = 'prerelease'; PreRelease = 'beta'; Expected = '1.2.0-beta.1' }
        @{ Current = '1.2.3'; Bump = 'prerelease'; PreRelease = 'rc'; Expected = '1.2.4-rc.1' }
        @{ Current = '1.2.0-rc.2'; Bump = 'release'; PreRelease = ''; Expected = '1.2.0' }
    ) {
        Get-NextVersion -Current $Current -Bump $Bump -PreRelease $PreRelease | Should -Be $Expected
    }

    It 'refuses <Bump> <PreRelease> after "<Current>"' -TestCases @(
        @{ Current = '1.2.0'; Bump = 'release'; PreRelease = '' }
        @{ Current = ''; Bump = 'release'; PreRelease = '' }
        @{ Current = '1.2.3'; Bump = 'prerelease'; PreRelease = '' }
        @{ Current = '1.2.0-beta.2'; Bump = 'prerelease'; PreRelease = 'alpha' }
    ) {
        { Get-NextVersion -Current $Current -Bump $Bump -PreRelease $PreRelease } | Should -Throw
    }
}

Describe 'Versions from tags' {
    BeforeAll {
        $shell = (Get-Process -Id $PID).Path
        $repository = Join-Path $TestDrive 'repository'
        New-Item -ItemType Directory (Join-Path $repository 'build'), (Join-Path $repository 'src\SampleApp') | Out-Null
        Copy-Item (Join-Path $build 'Release.psm1'), (Join-Path $build 'version.ps1') (Join-Path $repository 'build')
        Set-Content (Join-Path $repository 'src\SampleApp\Product.props') `
            '<Project><PropertyGroup><ManagerAppSlug>sample-app</ManagerAppSlug></PropertyGroup></Project>'

        function Invoke-Git {
            & git -C $repository -c user.name=Tester -c user.email=tester@example.invalid -c commit.gpgsign=false `
                -c tag.gpgsign=false @args
            if ($LASTEXITCODE -ne 0) { throw "git $args failed." }
        }

        function Invoke-Version([string[]]$Arguments, [string]$Outputs) {
            $workflowOutputs = $env:GITHUB_OUTPUT
            $env:GITHUB_OUTPUT = $Outputs
            $ErrorActionPreference = 'Continue'
            try {
                $output = & $shell -NoProfile -File (Join-Path $repository 'build\version.ps1') @Arguments 2>&1
                return [pscustomobject]@{ Output = ($output | Out-String).Trim(); ExitCode = $LASTEXITCODE }
            }
            finally {
                $env:GITHUB_OUTPUT = $workflowOutputs
            }
        }

        Invoke-Git init -q -b main
        Invoke-Git commit -q --allow-empty -m first
        Invoke-Git tag sample-app/v0.1.0
        Invoke-Git commit -q --allow-empty -m second
        Invoke-Git tag sample-app/v0.2.0-beta.1
        Invoke-Git tag other-app/v3.0.0
        Invoke-Git tag sample-app/vnext
        Invoke-Git switch -q -c side
        Invoke-Git commit -q --allow-empty -m side
        Invoke-Git tag sample-app/v5.0.0
        Invoke-Git switch -q main
    }

    It "lists the versions of the app's tags that HEAD contains, oldest first" {
        $listed = & $shell -NoProfile -Command "Import-Module '$repository\build\Release.psm1'; Get-AppTagVersions -Slug sample-app"

        $listed | Should -Be @('0.1.0', '0.2.0-beta.1')
    }

    It 'bumps the newest tag and hands the release to the workflow' {
        $outputs = Join-Path $TestDrive 'outputs.txt'
        Set-Content $outputs '' -NoNewline

        $result = Invoke-Version @('-App', 'sample-app', '-Bump', 'prerelease', '-PreRelease', 'none') $outputs

        $result.ExitCode | Should -Be 0
        $result.Output | Should -Be '0.2.0-beta.2'
        Get-Content $outputs | Should -Be @(
            'version=0.2.0-beta.2', 'tag=sample-app/v0.2.0-beta.2', 'prerelease=true', 'slug=sample-app')
    }

    It 'gives a CI build a pre-release of the next patch that its first alpha replaces' {
        $version = (Invoke-Version @('-App', 'SampleApp', '-Ci', '7')).Output

        $version | Should -Be '0.2.0-0.ci.7'
        Compare-Version $version '0.2.0-alpha.1' | Should -Be -1
    }

    It 'refuses a version whose tag marks another commit' {
        $result = Invoke-Version @('-App', 'sample-app', '-Version', '0.1.0')

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'sample-app/v0\.1\.0 already marks another commit'
    }

    It 'releases the commit that a pushed tag marks, when the tag is spelt as the app''s tags are' {
        Invoke-Git tag sample-app/v0.2.0

        $release = Invoke-Version @('-Tag', 'sample-app/v0.2.0')
        $misspelt = Invoke-Version @('-Tag', 'SampleApp/v0.3.0')

        $release.Output | Should -Be '0.2.0'
        $misspelt.ExitCode | Should -Not -Be 0
        $misspelt.Output | Should -Match 'SampleApp/v0\.3\.0 is not one of the app''s tags'
    }
}
