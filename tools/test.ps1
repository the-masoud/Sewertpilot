#requires -Version 5.1
[CmdletBinding()]
param([string]$Configuration)

function Get-SewerPilotTestProjects {
    param([string]$RepositoryRoot)
    $testRoot = Join-Path $RepositoryRoot 'tests'
    if (-not (Test-Path -LiteralPath $testRoot -PathType Container)) { return }
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($testRoot)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        $directoryItem = Get-Item -LiteralPath $directory -Force -ErrorAction Stop
        if ($directoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { continue }
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop) {
            if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { continue }
            if ($item.PSIsContainer) {
                if (@('.git', '.vs', 'bin', 'obj', 'artifacts') -notcontains $item.Name) { $pending.Push($item.FullName) }
                continue
            }
            if ($item.Extension -ne '.csproj') { continue }
            $readerSettings = [System.Xml.XmlReaderSettings]::new()
            $readerSettings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
            $readerSettings.XmlResolver = $null
            $reader = $null
            try {
                $reader = [System.Xml.XmlReader]::Create($item.FullName, $readerSettings)
                $projectXml = [System.Xml.XmlDocument]::new()
                $projectXml.XmlResolver = $null
                $projectXml.Load($reader)
                $testFlags = @($projectXml.SelectNodes("//*[local-name()='IsTestProject']") | Where-Object { $_.InnerText.Trim() -eq 'true' })
                if ($item.Name -like '*.Tests.csproj' -or $testFlags.Count -gt 0) {
                    $harnessFlags = @($projectXml.SelectNodes("//*[local-name()='SewerPilotTestHarness']") | Where-Object { $_.InnerText.Trim() -eq 'true' })
                    $framework = @($projectXml.SelectNodes("//*[local-name()='TargetFramework']") | Select-Object -First 1)
                    [pscustomobject]@{ Path = $item.FullName; IsHarness = ($harnessFlags.Count -gt 0); Framework = $(if ($framework.Count) { $framework[0].InnerText.Trim() } else { $null }) }
                }
            }
            catch { throw 'A project under tests/ contains invalid or unsafe XML.' }
            finally { if ($reader) { $reader.Dispose() } }
        }
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }

try {
    $ErrorActionPreference = 'Stop'
    $testRepository = Split-Path -Parent $PSScriptRoot
    $testRequested = $Configuration
    . (Join-Path $PSScriptRoot 'build.ps1')
    $settings = Get-SewerPilotBuildConfiguration -RepositoryRoot $testRepository
    $requested = Get-SewerPilotConfigurationName -Settings $settings -Requested $testRequested
    $projects = @(Get-SewerPilotTestProjects -RepositoryRoot $testRepository | Sort-Object Path)
    if ($projects.Count -eq 0) {
        Write-Output 'TEST_STATUS: NO_TEST_PROJECTS_YET'
        exit 0
    }
    $tools = Get-SewerPilotBuildTools
    if (-not $tools.Dotnet) { throw 'A local dotnet SDK is required by the current test runner.' }
    foreach ($project in $projects) {
        # Explicit project metadata opts a package-free executable into generic discovery.
        if ($project.IsHarness) {
            if ($project.Framework -ceq 'net48') {
                $projectDirectory = Split-Path -Parent $project.Path
                $harnessName = [System.IO.Path]::GetFileNameWithoutExtension($project.Path)
                $executableCandidates = @(
                    (Join-Path $projectDirectory ("bin/{0}/net48/{1}.exe" -f $requested, $harnessName)),
                    (Join-Path $projectDirectory ("bin/x64/{0}/net48/{1}.exe" -f $requested, $harnessName))
                )
                $executablePath = $executableCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
                if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
                    throw 'Prebuilt .NET Framework test harness is missing; build the solution first.'
                }
                $harnessOutput = @(& $executablePath 2>&1)
                $result = [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $harnessOutput }
            }
            else {
                $result = Invoke-SewerPilotDotnet -Executable $tools.Dotnet -Arguments @('run', '--project', $project.Path, '--configuration', $requested, '--no-build', '--no-restore')
            }
        }
        else {
            $result = Invoke-SewerPilotDotnet -Executable $tools.Dotnet -Arguments @('test', $project.Path, '--configuration', $requested, '--no-build', '--no-restore', '--nologo')
        }
        $result.Output | Write-Output
        if ($result.ExitCode -ne 0) { throw 'A local test project failed; prebuilt/restored projects are required.' }
    }
    Write-Output ('TEST_PROJECTS: ' + $projects.Count)
    Write-Output 'TEST_STATUS: PASS'
    exit 0
}
catch {
    Write-Output 'TEST_STATUS: FAIL'
    Write-Output ('TEST_ERROR: ' + $_.Exception.Message)
    exit 1
}
