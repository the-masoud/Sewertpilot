#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('All','Dependencies','Secrets')][string]$Scope = 'All',
    [string]$EvidenceDirectory = 'docs/evidence/remediation-20261021/security',
    [ValidateRange(10,600)][int]$TimeoutSeconds = 180,
    # Advisory index used as positive proof that vulnerability data was actually available.
    # Point this at an internal mirror for air-gapped qualification; if it cannot be read the
    # dependency gate reports NOT_PROVEN instead of claiming zero vulnerabilities.
    [string]$AdvisoryIndexUrl = 'https://api.nuget.org/v3/vulnerabilities/index.json',
    # N03: air-gapped authority. When supplied, this local advisory index REPLACES the remote URL
    # and is validated for existence, non-zero size, freshness and schema before any clearance.
    # A missing, empty, stale or malformed feed fails closed; it is never read as "zero findings".
    [string]$OfflineDbPath,
    # Maximum accepted age of an offline advisory feed. 0 disables the freshness check.
    [ValidateRange(0,3650)][int]$AdvisoryMaxAgeDays = 30
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$evidence = if ([IO.Path]::IsPathRooted($EvidenceDirectory)) { $EvidenceDirectory } else { Join-Path $root $EvidenceDirectory }
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
function Save-Receipt($name, $receipt) {
    $receipt | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $evidence $name) -Encoding UTF8
}
function Get-EvidenceSha256([string]$Path) {
    # The net48 harness may inherit a PowerShell 7 module path when starting
    # Windows PowerShell. Do not depend on the Get-FileHash script module.
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function Invoke-DotnetQuery {
    # Runs one dotnet package query and returns its raw exit code and streams.
    # Raw output is never persisted: feed URLs/configuration may contain credentials.
    param([string]$Arguments, [int]$TimeoutSeconds, [string]$Root)
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = (Get-Command dotnet -ErrorAction Stop).Source
    $start.Arguments = $Arguments
    $start.WorkingDirectory = $Root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { $process.Kill(); throw 'DEPENDENCY_QUERY_TIMEOUT' }
        return [ordered]@{ exit_code = $process.ExitCode; output = $stdout.Result; error = $stderr.Result }
    } finally { $process.Dispose() }
}
function Get-AdvisoryItems {
    # N03: the live NuGet v3 vulnerability index is a TOP-LEVEL JSON ARRAY, not { items: [...] }.
    # Reading $index.items on that shape yields nulls, and Windows PowerShell 5.1 counts a null
    # member-enumeration result as real elements -- which made the old "advisory item count" proof
    # vacuous (it counted nulls). Normalize both shapes and drop nulls before any count is trusted.
    param($Parsed)
    $items = @()
    if ($null -eq $Parsed) { return ,$items }
    if ($Parsed -is [System.Array]) { $items = @($Parsed) }
    elseif ($null -ne $Parsed.items) { $items = @($Parsed.items) }
    elseif ($null -ne $Parsed.'@id') { $items = @($Parsed) }
    $items = @($items | Where-Object { $null -ne $_ })
    return ,$items
}
function Resolve-AdvisoryAuthority {
    # N03: absence of verification data is a critical failure, never a clean bill of health.
    # The authority must exist, be non-empty, be schema-valid and (offline) be fresh. Every
    # rejection path throws, so no caller can proceed with an unproven advisory database.
    param([string]$OfflinePath, [string]$IndexUrl, [int]$MaxAgeDays, [string]$Root)
    if ($OfflinePath) {
        $path = if ([IO.Path]::IsPathRooted($OfflinePath)) { $OfflinePath } else { Join-Path $Root $OfflinePath }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'VULNERABILITY_DATA_UNAVAILABLE' }
        $info = Get-Item -LiteralPath $path
        if ($info.Length -le 0) { throw 'VULNERABILITY_DATA_UNAVAILABLE' }
        if ($MaxAgeDays -gt 0) {
            $ageDays = ([DateTime]::UtcNow - $info.LastWriteTimeUtc).TotalDays
            if ($ageDays -gt $MaxAgeDays) { throw 'VULNERABILITY_DATA_STALE' }
        }
        try { $index = [IO.File]::ReadAllText($path) | ConvertFrom-Json }
        catch { throw 'VULNERABILITY_DATA_UNAVAILABLE' }
        $items = Get-AdvisoryItems -Parsed $index
        if ($items.Count -eq 0) { throw 'VULNERABILITY_DATA_UNAVAILABLE' }
        foreach ($item in $items) {
            if ([string]::IsNullOrWhiteSpace([string]$item.'@id')) { throw 'VULNERABILITY_DATA_UNAVAILABLE' }
            $itemUri = $null
            if (-not [Uri]::TryCreate([string]$item.'@id', [UriKind]::Absolute, [ref]$itemUri)) { throw 'VULNERABILITY_DATA_UNAVAILABLE' }
        }
        return [ordered]@{ items = $items; source = 'OfflineDbPath'; reference = $path; sha256 = (Get-EvidenceSha256 $path) }
    }
    try { $index = Invoke-RestMethod -Uri $IndexUrl -TimeoutSec 30 -UseBasicParsing -ErrorAction Stop }
    catch { throw 'VULNERABILITY_DATA_UNAVAILABLE' }
    $items = Get-AdvisoryItems -Parsed $index
    if ($items.Count -eq 0) { throw 'VULNERABILITY_DATA_UNAVAILABLE' }
    foreach ($item in $items) {
        if ([string]::IsNullOrWhiteSpace([string]$item.'@id')) { throw 'VULNERABILITY_DATA_UNAVAILABLE' }
        $itemUri = $null
        if (-not [Uri]::TryCreate([string]$item.'@id', [UriKind]::Absolute, [ref]$itemUri)) { throw 'VULNERABILITY_DATA_UNAVAILABLE' }
    }
    return [ordered]@{ items = $items; source = 'AdvisoryIndexUrl'; reference = $IndexUrl; sha256 = $null }
}
if ($Scope -in @('All','Dependencies')) {
    $receipt = [ordered]@{ timestamp_utc = [DateTime]::UtcNow.ToString('o'); status = 'UNKNOWN'; command = 'dotnet list SewerPilot.sln package --vulnerable --include-transitive --format json --no-restore'; exit_code = $null; projects = 0; vulnerable_packages = 0; packages_enumerated = 0; limitation = 'Point-in-time NuGet advisory query; does not assess vendor native DLLs or future advisories. PASS additionally requires a readable advisory index, a non-empty enumerated package graph and a problem-free query; otherwise the gate reports NOT_PROVEN / VULNERABILITY_DATA_UNAVAILABLE.' }
    try {
        $query = Invoke-DotnetQuery -Arguments 'list SewerPilot.sln package --vulnerable --include-transitive --format json --no-restore' -TimeoutSeconds $TimeoutSeconds -Root $root
        $receipt.exit_code = $query.exit_code
        if ($query.exit_code -ne 0 -or $query.error -match '(?i)error|NU1900|unable|failed' -or $query.output -match '(?i)NU1900|NU1301|unable to load|failed to retrieve') { throw 'DEPENDENCY_QUERY_FAILED_OR_FEED_UNAVAILABLE' }
        $data = $query.output | ConvertFrom-Json
        if (-not $data.projects -or @($data.projects).Count -eq 0) { throw 'DEPENDENCY_QUERY_MISSING_PROJECT_RESULTS' }
        if (@($data.logs | Where-Object { $_.level -match '(?i)error' -or $_.message -match '(?i)NU1900|NU1301|failed|unable' }).Count -gt 0) { throw 'DEPENDENCY_QUERY_FAILED_OR_FEED_UNAVAILABLE' }
        # N03: NuGet reports "no advisory data" through problems[]/logs[], never through the package
        # arrays. Ignoring those arrays turned an unavailable advisory database into PASS.
        $diagnosticText = @()
        if ($null -ne $data.problems) { $diagnosticText += @($data.problems | ForEach-Object { [string]$_.text }) }
        if ($null -ne $data.logs) { $diagnosticText += @($data.logs | ForEach-Object { [string]$_.message }) }
        $receipt.diagnostic_entries = $diagnosticText.Count
        $combinedDiagnostics = ($diagnosticText -join "`n")
        if ($combinedDiagnostics -match '(?i)did not provide any vulnerability data|no vulnerability data|vulnerability data (?:is )?(?:not |un)?available|unable to (?:load|retrieve|download|resolve) (?:the )?vulnerability|NU1900') { throw 'VULNERABILITY_DATA_UNAVAILABLE' }
        if ($diagnosticText.Count -gt 0) { throw 'DEPENDENCY_QUERY_PROBLEM_REPORTED' }
        # Positive evidence that an advisory database was consulted. An empty --vulnerable result is
        # indistinguishable from an empty database, so the authority itself must be present, non-empty,
        # schema-valid and (for offline mirrors) fresh. Absence of data is NOT a clean bill of health.
        $authority = Resolve-AdvisoryAuthority -OfflinePath $OfflineDbPath -IndexUrl $AdvisoryIndexUrl -MaxAgeDays $AdvisoryMaxAgeDays -Root $root
        $advisoryItems = $authority.items
        $receipt.advisory_source = $authority.source
        $receipt.advisory_reference = $authority.reference
        if ($authority.sha256) { $receipt.advisory_sha256 = $authority.sha256 }
        $receipt.advisory_index_url = $AdvisoryIndexUrl
        $receipt.advisory_index_item_count = $advisoryItems.Count
        $expectedProjects = @([regex]::Matches([IO.File]::ReadAllText((Join-Path $root 'SewerPilot.sln')), '(?m)^Project\([^\r\n]+"([^"\r\n]+\.csproj)"')).Count
        if ($expectedProjects -eq 0 -or @($data.projects).Count -ne $expectedProjects) { throw 'DEPENDENCY_QUERY_INCOMPLETE_SOLUTION_COVERAGE' }
        $receipt.projects = @($data.projects).Count
        $receipt.expected_projects = $expectedProjects
        # N03: --vulnerable prints only packages that carry an advisory, so a clean result is an EMPTY
        # graph. That is indistinguishable from a query that evaluated nothing. Enumerate the full
        # resolved package graph so the gate can state how many packages were actually evaluated.
        $enumerated = Invoke-DotnetQuery -Arguments 'list SewerPilot.sln package --include-transitive --format json --no-restore' -TimeoutSeconds $TimeoutSeconds -Root $root
        if ($enumerated.exit_code -ne 0 -or $enumerated.error -match '(?i)error|NU1900|unable|failed' -or $enumerated.output -match '(?i)NU1900|NU1301|unable to load|failed to retrieve') { throw 'DEPENDENCY_ENUMERATION_FAILED' }
        $enumData = $enumerated.output | ConvertFrom-Json
        $enumSet = New-Object 'System.Collections.Generic.HashSet[string]'
        foreach ($enumProject in @($enumData.projects)) {
            if ($null -eq $enumProject) { throw 'DEPENDENCY_QUERY_INDETERMINATE_PACKAGE' }
            foreach ($enumFramework in @($enumProject.frameworks)) {
                if ($null -eq $enumFramework) { throw 'DEPENDENCY_QUERY_INDETERMINATE_PACKAGE' }
                # Windows PowerShell 5.1 makes @($null).Count equal to 1, so concatenating
                # @(top) + @(transitive) injects phantom nulls when a property is absent.
                # Accumulate only real collections; a null INSIDE a real array still fails closed.
                $enumPackages = @()
                if ($null -ne $enumFramework.topLevelPackages) { $enumPackages += @($enumFramework.topLevelPackages) }
                if ($null -ne $enumFramework.transitivePackages) { $enumPackages += @($enumFramework.transitivePackages) }
                foreach ($enumPackage in $enumPackages) {
                    # An unresolvable identity is an unverified dependency: it must never be skipped.
                    if ($null -eq $enumPackage -or [string]::IsNullOrWhiteSpace([string]$enumPackage.id)) { throw 'DEPENDENCY_QUERY_INDETERMINATE_PACKAGE' }
                    if ([string]::IsNullOrWhiteSpace([string]$enumPackage.resolvedVersion)) { throw 'DEPENDENCY_QUERY_INDETERMINATE_PACKAGE' }
                    [void]$enumSet.Add(([string]$enumPackage.id) + '/' + [string]$enumPackage.resolvedVersion)
                }
            }
        }
        $receipt.packages_enumerated = $enumSet.Count
        if ($enumSet.Count -le 0) { throw 'DEPENDENCY_QUERY_NO_PACKAGES_INSPECTED' }
        $safeProjects = @()
        foreach ($project in $data.projects) {
            $frameworks = @()
            foreach ($framework in $project.frameworks) {
                $packages = @()
                if ($null -ne $framework.topLevelPackages) { $packages += @($framework.topLevelPackages) }
                if ($null -ne $framework.transitivePackages) { $packages += @($framework.transitivePackages) }
                $safePackages = @()
                foreach ($package in $packages) {
                    # N03: every reported vulnerability must map onto an enumerated package, otherwise the
                    # finding refers to something the gate never verified.
                    if ($null -ne $package -and @($package.vulnerabilities).Count -gt 0) {
                        if (-not $enumSet.Contains(([string]$package.id) + '/' + [string]$package.resolvedVersion)) { throw 'DEPENDENCY_QUERY_INDETERMINATE_PACKAGE' }
                        $receipt.vulnerable_packages++
                    }
                    if ($null -ne $package) {
                        $advisories = @()
                        foreach ($vulnerability in $package.vulnerabilities) {
                            $uri = $null
                            if ([Uri]::TryCreate($vulnerability.advisoryurl, [UriKind]::Absolute, [ref]$uri) -and $uri.Scheme -eq 'https') {
                                $safeUrl = $uri.GetLeftPart([UriPartial]::Path)
                                if ($uri.UserInfo) { $safeUrl = 'https://' + $uri.Host + $uri.AbsolutePath }
                            } else { $safeUrl = '[INVALID_OR_REDACTED_URL]' }
                            $advisories += [ordered]@{ severity=$vulnerability.severity; advisory_url=$safeUrl }
                        }
                        $safePackages += [ordered]@{ id=$package.id; requested_version=$package.requestedVersion; resolved_version=$package.resolvedVersion; vulnerabilities=$advisories }
                    }
                }
                $frameworks += [ordered]@{ framework = $framework.framework; packages=$safePackages; vulnerable_package_count = @($packages | Where-Object { $null -ne $_.vulnerabilities }).Count }
            }
            $safeProjects += [ordered]@{ project = [IO.Path]::GetFileName($project.path); frameworks = $frameworks }
        }
        $receipt.project_results = $safeProjects
        $receipt.query_schema_version = $data.version
        $receipt.query_parameters = '--vulnerable --include-transitive --format json --no-restore'
        $receipt.log_levels = @($data.logs | ForEach-Object { $_.level })
        # NuGet's vulnerable-only JSON omits nonvulnerable frameworks. Preserve the
        # resolved local package inventory separately so zero findings is auditable.
        $inventory = @()
        $resolvedCount = 0
        foreach ($match in [regex]::Matches([IO.File]::ReadAllText((Join-Path $root 'SewerPilot.sln')), '(?m)^Project\([^\r\n]+"([^"\r\n]+\.csproj)"')) {
            $projectPath = Join-Path $root $match.Groups[1].Value
            $assets = Join-Path (Split-Path -Parent $projectPath) 'obj/project.assets.json'
            if (-not (Test-Path -LiteralPath $assets)) { throw 'DEPENDENCY_QUERY_MISSING_RESTORED_ASSETS' }
            $assetsData = Get-Content -LiteralPath $assets -Raw | ConvertFrom-Json
            $libraries = @($assetsData.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Name })
            foreach ($library in $libraries) {
                # Restored library keys are "Name/Version"; anything else is an unverified identity.
                if ([string]::IsNullOrWhiteSpace([string]$library) -or ([string]$library) -notmatch '^[^/]+/[^/]+$') { throw 'DEPENDENCY_QUERY_INDETERMINATE_PACKAGE' }
            }
            $resolvedCount += $libraries.Count
            $inventory += [ordered]@{ project = [IO.Path]::GetFileName($projectPath); assets_sha256 = Get-EvidenceSha256 $assets; resolved_packages = $libraries }
        }
        $receipt.resolved_inventory = $inventory
        $receipt.resolved_package_count = $resolvedCount
        if ($resolvedCount -le 0) { throw 'DEPENDENCY_QUERY_NO_PACKAGES_INSPECTED' }
        if ($receipt.vulnerable_packages -gt 0) { throw 'DEPENDENCY_VULNERABILITIES_FOUND' }
        $receipt.status = 'PASS'
    } catch {
        $receipt.failed_line = $_.InvocationInfo.ScriptLineNumber
        $receipt.exception_type = $_.Exception.GetType().FullName
        $receipt.failure = if ($_.Exception.Message -match '^(?:DEPENDENCY_[A-Z_]+|VULNERABILITY_DATA_[A-Z_]+)$') { $_.Exception.Message } else { 'DEPENDENCY_QUERY_UNAVAILABLE_OR_INVALID_RESPONSE' }
        $receipt.status = if ($receipt.failure -match '^VULNERABILITY_DATA_') { 'NOT_PROVEN' } else { 'BLOCKED' }
        Save-Receipt 'dependency-qualification.json' $receipt
        throw $receipt.failure
    }
    Save-Receipt 'dependency-qualification.json' $receipt
    Write-Output "DEPENDENCY_QUALIFICATION: PASS ($($receipt.projects) projects, $($receipt.packages_enumerated) packages evaluated, $($receipt.resolved_package_count) resolved inventory entries, zero reported vulnerable packages)"
}
if ($Scope -in @('All','Secrets')) {
    $rules = [ordered]@{
        AwsAccessKey = 'AKIA[0-9A-Z]{16}'
        PrivateKey = '-----BEGIN (?:RSA |DSA |EC |OPENSSH |PGP )?PRIVATE KEY-----'
        GithubToken = '(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})'
        OpenAiToken = 'sk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{20,}'
        AssignedCredential = '(?i)(?:password|passwd|pwd|api[_-]?key|client[_-]?secret|access[_-]?token|auth[_-]?token)\s*["'']?\s*[:=]\s*["'']?([A-Za-z0-9+/_.!@$%&*=-]{8,})'
        ConnectionCredential = '(?i)(?:Password|Pwd)\s*=\s*([^;\s"'']{4,})'
        BasicAuthUrl = '(?i)https?://[^/\s:@]+:[^/\s@]{4,}@'
    }
    $paths = @(& git -C $root ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'SECRET_SCAN_FILE_INVENTORY_FAILED' }
    # N03: an empty inventory is not a clean scan. Traversal must be positively demonstrated.
    if (@($paths).Count -eq 0) {
        $blocked = [ordered]@{ timestamp_utc = [DateTime]::UtcNow.ToString('o'); status = 'BLOCKED'; failure = 'SECRET_SCAN_FILE_INVENTORY_EMPTY'; files_scanned = 0; findings = @(); limitation = 'Git file inventory returned no candidate files; an empty inventory cannot clear the gate.' }
        Save-Receipt 'secret-qualification.json' $blocked
        throw 'SECRET_SCAN_FILE_INVENTORY_EMPTY'
    }
    $findings = @()
    $scanned = 0
    foreach ($relative in ($paths | Sort-Object -Unique)) {
        if ($relative -match '(^|/)(\.git|\.agents|\.codex|bin|obj|node_modules|staging|output)(/|$)') { continue }
        $path = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        $file = Get-Item -LiteralPath $path
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'SECRET_SCAN_REPARSE_FILE_REJECTED' }
        if ($file.Extension -notin @('.cs','.py','.js','.ts','.jsx','.tsx','.html','.css','.sql','.sh','.cmd','.bat','.json','.xml','.inp','.iss','.ps1','.md','.txt','.props','.config','.yaml','.yml','.toml','.ini','.env','.csv','.targets','.csproj','.sln','.pem','.key') -and $file.Name -notlike '.env*') { continue }
        $scanned++
        $content = [IO.File]::ReadAllText($path)
            foreach ($rule in $rules.GetEnumerator()) {
                $matches = [regex]::Matches($content, $rule.Value)
                foreach ($match in $matches) {
                    if ($rule.Key -in @('AssignedCredential','ConnectionCredential')) {
                        $value = $match.Groups[1].Value
                        # This unit test generates a random password at runtime; it
                        # contains no credential literal. Do not exempt its file.
                        if ($relative -eq 'tests/SewerPilot.OpenFlows.Adapter.Tests/EngineeringIdentityTests.cs' -and $value -match '^Guid\.NewGuid(?:\(\)\.ToString\()?\s*$') { continue }
                        if ($value -match '(?i)^(?:your[_-]|example|placeholder|changeme|redacted|not[_-]?(?:a[_-]?)?real|dummy|test[_-]|null|false|true|Environment|options\.|result\.|[A-Z_]+_HERE|\$|\{)') { continue }
                    }
                    $lineNumber = 1 + [regex]::Matches($content.Substring(0, $match.Index), "`n").Count
                    $findings += [ordered]@{ file = $relative; line = $lineNumber; rule = $rule.Key; value = '[REDACTED]' }
                }
            }
    }
    # N03: zero traversed files means the scan proved nothing; it must not clear the gate.
    if ($scanned -le 0) {
        $blocked = [ordered]@{ timestamp_utc = [DateTime]::UtcNow.ToString('o'); status = 'BLOCKED'; failure = 'SECRET_SCAN_NO_FILES_AUDITED'; files_scanned = 0; excluded = @('.git','.agents','.codex','bin','obj','node_modules','distribution/staging','distribution/output','nontext files','Git-ignored files'); rules = @($rules.Keys); findings = @(); limitation = 'No eligible text file was traversed; a zero-file scan cannot clear the gate.' }
        Save-Receipt 'secret-qualification.json' $blocked
        throw 'SECRET_SCAN_NO_FILES_AUDITED'
    }
    $receipt = [ordered]@{ timestamp_utc = [DateTime]::UtcNow.ToString('o'); status = $(if ($findings.Count) { 'FAIL' } else { 'PASS' }); files_scanned = $scanned; excluded = @('.git','.agents','.codex','bin','obj','node_modules','distribution/staging','distribution/output','nontext files','Git-ignored files'); rules = @($rules.Keys); findings = $findings; limitation = 'Pattern-based source/config scan; absence of matches is not a universal no-secrets guarantee.' }
    Save-Receipt 'secret-qualification.json' $receipt
    if ($findings.Count) { throw "SECRET_SCAN_FAILED: $($findings.Count) redacted findings; inspect receipt paths/rules." }
    Write-Output "SECRET_QUALIFICATION: PASS ($scanned text files, zero findings)"
}
