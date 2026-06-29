; ============================================================================
;  Cloudict — Windows installer (Inno Setup)
;  Build with Inno Setup 6:  iscc installer\Cloudict.iss
;  (or run scripts\build-installer.bat which publishes the app first)
; ============================================================================

#define MyAppName        "Cloudict"
#define MyAppVersion     "2.2.0"
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

[Files]
; Ship the entire self-contained publish folder, but NOT the ChromeDriver cache
; (WebDriverManager downloads the matching driver at runtime) or debug symbols.
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Excludes: "Chrome\*,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser
