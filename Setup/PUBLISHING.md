# Publishing Ultimate Zone Builder Optimized

Run `build_portable_package.ps1` after compiling, then test that numbered
portable package. Run `build_optimized_installer.ps1` to validate the package,
build its installer, and write a SHA-256 checksum beside it. Finally, run
`build_source_archive.ps1` with the same preview number to make the
corresponding source snapshot and source checksum.

The source repository lives under `UZB Custom Build\Source`, while portable
packages and installers belong under `UZB Custom Build\Releases`. Build each
portable preview from a clean `Build` directory. Do not copy a new build over
an older portable package, because obsolete compiler folders can remain and
make the same compiler definition load more than once.

The release packages intentionally exclude debug symbols, editor logs, crash reports, restore data, source-control files, and `ResourceIndexCache`. Those files are either private development artifacts or safely rebuilt on the user's computer.

## Before publishing

1. Test the portable preview package first.
2. Build the installer with the matching preview number.
3. Install it on a clean Windows account or virtual machine and test opening a map, Visual Mode, saving, uninstalling, and reinstalling.
4. Publish the generated installer and its `SHA256.txt` file together.
5. Publish the complete corresponding source code for that exact build. Ultimate Zone Builder is licensed under GPLv3, so binary releases must be accompanied by an appropriate source-code distribution or offer as described by the license.
6. If public distribution becomes regular, code-sign the installer and application executable with a trusted Windows code-signing certificate. Unsigned downloads can trigger Microsoft Defender SmartScreen warnings even when the files are safe.

The wizard assets named `UZB-large.bmp` and `UZB-small.bmp` must contain real
Windows bitmap data. Merely renaming a PNG to `.bmp` can compile successfully
but makes the installer stop at startup with “Bitmap image is not valid.”

The customized build should be described as a community/custom build, not as an official Sonic Team Junior release.

## Discord Rich Presence

Create a separate Discord application for Ultimate Zone Builder and put its numeric application ID in `discord.applicationid` in `Build/UZBuilder.default.cfg` before packaging. Do not reuse the Custom SRB2 client's ID: Discord would show the editor as the game. If you upload a large Rich Presence image asset, place its asset key in `discord.largeimage`; otherwise leave that value blank. The connection is optional and runs in the background, so UZB behaves normally when Discord is closed.
