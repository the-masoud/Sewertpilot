#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$verifyRepository = Split-Path -Parent $PSScriptRoot
$findings = [System.Collections.Generic.List[string]]::new()
$required = @('AGENTS.md', 'README.md', '.env.example', '.gitignore', 'Directory.Build.props',
    'build/BuildConfiguration.json', 'build/README.md', 'tools/env.ps1', 'tools/build.ps1',
    'tools/test.ps1', 'tools/verify.ps1', 'SewerPilot.sln',
    'src/SewerPilot.OpenFlows.Contracts/SewerPilot.OpenFlows.Contracts.csproj',
    'tests/SewerPilot.OpenFlows.Contracts.Tests/SewerPilot.OpenFlows.Contracts.Tests.csproj',
    'docs/architecture/P2_OPENFLOWS_CONTRACTS.md',
    'src/SewerPilot.OpenFlows.Compatibility/SewerPilot.OpenFlows.Compatibility.csproj',
    'tests/SewerPilot.OpenFlows.Compatibility.Tests/SewerPilot.OpenFlows.Compatibility.Tests.csproj',
    'tests/SewerPilot.OpenFlows.Compatibility.Tests/Program.cs',
    'tools/probes/OpenFlowsRuntimeBindingAcceptance/OpenFlowsRuntimeBindingAcceptance.csproj',
    'tools/probes/OpenFlowsRuntimeBindingAcceptance/Program.cs',
    'docs/architecture/P3_OPENFLOWS_RUNTIME_COMPATIBILITY.md',
    'src/SewerPilot.OpenFlows.Adapter/SewerPilot.OpenFlows.Adapter.csproj',
    'src/SewerPilot.OpenFlows.RuntimeHost/SewerPilot.OpenFlows.RuntimeHost.csproj',
    'tests/SewerPilot.OpenFlows.Adapter.Tests/SewerPilot.OpenFlows.Adapter.Tests.csproj',
    'tools/probes/OpenFlowsReadOnlyModelAcceptance/OpenFlowsReadOnlyModelAcceptance.csproj',
    'docs/architecture/P4_READ_ONLY_MODEL_ACCESS.md')

foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $verifyRepository $relative) -PathType Leaf)) {
        $findings.Add("Missing required file: $relative")
    }
}

try {
    . (Join-Path $PSScriptRoot 'build.ps1')
    $settings = Get-SewerPilotBuildConfiguration -RepositoryRoot $verifyRepository
    $null = Get-SewerPilotConfigurationName -Settings $settings -Requested $settings.defaultConfiguration
    if ($settings.solution -cne 'SewerPilot.sln' -or $settings.allowNoSolution) {
        throw 'P2 requires SewerPilot.sln and allowNoSolution=false.'
    }
    $solutionPath = Resolve-SewerPilotRepositoryPath $verifyRepository $settings.solution
    if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) { throw 'Configured solution is missing.' }
}
catch { $findings.Add('Build configuration invalid: ' + $_.Exception.Message) }

try {
    . (Join-Path $PSScriptRoot 'env.ps1')
    $null = Get-SewerPilotEnvironment -RepositoryRoot $verifyRepository
}
catch { $findings.Add('Environment input invalid: ' + $_.Exception.Message) }

try {
    $xmlSettings = [System.Xml.XmlReaderSettings]::new()
    $xmlSettings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $xmlSettings.XmlResolver = $null
    $xmlReader = $null
    try {
        $xmlReader = [System.Xml.XmlReader]::Create((Join-Path $verifyRepository 'Directory.Build.props'), $xmlSettings)
        $props = [System.Xml.XmlDocument]::new()
        $props.XmlResolver = $null
        $props.Load($xmlReader)
    }
    finally { if ($xmlReader) { $xmlReader.Dispose() } }
    if ($props.DocumentElement.Name -ne 'Project') { throw 'Expected Project root.' }
    $groups = @($props.DocumentElement.ChildNodes | Where-Object { $_ -is [System.Xml.XmlElement] })
    if ($groups.Count -ne 1 -or $groups[0].Name -ne 'PropertyGroup') { throw 'Only one common PropertyGroup is permitted.' }
    $properties = @($groups[0].ChildNodes | Where-Object { $_ -is [System.Xml.XmlElement] })
    if ($properties.Count -ne 2 -or @($properties.Name) -cnotcontains 'Deterministic' -or
        @($properties.Name) -cnotcontains 'ContinuousIntegrationBuild') { throw 'Only deterministic and conditional CI properties are permitted.' }
    if ($props.Project.PropertyGroup.Deterministic -cne 'true' -or
        $props.Project.PropertyGroup.ContinuousIntegrationBuild.InnerText -cne 'true' -or
        $props.Project.PropertyGroup.ContinuousIntegrationBuild.Condition -cne "'`$(CI)' == 'true' Or '`$(TF_BUILD)' == 'True'") {
        throw 'Unexpected common build policy.'
    }
}
catch { $findings.Add('Directory.Build.props invalid or contains unapproved build policy.') }

