#requires -Version 5.1
[CmdletBinding()]
param()

function ConvertFrom-SewerPilotEnvLines {
    param([AllowEmptyCollection()][string[]]$Lines, [string]$SourceName = 'environment input')
    $known = @('SEWERPILOT_ENV', 'SEWERPILOT_LOG_LEVEL', 'SEWERPILOT_OPENFLOWS_DISCOVERY_MODE',
        'SEWERPILOT_OPENFLOWS_HOME', 'SEWERPILOT_SAMPLE_MODEL', 'SEWERPILOT_ARTIFACTS_DIR')
    $values = @{}
    $lineNumber = 0
    foreach ($line in $Lines) {
        $lineNumber++
        $text = $line.Trim()
        if (-not $text -or $text.StartsWith('#')) { continue }
        if ($text -notmatch '^([A-Z][A-Z0-9_]*)\s*=(.*)$') {
            throw "$SourceName line ${lineNumber}: expected KEY=VALUE (value suppressed)."
        }
        $name = $Matches[1]
        $value = $Matches[2].Trim()
        if ($known -cnotcontains $name) {
            throw "$SourceName line ${lineNumber}: unknown variable (value suppressed)."
        }
        if ($values.ContainsKey($name)) {
            throw "$SourceName line ${lineNumber}: duplicate variable (value suppressed)."
        }
        if ($value.StartsWith('"') -or $value.StartsWith("'")) {
            if ($value.Length -lt 2 -or $value[$value.Length - 1] -cne $value[0]) {
                throw "$SourceName line ${lineNumber}: unmatched quote (value suppressed)."
            }
            $value = $value.Substring(1, $value.Length - 2)
        }
        $values[$name] = $value
    }
    return $values
}

function Get-SewerPilotEnvironment {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)
    $values = @{
        SEWERPILOT_ENV = 'Development'
        SEWERPILOT_LOG_LEVEL = 'Information'
        SEWERPILOT_OPENFLOWS_DISCOVERY_MODE = 'auto'
        SEWERPILOT_OPENFLOWS_HOME = ''
        SEWERPILOT_SAMPLE_MODEL = ''
        SEWERPILOT_ARTIFACTS_DIR = ''
    }
    $localPresent = $false
    foreach ($name in @('.env.example', '.env.local')) {
        $path = Join-Path $RepositoryRoot $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            if (Test-Path -LiteralPath $path) { throw "$name must be a readable file." }
            if ($name -eq '.env.example') { throw 'Required .env.example is missing.' }
            continue
        }
        if ($name -eq '.env.local') { $localPresent = $true }
        try { $lines = [System.IO.File]::ReadAllLines($path) }
        catch { throw "Cannot read $name (contents suppressed)." }
        $loaded = ConvertFrom-SewerPilotEnvLines -Lines $lines -SourceName $name
        foreach ($key in $loaded.Keys) { $values[$key] = $loaded[$key] }
    }
    if ([string]::IsNullOrWhiteSpace($values.SEWERPILOT_ENV) -or
        [string]::IsNullOrWhiteSpace($values.SEWERPILOT_LOG_LEVEL)) {
        throw 'Environment and log level must be nonempty (values suppressed).'
    }
    if ($values.SEWERPILOT_OPENFLOWS_DISCOVERY_MODE -cne 'auto') {
        throw 'Discovery mode must be auto (configured value suppressed).'
    }
    return [pscustomobject]@{ Values = $values; LocalPresent = $localPresent }
}

# Dot-sourcing exposes parsing helpers without applying environment changes.
if ($MyInvocation.InvocationName -eq '.') { return }

try {
    $ErrorActionPreference = 'Stop'
    $envRepository = Split-Path -Parent $PSScriptRoot
    $environment = Get-SewerPilotEnvironment -RepositoryRoot $envRepository
    foreach ($name in $environment.Values.Keys) {
        [Environment]::SetEnvironmentVariable($name, $environment.Values[$name], 'Process')
    }
    Write-Output 'ENV_STATUS: PASS'
    Write-Output ('LOCAL_OVERRIDE: ' + $(if ($environment.LocalPresent) { 'PRESENT' } else { 'NOT_PRESENT' }))
    Write-Output 'OPENFLOWS_DISCOVERY_MODE: auto'
    exit 0
}
catch {
    Write-Output 'ENV_STATUS: FAIL'
    Write-Output ('ENV_ERROR: ' + $_.Exception.Message)
    exit 1
}
