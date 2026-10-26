; Public installer for the customized 64-bit Ultimate Zone Builder build.
; Build with build_optimized_installer.ps1 so PackageDir, PreviewNumber, and
; OutputDir are supplied consistently.

#define public Dependency_NoExampleSetup
#include "CodeDependencies.iss"

#ifndef PackageDir
  #define PackageDir "..\..\..\Releases\Ultimate Zone Builder Optimized x64 Preview 99"
#endif

#ifndef PreviewNumber
  #define PreviewNumber "98"
#endif

#ifndef InstallerOutputDir
  #define InstallerOutputDir "..\..\..\Releases\Ultimate Zone Builder Optimized x64 Preview 99 Installer"
#endif

#ifndef SetupAssetsDir
  #define SetupAssetsDir "."
#endif

#define ProductName "Ultimate Zone Builder Optimized"
#define ProductVersion GetStringFileInfo(PackageDir + "\Builder.exe", PRODUCT_VERSION)
#define ProductFileVersion GetVersionNumbersString(PackageDir + "\Builder.exe")
#define ProductDisplayVersion ProductVersion + " Preview " + PreviewNumber
#define ProductAppUserModelId "UltimateZoneBuilder.Optimized"

[Setup]
AppId={{36c5ad8a-1415-43f1-9677-67f079eef10b}
AppName={#ProductName}
AppVerName={#ProductName} {#ProductDisplayVersion} (64-bit)
AppVersion={#ProductVersion}
VersionInfoVersion={#ProductFileVersion}
VersionInfoCompany=Ultimate Zone Builder Optimized contributors
VersionInfoDescription={#ProductName} installer
VersionInfoProductName={#ProductName}
VersionInfoProductVersion={#ProductVersion}
AppPublisher=Ultimate Zone Builder Optimized contributors
DefaultDirName={autopf}\Ultimate Zone Builder Optimized
DefaultGroupName=Ultimate Zone Builder Optimized
DisableProgramGroupPage=auto
AllowNoIcons=yes
LicenseFile={#PackageDir}\LICENSE.txt
OutputDir={#InstallerOutputDir}
OutputBaseFilename=Ultimate Zone Builder Installer
SetupIconFile={#PackageDir}\UZB.ico
UninstallDisplayIcon={app}\Builder.exe
Compression=lzma2/ultra64
SolidCompression=yes
SourceDir={#PackageDir}
SetupLogging=no
AppMutex=ultimatezonebuilder
SetupMutex=ultimatezonebuilderoptimizedsetup
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ShowLanguageDialog=no
LanguageDetectionMethod=none
MinVersion=6.1sp1
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
WizardImageFile={#SetupAssetsDir}\UZB-large.bmp
WizardSmallImageFile={#SetupAssetsDir}\UZB-small.bmp
WizardImageAlphaFormat=defined
WizardImageStretch=no
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
UsePreviousAppDir=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Ship the tested package, but omit debug symbols, logs, source-control files,
; and machine-specific caches. UZB recreates its cache on first use.
Source: "*"; DestDir: "{app}"; Excludes: "*.pdb,UZBuilder.log,UZBCrash.txt,Updater.exe,Updater.ini,.gitignore,ResourceIndexCache\*,Restore\*"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Ultimate Zone Builder Optimized"; Filename: "{app}\Builder.exe"; WorkingDir: "{app}"; IconFilename: "{app}\UZB.ico"; AppUserModelID: "{#ProductAppUserModelId}"
Name: "{group}\{cm:UninstallProgram,Ultimate Zone Builder Optimized}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Ultimate Zone Builder Optimized"; Filename: "{app}\Builder.exe"; WorkingDir: "{app}"; IconFilename: "{app}\UZB.ico"; AppUserModelID: "{#ProductAppUserModelId}"; Tasks: desktopicon

[Run]
Filename: "{app}\Builder.exe"; WorkingDir: "{app}"; Description: "Launch {#ProductName}"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Ultimate Zone Builder"; ValueType: string; ValueName: "Location"; ValueData: "{app}"; Flags: uninsdeletevalue

[InstallDelete]
; Preview packages made by overlaying an older portable build could retain
; compiler tools at the application root and a second flat BCC definition.
; The canonical copies are all below {app}\Compilers in current packages.
Type: filesandordirs; Name: "{app}\BCC"
Type: filesandordirs; Name: "{app}\Hexen"
Type: filesandordirs; Name: "{app}\Nodebuilders"
Type: filesandordirs; Name: "{app}\Zandronum"
Type: filesandordirs; Name: "{app}\ZDaemon"
Type: filesandordirs; Name: "{app}\ZDoom"
Type: filesandordirs; Name: "{app}\ZT-BCC"
Type: files; Name: "{app}\Compilers\bcc.cfg"
Type: files; Name: "{app}\Compilers\bcc.exe"
Type: files; Name: "{app}\Compilers\std.acs"
Type: files; Name: "{app}\Compilers\zcommon.bcs"

[UninstallDelete]
; Only delete files UZB generates inside its installation directory. Never
; recursively remove the whole directory, because users may keep add-ons there.
Type: files; Name: "{app}\UZBuilder.log"
Type: filesandordirs; Name: "{app}\ResourceIndexCache"

[Code]
procedure InitializeWizard();
begin
  Dependency_AddDotNet47;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usUninstall) and (not UninstallSilent) then
  begin
    if MsgBox(
      'Also delete Ultimate Zone Builder settings, logs, crash reports, and map restore data for this Windows account?'#13#10#13#10 +
      'Choose No to keep them for a future installation.',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\Ultimate Zone Builder\Restore'), True, True, True);
      DeleteFile(ExpandConstant('{localappdata}\Ultimate Zone Builder\UZBuilder.cfg'));
      DeleteFile(ExpandConstant('{localappdata}\Ultimate Zone Builder\UZBuilder.log'));
      DeleteFile(ExpandConstant('{localappdata}\Ultimate Zone Builder\UZBCrash.txt'));
    end;
  end;
end;
