#requires -Version 5.1
[CmdletBinding()]
param([string]$Configuration)

function Resolve-SewerPilotRepositoryPath {
    param([string]$RepositoryRoot, [string]$RelativePath)
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [System.IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath -match '[:*?"<>|]' -or
        $RelativePath -match '(^|[\\/])\.\.([\\/]|$)') {
        throw 'Configuration paths must be repository-relative without parent traversal.'
    }
    $root = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
    $path = [System.IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    if (-not $path.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Configuration path must stay below the repository root.'
    }
    $cursor = $path
    while ($cursor -and $cursor -ne $root) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
            if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                throw 'Configuration paths must not traverse filesystem links.'
            }
        }
        $cursor = Split-Path -Parent $cursor
    }
    return $path
}

function Get-SewerPilotBuildConfiguration {
    param([string]$RepositoryRoot, [string]$JsonText)
    if (-not $PSBoundParameters.ContainsKey('JsonText')) {
        try { $JsonText = [System.IO.File]::ReadAllText((Join-Path $RepositoryRoot 'build/BuildConfiguration.json')) }
        catch { throw 'Cannot read build/BuildConfiguration.json.' }
    }
    try { $settings = ConvertFrom-Json -InputObject $JsonText -ErrorAction Stop }
    catch { throw 'BuildConfiguration.json must contain valid JSON (contents suppressed).' }
    $keys = @('schemaVersion', 'defaultConfiguration', 'allowedConfigurations', 'solution',
        'artifactsDirectory', 'allowNoSolution', 'toolPreference')
    if ($null -eq $settings -or $settings -isnot [pscustomobject] -or
        @($settings.PSObject.Properties).Count -ne $keys.Count) {
        throw 'BuildConfiguration.json must contain exactly the schema version 1 fields.'
    }
    foreach ($key in $keys) {
        if (@($settings.PSObject.Properties.Name) -cnotcontains $key) {
            throw "BuildConfiguration.json is missing required field $key."
        }
    }
    if (($settings.schemaVersion -isnot [int] -and $settings.schemaVersion -isnot [long]) -or
        $settings.schemaVersion -ne 1) { throw 'Unsupported build configuration schema.' }
    if ($settings.allowedConfigurations -isnot [array] -or $settings.allowedConfigurations.Count -eq 0 -or
        @($settings.allowedConfigurations | Select-Object -Unique).Count -ne $settings.allowedConfigurations.Count) {
        throw 'allowedConfigurations must be a nonempty unique array.'
    }
    foreach ($name in $settings.allowedConfigurations) {
        if ($name -isnot [string] -or @('Debug', 'Release') -cnotcontains $name) {
            throw 'Only Debug and Release build configurations are supported.'
        }
    }
    if ($settings.defaultConfiguration -isnot [string] -or
        $settings.allowedConfigurations -cnotcontains $settings.defaultConfiguration) {
        throw 'defaultConfiguration must belong to allowedConfigurations.'
    }
    if ($settings.allowNoSolution -isnot [bool]) { throw 'allowNoSolution must be a Boolean.' }
    if ($settings.toolPreference -isnot [array] -or $settings.toolPreference.Count -eq 0 -or
        @($settings.toolPreference | Select-Object -Unique).Count -ne $settings.toolPreference.Count) {
        throw 'toolPreference must be a nonempty unique array.'
    }
    foreach ($tool in $settings.toolPreference) {
        if ($tool -isnot [string] -or @('dotnet', 'msbuild') -cnotcontains $tool) {
            throw 'Only dotnet and msbuild tool preferences are supported.'
        }
    }
    if ($settings.artifactsDirectory -isnot [string]) { throw 'artifactsDirectory must be a relative directory.' }
    $null = Resolve-SewerPilotRepositoryPath $RepositoryRoot $settings.artifactsDirectory
    if ($null -ne $settings.solution) {
        if ($settings.solution -isnot [string] -or $settings.solution -notmatch '\.sln$') {
            throw 'solution must be null or a relative .sln path.'
        }
        $null = Resolve-SewerPilotRepositoryPath $RepositoryRoot $settings.solution
    }
    return $settings
}

function Get-SewerPilotConfigurationName {
    param($Settings, [string]$Requested)
    if (-not $Requested) { $Requested = $Settings.defaultConfiguration }
    if ($Settings.allowedConfigurations -cnotcontains $Requested) {
        throw 'Requested configuration is not allowed (configured value suppressed).'
    }
    return $Requested
}