try {
    $gitCommand = Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $gitExe = $gitCommand.Source
    # Ownership exception applies to these read-only commands, never global Git config.
    $gitOptions = @('-c', "safe.directory=$verifyRepository", '-C', $verifyRepository)
    $top = & $gitExe @gitOptions rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or [System.IO.Path]::GetFullPath($top) -ne [System.IO.Path]::GetFullPath($verifyRepository)) {
        throw 'Resolved directory is not the expected Git repository.'
    }
    $ignoredOverrides = @(& $gitExe @gitOptions check-ignore --no-index -- .env.local .env.development.local)
    if ($LASTEXITCODE -ne 0 -or $ignoredOverrides.Count -ne 2) {
        $findings.Add('Both local environment override patterns must be ignored by Git.')
    }
    $null = & $gitExe @gitOptions check-ignore --no-index -q -- .env.example
    if ($LASTEXITCODE -ne 1) { $findings.Add('.env.example must remain trackable.') }
    $ignoredBuildFiles = @(& $gitExe @gitOptions check-ignore --no-index -- build/BuildConfiguration.json build/README.md)
    if ($LASTEXITCODE -ne 1 -or $ignoredBuildFiles.Count -ne 0) {
        $findings.Add('Root build foundation files must remain trackable.')
    }
    $historicalPaths = @(& $gitExe @gitOptions ls-files -- docs/evidence/p0 'docs/handoffs/*P0*')
    if ($LASTEXITCODE -ne 0 -or $historicalPaths.Count -eq 0 -or
        @($historicalPaths | Where-Object { $_ -like 'docs/evidence/p0/*' }).Count -eq 0 -or
        @($historicalPaths | Where-Object { $_ -like 'docs/handoffs/*' }).Count -eq 0) {
        throw 'Tracked historical P0 evidence and handoff must remain present.'
    }
    foreach ($relative in $historicalPaths) {
        if (-not (Test-Path -LiteralPath (Join-Path $verifyRepository $relative) -PathType Leaf)) {
            $findings.Add("Historical file missing: $relative")
        }
    }
    $null = & $gitExe @gitOptions diff --quiet HEAD -- docs/evidence/p0 'docs/handoffs/*P0*'
    if ($LASTEXITCODE -ne 0) { $findings.Add('Historical P0 evidence/handoff has uncommitted changes.') }
    $null = & $gitExe @gitOptions diff --quiet HEAD -- docs/evidence/p1 docs/compatibility/P1_OPENFLOWS_RUNTIME_BASELINE.md tools/probes/WaterObjectsCompatibilityProbe
    if ($LASTEXITCODE -ne 0) { $findings.Add('Historical P1 evidence/probe has uncommitted changes.') }
    $trackedBinaries = @(& $gitExe @gitOptions ls-files -- src tests tools/probes | Where-Object { $_ -match '(?i)\.(dll|exe|nupkg)$' })
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect tracked source/test binaries.' }
    if ($trackedBinaries.Count -gt 0) { $findings.Add('Binaries must not be tracked under src/tests.') }
    # Git excludes generated/ignored output, while including new P2 source before staging.
    $sourcePaths = @(& $gitExe @gitOptions ls-files --cached --others --exclude-standard -- src tests tools/probes build | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source/test boundary files.' }
}
catch { $findings.Add('Git foundation verification failed: ' + $_.Exception.Message) }

