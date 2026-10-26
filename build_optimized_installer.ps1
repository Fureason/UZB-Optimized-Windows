param(
    [ValidateRange(1, 9999)]
    [int]$PreviewNumber = 99,

    [string]$PackageDirectory,

    [string]$OutputDirectory,

    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'

$repositoryDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$customBuildDirectory = Resolve-Path -LiteralPath (Join-Path $repositoryDirectory '..\..')
$releasesDirectory = Join-Path $customBuildDirectory 'Releases'

if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $releasesDirectory "Ultimate Zone Builder Optimized x64 Preview $PreviewNumber"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $releasesDirectory "Ultimate Zone Builder Optimized x64 Preview $PreviewNumber Installer"
}

if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
    $compilerCandidates = @(
        (Join-Path (Split-Path -Parent $customBuildDirectory) "Don't delete\Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Antigravity IDE\resources\app\node_modules\innosetup\bin\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )

    $InnoCompiler = $compilerCandidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($InnoCompiler) -or
    -not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw 'Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6 or pass -InnoCompiler.'
}

$requiredPackageFiles = @(
    'Builder.exe',
    'Builder.exe.config',
    'BuilderNative.dll',
    'LICENSE.txt',
    'UZB.ico',
    'UZBuilder.default.cfg'
)

if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
    throw "Package directory does not exist: $PackageDirectory"
}

foreach ($requiredFile in $requiredPackageFiles) {
    $requiredPath = Join-Path $PackageDirectory $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "The package is incomplete. Missing: $requiredPath"
    }
}

$bccConfigurations = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter 'bcc.cfg' -File -Recurse)
if ($bccConfigurations.Count -ne 1) {
    throw "The package must contain exactly one bcc.cfg below Compilers. Found $($bccConfigurations.Count). Rebuild the portable package from a clean Build directory."
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$installerScript = Join-Path $repositoryDirectory 'Setup\UZBuilder_optimized_x64.iss'
$setupAssetsDirectory = Join-Path $repositoryDirectory 'Setup'
$compilerArguments = @(
    '/Qp',
    "/DPackageDir=$PackageDirectory",
    "/DPreviewNumber=$PreviewNumber",
    "/DInstallerOutputDir=$OutputDirectory",
    "/DSetupAssetsDir=$setupAssetsDirectory",
    $installerScript
)

Write-Host "Building Preview $PreviewNumber installer..."
& $InnoCompiler @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $OutputDirectory 'Ultimate Zone Builder Installer.exe'
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "The compiler completed without producing the expected installer: $installerPath"
}

$hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
$hashFile = Join-Path $OutputDirectory 'SHA256.txt'
Set-Content -LiteralPath $hashFile -Encoding ASCII -Value ("{0}  {1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $installerPath))

Write-Host "Installer: $installerPath"
Write-Host "SHA-256:  $($hash.Hash.ToLowerInvariant())"
