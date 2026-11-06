param(
    [switch]$Push,
    [string]$Branch = 'docs-site'
)

$ErrorActionPreference = 'Stop'

$docs = $PSScriptRoot
$repo = Split-Path $docs -Parent
$site = Join-Path $docs '_site'
$worktree = Join-Path ([System.IO.Path]::GetTempPath()) "loopback-transcriber-$Branch"

function Invoke-Checked {
    param([string]$Description, [scriptblock]$Command)
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Test-GitRef([string]$Ref) {
    git -C $repo show-ref --verify --quiet $Ref
    return $LASTEXITCODE -eq 0
}

& (Join-Path $docs 'build.ps1')

git -C $repo worktree prune
if (Test-Path $worktree) {
    Remove-Item $worktree -Recurse -Force
}

if ((Test-GitRef "refs/heads/$Branch") -or (Test-GitRef "refs/remotes/origin/$Branch")) {
    Invoke-Checked 'git worktree add' { git -C $repo worktree add $worktree $Branch }
}
else {
    Invoke-Checked 'git worktree add' { git -C $repo worktree add --detach $worktree }
    Invoke-Checked 'git switch --orphan' { git -C $worktree switch --orphan $Branch }
}

try {
    Get-ChildItem $worktree -Force | Where-Object Name -ne '.git' | Remove-Item -Recurse -Force
    Copy-Item (Join-Path $site '*') $worktree -Recurse -Force
    New-Item (Join-Path $worktree '.nojekyll') -ItemType File -Force | Out-Null

    Invoke-Checked 'git add' { git -C $worktree add --all }
    git -C $worktree diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "No changes to publish on '$Branch'."
        return
    }

    $source = git -C $repo rev-parse --short HEAD
    Invoke-Checked 'git commit' { git -C $worktree commit --quiet -m "Publish documentation from $source" }

    if ($Push) {
        Invoke-Checked 'git push' { git -C $worktree push --set-upstream origin $Branch }
        Write-Host "Documentation published to '$Branch'."
    }
    else {
        Write-Host "Documentation committed to '$Branch'. Publish it with: git push origin $Branch"
    }
}
finally {
    git -C $repo worktree remove --force $worktree
}
