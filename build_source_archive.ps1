param(
    [ValidateRange(1, 9999)]
    [int]$PreviewNumber = 99,

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repositoryDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$customBuildDirectory = Resolve-Path -LiteralPath (Join-Path $repositoryDirectory '..\..')
$releasesDirectory = Join-Path $customBuildDirectory 'Releases'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $releasesDirectory "Ultimate Zone Builder Optimized x64 Preview $PreviewNumber Installer"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$stagingDirectory = Join-Path $temporaryRoot ("uzb-source-" + [guid]::NewGuid().ToString('N'))
$archiveRoot = Join-Path $stagingDirectory "Ultimate Zone Builder Optimized Preview $PreviewNumber Source"

try {
    New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null

    Push-Location $repositoryDirectory
    try {
        $trackedFiles = @(git ls-files)
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to enumerate the repository files with Git.'
        }

        $deletedFiles = @(git ls-files --deleted)
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to enumerate deleted working-tree files with Git.'
        }

        # The optimized build intentionally contains working-tree source and script
        # additions that have not been committed to the historical base repository.
        # Include every non-ignored addition, while excluding only local build/runtime
        # state. This keeps the GPL source snapshot paired with the binary complete as
        # new forms, tests, and bundled UDB scripts are added.
        $additionalSourceFiles = @(git ls-files --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to enumerate added working-tree files with Git.'
        }

        $sourceFiles = @($trackedFiles + $additionalSourceFiles) |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                $deletedFiles -notcontains $_ -and
                $_ -notmatch '(?i)\.pdb$' -and
                $_ -notmatch '^(?i:\.tooling)/' -and
                $_ -notmatch '^(?i:Build/ResourceIndexCache)/' -and
                $_ -notmatch '^(?i:Build/Restore)/' -and
                $_ -notmatch '^(?i:Build/UZBuilder\.(cfg|log))$' -and
                $_ -notmatch '^(?i:Build/UZBCrash\.txt)$'
            } |
            Sort-Object -Unique

        foreach ($relativePath in $sourceFiles) {
            $sourcePath = Join-Path $repositoryDirectory $relativePath
            if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                throw "Source snapshot file is missing: $sourcePath"
            }

            $destinationPath = Join-Path $archiveRoot $relativePath
            $destinationParent = Split-Path -Parent $destinationPath
            if (-not (Test-Path -LiteralPath $destinationParent -PathType Container)) {
                New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
            }

            Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
        }

        $baseCommit = (git rev-parse HEAD).Trim()
        $sourceInfo = @(
            "Ultimate Zone Builder Optimized Preview $PreviewNumber",
            '',
            'This archive contains the corresponding working-tree source used for the customized build.',
            "Base repository commit: $baseCommit",
            'This independently maintained build retains the original project history and GPLv3 license notices.',
            '',
            'Build instructions and project files are included in this archive.',
            'License: GNU General Public License version 3; see LICENSE.txt.'
        )
        Set-Content -LiteralPath (Join-Path $archiveRoot 'SOURCE-INFO.txt') -Encoding UTF8 -Value $sourceInfo
    }
    finally {
        Pop-Location
    }

    $archivePath = Join-Path $OutputDirectory "Ultimate Zone Builder Optimized Preview $PreviewNumber Source.zip"
    if (Test-Path -LiteralPath $archivePath -PathType Leaf) {
        Remove-Item -LiteralPath $archivePath -Force
    }

    Compress-Archive -LiteralPath $archiveRoot -DestinationPath $archivePath -CompressionLevel Optimal

    $hash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
    $hashFile = Join-Path $OutputDirectory 'Source SHA256.txt'
    Set-Content -LiteralPath $hashFile -Encoding ASCII -Value ("{0}  {1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $archivePath))

    Write-Host "Source:   $archivePath"
    Write-Host "SHA-256: $($hash.Hash.ToLowerInvariant())"
}
finally {
    $resolvedStagingDirectory = [IO.Path]::GetFullPath($stagingDirectory)
    if ($resolvedStagingDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedStagingDirectory -PathType Container)) {
        Remove-Item -LiteralPath $resolvedStagingDirectory -Recurse -Force
    }
}