try {
    $solutionText = [System.IO.File]::ReadAllText((Join-Path $verifyRepository 'SewerPilot.sln'))
    $projects = @([regex]::Matches($solutionText, '(?m)^Project\("[^"\r\n]+"\) = "[^"\r\n]+", "([^"\r\n]+\.csproj)", "[^"\r\n]+"'))
    if ($projects.Count -lt 2) { throw 'Solution must include contracts and contract verification projects.' }
    $projectPaths = @()
    foreach ($project in $projects) {
        $relative = $project.Groups[1].Value.Replace('\', '/')
        $path = Resolve-SewerPilotRepositoryPath $verifyRepository $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'A solution project is missing.' }
        $projectPaths += $relative
        $reader = $null
        try {
            $reader = [System.Xml.XmlReader]::Create($path, $xmlSettings)
            $projectXml = [System.Xml.XmlDocument]::new()
            $projectXml.XmlResolver = $null
            $projectXml.Load($reader)
        }
        finally { if ($reader) { $reader.Dispose() } }
        foreach ($reference in $projectXml.SelectNodes("//*[local-name()='ProjectReference']")) {
            $resolved = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $path) $reference.GetAttribute('Include')))
            $rootPrefix = [System.IO.Path]::GetFullPath($verifyRepository).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
            if (-not $resolved.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Project reference leaves the repository.' }
            $referencePath = Resolve-SewerPilotRepositoryPath $verifyRepository $resolved.Substring($rootPrefix.Length)
            if (-not (Test-Path -LiteralPath $referencePath -PathType Leaf)) { throw 'A project reference is missing.' }
        }
        if ($relative -ceq 'src/SewerPilot.OpenFlows.Contracts/SewerPilot.OpenFlows.Contracts.csproj') {
            $targets = @($projectXml.SelectNodes("//*[local-name()='TargetFramework']"))
            if ($targets.Count -ne 1 -or $targets[0].InnerText -cne 'netstandard2.0') { throw 'Contracts must target only netstandard2.0.' }
            if ($projectXml.SelectNodes("//*[local-name()='Reference' or local-name()='PackageReference' or local-name()='HintPath' or local-name()='ProjectReference']").Count -ne 0) {
                throw 'Contracts must not reference packages, external assemblies or other projects.'
            }
        }
        if ($relative -ceq 'tests/SewerPilot.OpenFlows.Compatibility.Tests/SewerPilot.OpenFlows.Compatibility.Tests.csproj' -or
            $relative -ceq 'tools/probes/OpenFlowsRuntimeBindingAcceptance/OpenFlowsRuntimeBindingAcceptance.csproj' -or
            $relative -ceq 'src/SewerPilot.OpenFlows.Adapter/SewerPilot.OpenFlows.Adapter.csproj' -or
            $relative -ceq 'src/SewerPilot.OpenFlows.RuntimeHost/SewerPilot.OpenFlows.RuntimeHost.csproj' -or
            $relative -ceq 'tests/SewerPilot.OpenFlows.Adapter.Tests/SewerPilot.OpenFlows.Adapter.Tests.csproj' -or
            $relative -ceq 'tools/probes/OpenFlowsReadOnlyModelAcceptance/OpenFlowsReadOnlyModelAcceptance.csproj') {
            $targets = @($projectXml.SelectNodes("//*[local-name()='TargetFramework']"))
            $platforms = @($projectXml.SelectNodes("//*[local-name()='PlatformTarget']"))
            if ($targets.Count -ne 1 -or $targets[0].InnerText -cne 'net48' -or
                $platforms.Count -ne 1 -or $platforms[0].InnerText -cne 'x64') {
                throw 'P3/P4 runtime projects and harnesses must target net48/x64.'
            }
        }
        if ($relative -ceq 'src/SewerPilot.OpenFlows.Compatibility/SewerPilot.OpenFlows.Compatibility.csproj') {
            $targets = @($projectXml.SelectNodes("//*[local-name()='TargetFramework']"))
            if ($targets.Count -ne 1 -or $targets[0].InnerText -cne 'net48') { throw 'Compatibility must target only net48.' }
            $platforms = @($projectXml.SelectNodes("//*[local-name()='PlatformTarget']"))
            if ($platforms.Count -ne 1 -or $platforms[0].InnerText -cne 'x64') { throw 'Compatibility must target x64.' }
            if ($projectXml.SelectNodes("//*[local-name()='Reference' or local-name()='PackageReference' or local-name()='HintPath']").Count -ne 0) {
                throw 'Compatibility must use runtime binding and cannot pin assemblies or packages.'
            }
            $projectReferences = @($projectXml.SelectNodes("//*[local-name()='ProjectReference']"))
            if ($projectReferences.Count -ne 1 -or $projectReferences[0].GetAttribute('Include') -notmatch 'SewerPilot\.OpenFlows\.Contracts\.csproj$') {
                throw 'Compatibility must reference only SewerPilot.OpenFlows.Contracts.'
            }
        }
    }
    if ($projects.Count -ne 9) { throw 'The P4 solution must contain three libraries, three test harnesses, two acceptance runners and one production RuntimeHost.' }
    foreach ($expected in @('src/SewerPilot.OpenFlows.Contracts/SewerPilot.OpenFlows.Contracts.csproj',
        'tests/SewerPilot.OpenFlows.Contracts.Tests/SewerPilot.OpenFlows.Contracts.Tests.csproj',
        'src/SewerPilot.OpenFlows.Compatibility/SewerPilot.OpenFlows.Compatibility.csproj',
        'tests/SewerPilot.OpenFlows.Compatibility.Tests/SewerPilot.OpenFlows.Compatibility.Tests.csproj',
        'tools/probes/OpenFlowsRuntimeBindingAcceptance/OpenFlowsRuntimeBindingAcceptance.csproj',
        'src/SewerPilot.OpenFlows.Adapter/SewerPilot.OpenFlows.Adapter.csproj',
        'src/SewerPilot.OpenFlows.RuntimeHost/SewerPilot.OpenFlows.RuntimeHost.csproj',
        'tests/SewerPilot.OpenFlows.Adapter.Tests/SewerPilot.OpenFlows.Adapter.Tests.csproj',
        'tools/probes/OpenFlowsReadOnlyModelAcceptance/OpenFlowsReadOnlyModelAcceptance.csproj')) {
        if ($projectPaths -cnotcontains $expected) { throw 'Required project is absent from the solution.' }
    }
}
catch { $findings.Add('Solution/contracts boundary invalid: ' + $_.Exception.Message) }

