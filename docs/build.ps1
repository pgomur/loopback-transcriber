param(
    [switch]$Serve
)

$ErrorActionPreference = 'Stop'

$docs = $PSScriptRoot
$repo = Split-Path $docs -Parent
$site = Join-Path $docs '_site'
$languages = @('es', 'en')

function Invoke-Checked {
    param([string]$Description, [scriptblock]$Command)
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repo
try {
    Invoke-Checked 'dotnet tool restore' { dotnet tool restore }

    if (Test-Path $site) {
        Remove-Item $site -Recurse -Force
    }

    foreach ($language in $languages) {
        Invoke-Checked "docfx build ($language)" { dotnet docfx build (Join-Path $docs "$language\docfx.json") }
    }

    Get-ChildItem $site -Recurse -Filter '*.map' | Remove-Item -Force

    Copy-Item (Join-Path $docs 'index.html') $site

    if ($Serve) {
        Invoke-Checked 'docfx serve' { dotnet docfx serve $site }
    }
}
finally {
    Pop-Location
}
