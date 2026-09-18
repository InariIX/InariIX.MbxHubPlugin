#Requires -Version 5.1
<#
.SYNOPSIS
    Builds MacroDeck.MbxHubPlugin and packs it into a .macroDeckPlugin artifact.

.DESCRIPTION
    Mirrors the workflow documented in the Macro-Deck-Sample-Plugins README:
      1. dotnet restore / dotnet build  - fast compile check, same as CI's first step.
      2. macrodeck-plugin build         - framework-dependent publish for every RID in
                                           macrodeck-build.json, packed into ./artifacts.
      3. macrodeck-plugin validate      - default-level manifest check against the packed artifact
                                           (pass -Publication for the stricter pre-publish check).

    If the macrodeck-plugin CLI (https://docs.macro-deck.app/cli/) isn't installed, falls back to
    running the same `dotnet publish` commands macrodeck-build.json declares, one per RID, into
    bin/publish/<rid>/ - you'll have runnable per-platform builds but not a packed .macroDeckPlugin,
    since only the CLI knows how to assemble and sign that artifact.

.PARAMETER Rid
    Build a single runtime identifier instead of all four declared in macrodeck-build.json
    (win-x64, osx-arm64, osx-x64, linux-x64).

.PARAMETER SkipValidate
    Skip the manifest validation step entirely (only relevant when the CLI is installed).

.PARAMETER Publication
    Validate at Publication level instead of the default - the stricter check CI runs before actually
    publishing to the Macro Deck plugin ecosystem (e.g. requires manifest.json's repository field).
    Not needed for local development or running the plugin yourself.

.EXAMPLE
    ./build.ps1
    ./build.ps1 -Rid win-x64
    ./build.ps1 -Publication
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'osx-arm64', 'osx-x64', 'linux-x64')]
    [string]$Rid,

    [switch]$SkipValidate,

    [switch]$Publication
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$rids = if ($Rid) { @($Rid) } else { @('win-x64', 'osx-arm64', 'osx-x64', 'linux-x64') }

function Invoke-Checked {
    param([string]$Exe, [string[]]$CliArgs)
    Write-Host "> $Exe $($CliArgs -join ' ')" -ForegroundColor Cyan
    & $Exe @CliArgs
    if ($LASTEXITCODE -ne 0) {
        throw "'$Exe $($CliArgs -join ' ')' exited with code $LASTEXITCODE"
    }
}

Write-Host "`n== Restore & compile check ==" -ForegroundColor Yellow
Invoke-Checked dotnet @('restore')
Invoke-Checked dotnet @('build', '-c', 'Release', '--no-restore')

$cli = Get-Command macrodeck-plugin -ErrorAction SilentlyContinue

if ($cli) {
    Write-Host "`n== Packing with macrodeck-plugin CLI ==" -ForegroundColor Yellow
    $buildArgs = @('build', '--output', './artifacts')
    if ($Rid) { $buildArgs += @('--rid', $Rid) }
    Invoke-Checked macrodeck-plugin $buildArgs

    if (-not $SkipValidate) {
        $artifact = Get-ChildItem -Path './artifacts' -Filter '*.macroDeckPlugin' |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($artifact) {
            Write-Host "`n== Validating $($artifact.Name) ==" -ForegroundColor Yellow
            $validateArgs = @('validate', '--artifact', $artifact.FullName)
            if ($Publication) { $validateArgs += @('--level', 'Publication') }
            Invoke-Checked macrodeck-plugin $validateArgs
        }
        else {
            Write-Warning "No .macroDeckPlugin artifact found in ./artifacts to validate."
        }
    }

    Write-Host "`nDone. Packed artifact(s) in ./artifacts" -ForegroundColor Green
}
else {
    Write-Warning @"
macrodeck-plugin CLI not found on PATH - falling back to plain 'dotnet publish' per RID.
This produces runnable per-platform builds under bin/publish/<rid>/, but NOT a packed
.macroDeckPlugin artifact (only the CLI assembles and signs that).

Install the CLI for the full build/pack/validate flow:
    dotnet tool install --global MacroDeck.Plugin.Cli --version 3.0.0-preview.6
"@

    foreach ($r in $rids) {
        Write-Host "`n== Publishing $r ==" -ForegroundColor Yellow
        Invoke-Checked dotnet @(
            'publish', 'MacroDeck.MbxHubPlugin.csproj',
            '-c', 'Release', '-r', $r, '--self-contained', 'false', '-p:UseAppHost=false',
            '-o', "bin/publish/$r"
        )
    }

    Write-Host "`nDone. Per-RID publish output in bin/publish/<rid>/" -ForegroundColor Green
}
