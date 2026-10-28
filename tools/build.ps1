# Authored with LLM assistance, changes must be reviewed and owned by a human.
# Initial version reviewed and owned by @kaldigo
[CmdletBinding()]
param([string]$Version='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $references = if ($env:DALAMUD_HOME) { $env:DALAMUD_HOME } else { Join-Path $env:APPDATA 'XIVLauncher/addon/Hooks/dev' }
    $arguments=@('build','Questionable/Questionable.csproj','-c','Release',"-p:DalamudLibPath=$references/",'-p:UseSharedCompilation=false','--nologo')
    if ($Version) { $arguments += "-p:Version=$Version" }
    dotnet @arguments
    if ($LASTEXITCODE) { throw 'Plugin build failed' }
    if ($Version -and [Reflection.AssemblyName]::GetAssemblyName((Join-Path $root 'Questionable/dist/Questionable.dll')).Version -ne [version]$Version) { throw 'DLL version differs from release plan' }
    dotnet run --project tests/RebornChecks -c Release
    if ($LASTEXITCODE) { throw 'Reborn contract tests failed' }
    python -m unittest discover -s tests -p 'test_*.py' -v
    if ($LASTEXITCODE) { throw 'Fork and update policy checks failed' }
    dotnet test QuestPaths.JsonValidator/QuestPaths.JsonValidator.csproj -c Release
    if ($LASTEXITCODE) { throw 'Upstream quest route schema validation failed' }
    # Validate a package during update checks as well as during release publication.
    $feedPath = Join-Path $root 'pluginmaster.json'
    $originalFeed = [System.IO.File]::ReadAllBytes($feedPath)
    try {
        $builtVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $root 'Questionable/dist/Questionable.dll')).Version.ToString()
        python tools/package_release.py --tag "v$builtVersion"
        if ($LASTEXITCODE) { throw 'Package assembly failed' }
        python tools/check_package.py --tag "v$builtVersion"
        if ($LASTEXITCODE) { throw 'Package validation failed' }
    } finally { [System.IO.File]::WriteAllBytes($feedPath, $originalFeed) }
} finally { Pop-Location }