if (Test-Path -LiteralPath (Join-Path $verifyRepository '.pi/settings.json')) {
    $findings.Add('Obsolete active Pi agent settings are present.')
}
if (Test-Path -LiteralPath (Join-Path $verifyRepository '.vscode/settings.json')) {
    try {
        $editorSettings = [System.IO.File]::ReadAllText((Join-Path $verifyRepository '.vscode/settings.json'))
        if ($editorSettings -match '(?i)ATRIA|PI_PROVIDER|PI_MODEL') {
            $findings.Add('Editor settings contain active legacy agent injection.')
        }
    }
    catch { $findings.Add('Cannot inspect active editor settings.') }
}

$secretPatterns = @(
    '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
    '\bsk-[A-Za-z0-9_-]{20,}', '\bgh[pousr]_[A-Za-z0-9]{20,}',
    '\bAIza[A-Za-z0-9_-]{30,}',
    '\beyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{15,}',
    '(?i)(?:api[_-]?key|password|secret|access[_-]?token)\s*[:=]\s*[\x22\x27]?[A-Za-z0-9/+_-]{24,}'
)
$bindingPatterns = @(
    '(?i)Program Files[\\/]+Bentley',
    '(?i)[A-Z]:[\\/][^\r\n]*?(?:Bentley|SewerGEMS|OpenFlows)',
    '(?i)SEWERPILOT_[A-Z0-9_]*(?:VERSION|REQUIRED_BUILD|\d{4}_PATH)\s*=',
    '(?i)(?:SewerGEMS|OpenFlows)[^\r\n]{0,60}(?:version|year|build)\s*(?:==|-eq|-ge|-gt|>=|<=)'
)
foreach ($relative in $required) {
    $path = Join-Path $verifyRepository $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    try { $content = [System.IO.File]::ReadAllText($path) }
    catch { $findings.Add("Cannot inspect foundation file: $relative"); continue }
    foreach ($pattern in $secretPatterns) {
        if ($content -match $pattern) { $findings.Add("Secret-like material in $relative (value suppressed)."); break }
    }
    foreach ($pattern in $bindingPatterns) {
        if ($content -match $pattern) { $findings.Add("Forbidden vendor binding in $relative (value suppressed)."); break }
    }
    if ($content -match '(?im)(?:PRIMARY|defaultModel)\s*[:=]\s*[\x60\x22\x27 ]*Atria') {
        $findings.Add("Atria must not be primary in $relative.")
    }
}

