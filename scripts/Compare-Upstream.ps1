[CmdletBinding()]
param(
    [string] $TargetRef,
    [string] $BaselineFile = (Join-Path (Split-Path $PSScriptRoot -Parent) 'upstream.json'),
    [string] $OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) 'typesafe-upstream'),
    [switch] $Check
)

$ErrorActionPreference = 'Stop'
$baseline = Get-Content $BaselineFile -Raw | ConvertFrom-Json

function Get-GitHubJson([string] $Path) {
    $json = & gh api $Path
    if ($LASTEXITCODE -ne 0) { throw "GitHub request failed: $Path" }
    return ($json -join "`n" | ConvertFrom-Json)
}

$repo = $baseline.repository
$release = Get-GitHubJson "repos/$repo/releases/latest"
if (-not $TargetRef) { $TargetRef = $release.tag_name }
$target = Get-GitHubJson "repos/$repo/commits/$([Uri]::EscapeDataString($TargetRef))"
$pinned = Get-GitHubJson "repos/$repo/commits/$([Uri]::EscapeDataString($baseline.tag))"
if ($pinned.sha -ne $baseline.commit) { throw 'Pinned upstream tag no longer matches its recorded commit.' }
$comparison = Get-GitHubJson "repos/$repo/compare/$($baseline.commit)...$($target.sha)"
if ($comparison.status -in @('behind', 'diverged')) { throw "Target must descend from the pinned commit (status: $($comparison.status))." }
$drift = $target.sha -ne $baseline.commit
$files = @($comparison.files | ForEach-Object {
    $file = $_
    $mapping = $baseline.surfaces | Where-Object { $_.upstream -eq $file.filename -or $_.upstream -eq $file.previous_filename }
    [ordered]@{
        upstream = $file.filename
        status = $file.status
        local = @($mapping | ForEach-Object { $_.local })
        patch = $file.patch
    }
})
$report = [ordered]@{
    repository = $repo
    baseline = $baseline
    targetRef = $TargetRef
    targetCommit = $target.sha
    latestRelease = $release.tag_name
    drift = $drift
    compareUrl = $comparison.html_url
    files = $files
}
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$report | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $OutputDirectory 'upstream-report.json') -Encoding utf8
$lines = @(
    '# TypeSafe upstream comparison'
    ''
    "Baseline: $($baseline.tag) ($($baseline.commit))"
    "Target: $TargetRef ($($target.sha))"
    "Latest release: $($release.tag_name)"
    "Drift detected: $drift"
    ''
    "[Full comparison]($($comparison.html_url))"
    ''
    '| Upstream file | Change | .NET surfaces to review |'
    '| --- | --- | --- |'
)
foreach ($file in $files) {
    $local = if ($file.local.Count) { $file.local -join ', ' } else { 'Unmapped: review explicitly (including new files and tests)' }
    $lines += "| $($file.upstream) | $($file.status) | $local |"
}
$lines += @('', 'The JSON report includes patches. This is a review aid, not an automatic port or proof of semantic parity.',
    'GitHub compare returns at most 300 files; use the full comparison/checkout for larger changes.',
    'Review upstream release notes and tests; update the manifest only after implementing and validating the port.')
$lines | Set-Content (Join-Path $OutputDirectory 'upstream-report.md') -Encoding utf8
$lines | Write-Output
if ($Check -and $drift) { exit 1 }