function Invoke-SewerPilotDotnet {
    param([string]$Executable, [string[]]$Arguments)
    $saved = @{}
    $offline = @{
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        DOTNET_NOLOGO = '1'
    }
    try {
        foreach ($name in $offline.Keys) {
            $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, $offline[$name], 'Process')
        }
        $output = @(& $Executable @Arguments 2>&1)
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }
    finally {
        foreach ($name in $saved.Keys) {
            [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process')
        }
    }
}

function Get-SewerPilotBuildTools {
    $dotnetPath = $null
    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($dotnetCommand) {
        try {
            $sdk = Invoke-SewerPilotDotnet -Executable $dotnetCommand.Source -Arguments @('--list-sdks')
            if ($sdk.ExitCode -eq 0 -and ($sdk.Output -join "`n") -match '(?m)^\d+\.\d+\.\d+[^\r\n]*\[') {
                $dotnetPath = $dotnetCommand.Source
            }
        }
        catch { $dotnetPath = $null }
    }
    $msbuildPath = $null
    $msbuildCommand = Get-Command MSBuild -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($msbuildCommand) { $msbuildPath = $msbuildCommand.Source }
    if (-not $msbuildPath) {
        $vswhereCommand = Get-Command vswhere -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        $vswherePath = $null
        if ($vswhereCommand) { $vswherePath = $vswhereCommand.Source }
        else {
            $programRoot = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)', 'Process')
            if ($programRoot) {
                $candidate = Join-Path $programRoot 'Microsoft Visual Studio/Installer/vswhere.exe'
                if (Test-Path -LiteralPath $candidate -PathType Leaf) { $vswherePath = $candidate }
            }
        }
        if ($vswherePath) {
            try {
                $candidates = @(& $vswherePath -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null)
                if ($LASTEXITCODE -eq 0) {
                    $msbuildPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
                }
            }
            catch { $msbuildPath = $null }
        }
    }
    if ($msbuildPath) {
        try {
            $null = & $msbuildPath -version -nologo 2>$null
            if ($LASTEXITCODE -ne 0) { $msbuildPath = $null }
        }
        catch { $msbuildPath = $null }
    }
    return [pscustomobject]@{ Dotnet = $dotnetPath; MSBuild = $msbuildPath }
}

# Other foundation scripts reuse validation/discovery without executing a build.
if ($MyInvocation.InvocationName -eq '.') { return }

try {
    $ErrorActionPreference = 'Stop'
    $buildRepository = Split-Path -Parent $PSScriptRoot
    $settings = Get-SewerPilotBuildConfiguration -RepositoryRoot $buildRepository
    $requested = Get-SewerPilotConfigurationName -Settings $settings -Requested $Configuration
    $tools = Get-SewerPilotBuildTools
    Write-Output ('DOTNET: ' + $(if ($tools.Dotnet) { 'FOUND' } else { 'NOT_FOUND' }))
    Write-Output ('MSBUILD: ' + $(if ($tools.MSBuild) { 'FOUND' } else { 'NOT_FOUND' }))
    if ($null -eq $settings.solution) {
        if (-not $settings.allowNoSolution) { throw 'A solution is required by build configuration.' }
        Write-Output 'BUILD_STATUS: NO_SOLUTION_YET'
        exit 0
    }
    $solutionPath = Resolve-SewerPilotRepositoryPath $buildRepository $settings.solution
    if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) { throw 'Configured solution does not exist.' }
    $selected = $settings.toolPreference | Where-Object {
        ($_ -eq 'dotnet' -and $tools.Dotnet) -or ($_ -eq 'msbuild' -and $tools.MSBuild)
    } | Select-Object -First 1
    if (-not $selected) { throw 'No preferred local build tool is available.' }
    if ($selected -eq 'dotnet') {
        $result = Invoke-SewerPilotDotnet -Executable $tools.Dotnet -Arguments @('build', $solutionPath, '--configuration', $requested, '--no-restore', '--nologo')
        $code = $result.ExitCode
    }
    else {
        $null = & ($tools.MSBuild) $solutionPath /nologo /t:Build "/p:Configuration=$requested" /p:RestoreDuringBuild=false
        $code = $LASTEXITCODE
    }
    if ($code -ne 0) { throw "Local build failed with exit code $code; restore is disabled." }
    Write-Output 'BUILD_STATUS: PASS'
    exit 0
}
catch {
    Write-Output 'BUILD_STATUS: FAIL'
    Write-Output ('BUILD_ERROR: ' + $_.Exception.Message)
    exit 1
}