foreach ($relative in $sourcePaths) {
    if ($relative -notmatch '\.(cs|csproj|props|targets|ps1|json|config)$') { continue }
    $path = Join-Path $verifyRepository $relative
    try { $content = [System.IO.File]::ReadAllText($path) }
    catch { $findings.Add("Cannot inspect boundary file: $relative"); continue }
    # P2's vendor-neutral source guarantee applies to its Contracts assembly only;
    # the P3 compatibility assembly identifies vendor types by simple-name strings at runtime.
    if ($relative -like 'src/SewerPilot.OpenFlows.Contracts/*' -and
        $content -match '(?i)Haestad|Bentley|WaterObjects(?!Present\b)|Program Files') {
        $findings.Add("Vendor reference/path in vendor-neutral contracts: $relative")
    }
    foreach ($pattern in $secretPatterns) {
        if ($content -match $pattern) { $findings.Add("Secret-like material in $relative (value suppressed)."); break }
    }
    if ($relative -like 'src/SewerPilot.OpenFlows.Contracts/*') {
        if ($content -match '(?i)Microsoft\.Win32|System\.Reflection|Assembly\.Load|DllImport|HintPath') {
            $findings.Add("Runtime binding/discovery is forbidden in contracts: $relative")
        }
    }
    if ($relative -match '\.(?:cs|csproj|props|targets|ps1|json|config)$' -and
        $content -match '(?i)[A-Z]:[\\/](?:Program Files|Bentley|OpenFlows)[^\r\n\x22\x27]*') {
        $findings.Add("Fixed local runtime path in active source: $relative")
    }
}

# Product version/build text is diagnostic metadata throughout the runtime model,
# tests and recorded probes. Inspect executable selection/adapter decision code only.
$decisionFiles = @($sourcePaths | Where-Object {
    $_ -like 'src/SewerPilot.OpenFlows.Compatibility/*.cs' -or $_ -like 'src/SewerPilot.OpenFlows.Adapter/*.cs' -or $_ -like 'src/SewerPilot.OpenFlows.RuntimeHost/*.cs'
})
$decisionPatterns = @(
    '(?is)\b(?:if|switch)\s*\([^)]*\b(?:ProductVersion|FileVersion|AssemblyVersion|ProductBuild|BuildMetadata|VersionMetadata|version|build|year)\b',
    '(?i)\b(?:CompareTo|Compare|Version\.Parse)\s*\([^\r\n)]*\b(?:ProductVersion|FileVersion|AssemblyVersion|ProductBuild|BuildMetadata|VersionMetadata|version|build|year)\b',
    '(?i)\b(?:OrderBy|ThenBy|MaxBy|MinBy)\w*\s*\([^\r\n)]*\b(?:ProductVersion|FileVersion|AssemblyVersion|ProductBuild|BuildMetadata|VersionMetadata)\b',
    '(?i)SupportsVersion|IsBuildAtLeast|Use\d{4}Adapter|\b(?:SewerGems|OpenFlows)\w*20\d{2}\w*(?:Adapter|Runtime)\b|\b20\d{2}Adapter\b'
)
foreach ($relative in $decisionFiles) {
    $path = Join-Path $verifyRepository $relative
    try { $decisionText = [System.IO.File]::ReadAllText($path) }
    catch { $findings.Add("Cannot inspect compatibility decision file: $relative"); continue }
    # CLR process eligibility is a runtime capability check, not product-version ranking.
    $decisionText = $decisionText -replace '(?i)Environment\.Version', 'Environment.RuntimeVersion'
    foreach ($pattern in $decisionPatterns) {
        if ($decisionText -match $pattern) {
            $findings.Add("Version/build drives compatibility decisions in $relative (match suppressed).")
            break
        }
    }
}

