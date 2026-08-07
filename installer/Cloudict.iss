; ============================================================================
;  Cloudict — Windows installer (Inno Setup)
;  Build with Inno Setup 6:  iscc installer\Cloudict.iss
;  (or run scripts\build-installer.bat which publishes the app first)
; ============================================================================

#define MyAppName        "Cloudict"
#define MyAppVersion     "2.3.1"
#define MyAppPublisher   "Cloudtart"
#define MyAppURL         "https://cloudtart.com"
#define MyAppExeName     "Cloudict.exe"
; Folder produced by `dotnet publish ... -c Release -r win-x64 --self-contained true`
#define MyPublishDir     "..\src\Cloudict\bin\Release\net7.0-windows10.0.22621.0\win-x64\publish"
#define MyIcon           "..\src\Cloudict\Assets\app-icon.ico"

[Setup]
; A unique, stable identifier for this product (do not reuse for other apps).
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

; The app itself requires administrator rights, so install machine-wide.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Modern, clean wizard.
WizardStyle=modern
SetupIconFile={#MyIcon}
; Logo shown inside the install wizard (welcome/finish strip + small top-right on each page).
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
Compression=lzma2/max
SolidCompression=yes
OutputDir=Output
OutputBaseFilename=Cloudict-{#MyAppVersion}-Setup
DisableWelcomePage=no
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[InstallDelete]
; Files that earlier versions installed and this one no longer uses. Inno only overwrites and
; adds, so without this an upgrade would leave them behind forever.
Type: files;          Name: "{app}\WebDriverManager.dll"
Type: filesandordirs; Name: "{app}\selenium-manager\linux"
Type: filesandordirs; Name: "{app}\selenium-manager\macos"

[Files]
; Ship the entire self-contained publish folder — including Drivers\, which carries the bundled
; ChromeDriver so the app works on first run with no internet connection. Excluded: debug symbols,
; the stale WebDriverManager download cache, Selenium Manager's non-Windows binaries (the app
; resolves its own driver, so none of them are ever used), and any settings.json left behind by
; running the app from the publish folder — that would ship the developer's own configuration.
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,Chrome\*,selenium-manager\linux\*,selenium-manager\macos\*,settings.json,settings.backup.json"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser
