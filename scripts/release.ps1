<#
.SYNOPSIS
    Tags the current `main` with a version and pushes the tag to trigger the release build.

.DESCRIPTION
    Pushing the tag triggers .github/workflows/release.yml (on: push: tags: ["v*"]) to build the
    platform binaries and publish a GitHub Release. Pushing `main` itself (e.g. after merging a
    feature branch) does not do this - only the tag push does.

    The tag is annotated with a changelog (commit subjects since the previous tag), which
    release.yml reads back out of the tag object and uses as the GitHub Release notes.

    PowerShell port of scripts/release.sh. Works on Windows PowerShell 5.1 and PowerShell 7+.

.PARAMETER Version
    The version to release, as X.Y.Z or vX.Y.Z.

.EXAMPLE
    scripts\release.ps1 0.1.6

.EXAMPLE
    scripts\release.ps1 v0.1.6
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version
)

Set-StrictMode -Version Latest

# Read git output as UTF-8 so non-ASCII commit subjects survive into the changelog.
$previousOutputEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

function Fail([string[]]$Message) {
    foreach ($line in $Message) { [Console]::Error.WriteLine($line) }
    [Console]::OutputEncoding = $previousOutputEncoding
    exit 1
}

# Runs git and returns its stdout; aborts the script if git exits non-zero.
function Invoke-Git {
    $output = & git @args
    if ($LASTEXITCODE -ne 0) {
        Fail "Error: 'git $($args -join ' ')' failed with exit code $LASTEXITCODE."
    }
    return $output
}

$normalized = $Version -replace '^v', ''

if ($normalized -notmatch '^\d+\.\d+\.\d+$') {
    Fail "Error: version must look like X.Y.Z (got '$Version')"
}

$tag = "v$normalized"

$currentBranch = Invoke-Git rev-parse --abbrev-ref HEAD
if ($currentBranch -ne 'main') {
    Fail "Error: you're on '$currentBranch', not 'main'. Check out main first: git checkout main"
}

$status = Invoke-Git status --porcelain
if ($status) {
    Fail 'Error: working tree has uncommitted changes. Commit or stash them first.'
}

Write-Host 'Fetching origin...'
Invoke-Git fetch origin main --tags | Out-Host

$localSha = Invoke-Git rev-parse main
$remoteSha = Invoke-Git rev-parse origin/main
if ($localSha -ne $remoteSha) {
    Fail "Error: local main ($localSha) doesn't match origin/main ($remoteSha).",
         "Push or pull main so it's in sync before tagging."
}

& git rev-parse --verify --quiet "refs/tags/$tag" *> $null
if ($LASTEXITCODE -eq 0) {
    Fail "Error: tag '$tag' already exists locally."
}

$remoteTag = Invoke-Git ls-remote --tags origin "refs/tags/$tag"
if ($remoteTag -and ($remoteTag | Select-String -SimpleMatch $tag -Quiet)) {
    Fail "Error: tag '$tag' already exists on origin."
}

Write-Host 'Building changelog since last tag...'
$previousTag = & git describe --tags --abbrev=0 $localSha 2> $null
if ($LASTEXITCODE -ne 0) { $previousTag = $null }

if ($previousTag) {
    $range = "$previousTag..$localSha"
} else {
    $range = $localSha
}

$changelogLines = @(Invoke-Git log $range --no-merges '--pretty=format:- %s (%h)')
$changelog = ($changelogLines -join "`n").Trim()
if (-not $changelog) {
    $changelog = "- No changes recorded since $previousTag"
}

$tagMessage = "## Changelog`n$changelog`n"

# Pass the message through a file rather than -m: Windows PowerShell mangles embedded quotes in
# native-command arguments, and commit subjects can contain them.
$messageFile = [System.IO.Path]::GetTempFileName()
try {
    [System.IO.File]::WriteAllText($messageFile, $tagMessage, [System.Text.UTF8Encoding]::new($false))

    Write-Host "Tagging $localSha as $tag..."
    Invoke-Git tag -a $tag -F $messageFile --cleanup=verbatim | Out-Host
}
finally {
    Remove-Item -LiteralPath $messageFile -ErrorAction SilentlyContinue
}

Write-Host "Pushing $tag to origin..."
Invoke-Git push origin $tag | Out-Host

Write-Host 'Done. See https://github.com/kevinRForshey/Paarde/actions for the release build.'
[Console]::OutputEncoding = $previousOutputEncoding
