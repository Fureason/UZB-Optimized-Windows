param(
    [ValidateRange(1, 9999)]
    [int]$PreviewNumber = 99,

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repositoryDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildDirectory = Join-Path $repositoryDirectory 'Build'
$customBuildDirectory = Resolve-Path -LiteralPath (Join-Path $repositoryDirectory '..\..')
$releasesDirectory = Join-Path $customBuildDirectory 'Releases'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $releasesDirectory "Ultimate Zone Builder Optimized x64 Preview $PreviewNumber"
}

if (-not (Test-Path -LiteralPath $buildDirectory -PathType Container)) {
    throw "Build directory does not exist: $buildDirectory"
}

if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Output already exists. Choose a new preview number or remove the old package deliberately: $OutputDirectory"
}

New-Item -ItemType Directory -Path $OutputDirectory | Out-Null

& robocopy $buildDirectory $OutputDirectory /E /XD ResourceIndexCache Restore /XF '*.pdb' UZBuilder.log UZBuilder.cfg UZBCrash.txt Updater.exe Updater.ini /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -gt 7) {
    throw "Portable package copy failed with robocopy exit code $LASTEXITCODE."
}

$requiredFiles = @('Builder.exe', 'Builder.exe.config', 'BuilderNative.dll', 'LICENSE.txt', 'UZB.ico', 'UZBuilder.default.cfg')
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory $requiredFile) -PathType Leaf)) {
        throw "Portable package is incomplete. Missing: $requiredFile"
    }
}

$bccConfigurations = @(Get-ChildItem -LiteralPath $OutputDirectory -Filter 'bcc.cfg' -File -Recurse)
if ($bccConfigurations.Count -ne 1) {
    throw "Portable package must contain exactly one bcc.cfg; found $($bccConfigurations.Count)."
}

Write-Host "Portable package: $OutputDirectory"
