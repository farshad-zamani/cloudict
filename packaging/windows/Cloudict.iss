; ============================================================================
;  Cloudict — Windows installer (Inno Setup)
;  Build with:  iscc packaging\windows\Cloudict.iss
;  (or run scripts\build-all.ps1, which publishes first)
; ============================================================================

#define MyAppName        "Cloudict"
#define MyAppVersion     "3.0.3"
#define MyAppPublisher   "Cloudtart"
#define MyAppURL         "https://cloudtart.com"
#define MyAppExeName     "Cloudict.exe"
; Folder produced by: dotnet publish src\Cloudict.App -c Release -r win-x64 --self-contained true
#define MyPublishDir     "..\..\src\Cloudict.App\bin\Release\net10.0\win-x64\publish"
#define MyIcon           "..\..\src\Cloudict.App\Assets\app-icon.ico"

[Setup]
; Unchanged from 2.x so this upgrades an existing install rather than sitting beside it.
AppId={{D7B6F3A2-9C4E-4E1B-A2D7-2F1E9B0A6C44}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoDescription={#MyAppName} | Speak to type anywhere
VersionInfoVersion={#MyAppVersion}

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

; 3.0 no longer requires administrator to run, but installing under Program Files still does.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

WizardStyle=modern
SetupIconFile={#MyIcon}
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
Compression=lzma2/max
SolidCompression=yes
OutputDir=..\..\dist
OutputBaseFilename=Cloudict-{#MyAppVersion}-Setup
DisableWelcomePage=no
LicenseFile=..\..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[InstallDelete]
; Clear the install folder before laying down 3.0.
;
; Inno only overwrites and adds, and 3.0 shares almost no file names with 2.x: the UI moved from
; WPF to Avalonia, so the entire WPF runtime, the old dependencies and the previous driver version
; would all survive an upgrade. Measured on a real 2.3.1 install, that left 321 MB in the folder
; where 3.0 alone needs about 150.
;
; Listing individual files cannot work here - there are hundreds - so the folder is emptied. The
; only thing worth keeping is a settings.json belonging to a pre-2.2.6 install that never migrated,
; and PrepareToInstall below copies that out first.
Type: filesandordirs; Name: "{app}\*"

[Files]
; The whole self-contained publish folder, including Drivers\, which carries the bundled
; ChromeDriver so the app works on first run with no internet connection. Excluded: debug symbols
; and any settings.json left behind by running the app from the publish folder, which would ship
; the developer's own configuration.
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,settings.json,settings.backup.json"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
{
  Rescues settings from a pre-2.2.6 install before the folder is emptied.

  Versions up to 2.2.4 kept settings.json beside the executable. 2.2.6 moved it to the per-user
  config directory and migrates on first run, but that migration reads the old file - which the
  wipe above is about to delete. Copying it across here means someone upgrading straight from an
  old version keeps their delays, shortcuts and voice commands.
}
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  OldSettings, NewDir, NewSettings: String;
begin
  Result := '';

  OldSettings := ExpandConstant('{app}\settings.json');
  NewDir      := ExpandConstant('{userappdata}\Cloudict');
  NewSettings := NewDir + '\settings.json';

  if FileExists(OldSettings) and (not FileExists(NewSettings)) then
  begin
    if ForceDirectories(NewDir) then
    begin
      if FileCopy(OldSettings, NewSettings, False) then
        Log('Migrated settings.json out of the install folder before cleaning it.')
      else
        Log('Could not migrate settings.json; continuing.');
    end;
  end;
end;