$compatibilityProject = Join-Path $verifyRepository 'src/SewerPilot.OpenFlows.Compatibility/SewerPilot.OpenFlows.Compatibility.csproj'
if (Test-Path -LiteralPath $compatibilityProject -PathType Leaf) {
    try { $compatibilityProjectText = [System.IO.File]::ReadAllText($compatibilityProject) }
    catch { $findings.Add('Cannot inspect the active compatibility project file.') }
    if ($compatibilityProjectText -match '(?is)<HintPath\b[^>]*>[^<]*(?:Bentley|Haestad|WaterObjects)|<Reference\b[^>]*Include\s*=\s*[\x22\x27][^\x22\x27]*(?:Bentley|Haestad|WaterObjects)') {
        $findings.Add('Production compatibility project contains a vendor HintPath/reference.')
    }
}

$activeProductionText = @()
foreach ($relative in @('src/SewerPilot.OpenFlows.Contracts/SewerPilot.OpenFlows.Contracts.csproj',
    'src/SewerPilot.OpenFlows.Compatibility/SewerPilot.OpenFlows.Compatibility.csproj',
    'src/SewerPilot.OpenFlows.Adapter/SewerPilot.OpenFlows.Adapter.csproj')) {
    $path = Join-Path $verifyRepository $relative
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $activeProductionText += [System.IO.File]::ReadAllText($path)
    }
}
if ($activeProductionText -join "`n" -match '(?i)<HintPath\b[^>]*>[^<]*(?:Bentley|Haestad|WaterObjects)|<Reference\b[^>]*Include\s*=\s*[\x22\x27][^\x22\x27]*(?:Bentley|Haestad|WaterObjects)') {
    $findings.Add('A production project contains a vendor HintPath/reference.')
}
foreach ($relative in @('AGENTS.md', 'README.md', 'build/README.md')) {
    $path = Join-Path $verifyRepository $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    $policy = [System.IO.File]::ReadAllText($path)
    if ($policy -notmatch 'Atria-Dawn-Preview' -or $policy -notmatch '(?i)second review' -or
        $policy -notmatch '(?i)advisory(?: only)?\s*/\s*non-blocking') {
        $findings.Add("Explicit advisory second-review policy missing in $relative.")
    }
}


# Bounded P4 production checks. Reflection inspects owned exported metadata only;
# no reader operation or vendor lifecycle is executed by verification.
try {
    $adapterPath = Join-Path $verifyRepository 'src/SewerPilot.OpenFlows.Adapter/SewerPilot.OpenFlows.Adapter.csproj'
    [xml]$adapterProject = [System.IO.File]::ReadAllText($adapterPath)
    $refs = @($adapterProject.Project.ItemGroup.ProjectReference | Where-Object { $_ })
    if ($refs.Count -ne 2 -or @($refs.Include | Where-Object { $_ -match 'SewerPilot\.OpenFlows\.(Contracts|Compatibility)\.csproj$' }).Count -ne 2) {
        throw 'Adapter must reference only Contracts and Compatibility projects.'
    }
    if ($adapterProject.SelectNodes("//*[local-name()='HintPath' or local-name()='PackageReference']").Count -ne 0) {
        throw 'Adapter cannot pin packages or external assembly paths.'
    }
    foreach ($reference in $adapterProject.SelectNodes("//*[local-name()='Reference']")) {
        if (@('System.Windows.Forms', 'System.Web.Extensions') -cnotcontains $reference.GetAttribute('Include')) { throw 'Unapproved compile-time Adapter reference.' }
    }
    [xml]$hostProject = [System.IO.File]::ReadAllText((Join-Path $verifyRepository 'src/SewerPilot.OpenFlows.RuntimeHost/SewerPilot.OpenFlows.RuntimeHost.csproj'))
    $hostRefs = @($hostProject.Project.ItemGroup.ProjectReference | Where-Object { $_ })
    if ($hostRefs.Count -ne 1 -or $hostRefs[0].Include -notmatch 'SewerPilot\.OpenFlows\.Adapter\.csproj$' -or
        $hostProject.SelectNodes("//*[local-name()='HintPath' or local-name()='PackageReference']").Count -ne 0 -or
        @($hostProject.SelectNodes("//*[local-name()='Reference']") | Where-Object { $_.GetAttribute('Include') -cne 'System.Web.Extensions' }).Count -ne 0) {
        throw 'RuntimeHost must reference only the production Adapter and framework JSON serializer.'
    }
    $adapterSources = @($sourcePaths | Where-Object { $_ -like 'src/SewerPilot.OpenFlows.Adapter/*.cs' -or $_ -like 'src/SewerPilot.OpenFlows.RuntimeHost/*.cs' })
    $adapterText = ($adapterSources | ForEach-Object { [System.IO.File]::ReadAllText((Join-Path $verifyRepository $_)) }) -join "`n"
    if ($adapterText -match 'AppDomain\.(CreateDomain|Unload)\s*\(') { throw 'Secondary AppDomain exists in the production P4 path.' }
    $readerText = [System.IO.File]::ReadAllText((Join-Path $verifyRepository 'src/SewerPilot.OpenFlows.Adapter/OpenFlowsModelReader.cs'))
    if ($readerText -notmatch 'new MetadataWorker\(\)\.Inspect' -or $readerText -match 'new OpenFlowsAssemblyBinder\(') {
        throw 'P4 must bypass the historical secondary-domain P3 wrappers.'
    }
    if ($adapterText -match '(?i)(?:[\x22\x27](?:Save|SaveAs|SaveCurrentProject|Commit|ApplyChanges|Compute|RunSolver|ComputeCurrentScenario|ScenarioManager|AlternativeManager)[\x22\x27]|\.(?:Save|SaveAs|SaveCurrentProject|Commit|ApplyChanges|Compute|RunSolver|ComputeCurrentScenario|SetValue)\s*\()') {
        throw 'Executable write/create/solver member exists in the P4 path.'
    }
    if ($adapterText -match '(?i)(?:DomainElementManager\s*\([^\r\n]*\b3\b|ConduitElementManager\s*=\s*3|[\x22\x27]ConduitElementManager[\x22\x27]\s*,\s*3)') {
        throw 'Conduit selection depends on a recorded numeric enum value.'
    }
    if ($adapterText -notmatch 'EnumMember\(elementType, "ConduitElementManager"\)' -or
        $adapterText -notmatch 'evidence\.Accepted\s*\?\s*CapabilityState\.Supported' -or
        $adapterText -notmatch 'CapabilityEvidenceKind\.ModelRead') { throw 'Named enum/explicit ModelRead evidence requirement is absent.' }
    $gate = [System.IO.File]::ReadAllText((Join-Path $verifyRepository 'src/SewerPilot.OpenFlows.Adapter/ModelReadResult.cs'))
    foreach ($condition in @('RuntimeSelected', 'AssembliesBound', 'LifecycleInitialized', 'OpenSucceeded',
        'CurrentGraphicalProjectAvailable', 'DomainProjectAvailable', 'DomainDataSetAvailable',
        'FirstConduitElementID.HasValue', 'CloseCompleted', 'SourceIntegrity', 'WorkingIntegrity',
        'SaveWriteInvocations.Count == 0', 'FailureCode == null', 'RuntimeHostResultValid', 'RuntimeHostResultCommitted',
        'RuntimeHostStaCompleted', 'RuntimeHostProcessTerminated', 'RuntimeHostExitCode', 'RuntimeHostWatchdogTermination')) {
        if (-not $gate.Substring($gate.IndexOf('public bool Accepted =>')).Contains($condition)) { throw 'Acceptance gate is incomplete.' }
    }
    $probe = [System.IO.File]::ReadAllText((Join-Path $verifyRepository 'tools/probes/OpenFlowsReadOnlyModelAcceptance/Program.cs'))
    if ($probe -match 'Assembly\.Load|NewSewerGEMSApplicationDescription|OpenFileWithSpecifiedExtension' -or
        $probe -notmatch 'reader\.ReadWithEvidence') { throw 'Acceptance runner must delegate the lifecycle to the Adapter.' }
    $dll = @('bin/Release/net48/SewerPilot.OpenFlows.Adapter.dll', 'bin/x64/Release/net48/SewerPilot.OpenFlows.Adapter.dll') |
        ForEach-Object { Join-Path (Split-Path -Parent $adapterPath) $_ } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $dll) { throw 'Build the Release Adapter before metadata verification.' }
    $assembly = [System.Reflection.Assembly]::LoadFrom($dll)
    function Test-P4NeutralType([Type]$Type) {
        if ($null -eq $Type) { return $true }
        if ($Type.HasElementType) { return Test-P4NeutralType $Type.GetElementType() }
        if ($Type.FullName -match '^(Haestad\.|Bentley\.|System\.Windows\.Forms\.)') { return $false }
        if ($Type.IsGenericType) {
            foreach ($argument in $Type.GetGenericArguments()) { if (-not (Test-P4NeutralType $argument)) { return $false } }
        }
        return $true
    }
    foreach ($type in $assembly.GetExportedTypes()) {
        $types = @($type, $type.BaseType) + @($type.GetInterfaces())
        foreach ($method in $type.GetMethods()) {
            $types += $method.ReturnType
            $types += @($method.GetParameters() | ForEach-Object { $_.ParameterType })
        }
        foreach ($constructor in $type.GetConstructors()) { $types += @($constructor.GetParameters() | ForEach-Object { $_.ParameterType }) }
        foreach ($property in $type.GetProperties()) {
            $types += $property.PropertyType
            $types += @($property.GetIndexParameters() | ForEach-Object { $_.ParameterType })
        }
        $types += @($type.GetFields() | ForEach-Object { $_.FieldType })
        $types += @($type.GetEvents() | ForEach-Object { $_.EventHandlerType })
        foreach ($exposed in $types) { if (-not (Test-P4NeutralType $exposed)) { throw 'Public Adapter API exposes a vendor/UI type.' } }
    }
    if (@($assembly.GetReferencedAssemblies() | Where-Object { $_.Name -match '^(Haestad\.|Bentley\.)' }).Count) { throw 'Compiled Adapter references a vendor assembly.' }
    $hostAssembly = [System.Reflection.Assembly]::LoadFrom((Join-Path (Split-Path -Parent $dll) 'SewerPilot.OpenFlows.RuntimeHost.exe'))
    if ($hostAssembly.GetExportedTypes().Length -ne 0 -or
        @($hostAssembly.GetReferencedAssemblies() | Where-Object { $_.Name -match '^(Haestad\.|Bentley\.)' }).Count) {
        throw 'RuntimeHost exports a public type or references vendor assemblies.'
    }
}
catch { $findings.Add('P4 Adapter boundary invalid: ' + $_.Exception.Message) }

if ($findings.Count -gt 0) {
    Write-Output 'VERIFY_STATUS: FAIL'
    foreach ($finding in $findings) { Write-Output ('FINDING: ' + $finding) }
    exit 1
}
Write-Output 'VERIFY_STATUS: PASS'
Write-Output 'FOUNDATION_FILES: PRESENT'
Write-Output 'HISTORICAL_P0: PRESERVED'
Write-Output 'HISTORICAL_P1: PRESERVED'
Write-Output 'CONTRACT_BOUNDARY: PASS'
Write-Output 'COMPATIBILITY_BOUNDARY: PASS'
Write-Output 'ADAPTER_BOUNDARY: PASS'
Write-Output 'PRODUCTION_APPDOMAIN_CREATE: 0'
Write-Output 'PRODUCTION_APPDOMAIN_UNLOAD: 0'
Write-Output 'PUBLIC_VENDOR_TYPES: 0'
Write-Output 'VENDOR_BINARIES_TRACKED: 0'
exit 0
