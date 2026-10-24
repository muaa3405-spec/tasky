; â”€â”€â”€ Tasky â€“ Inno Setup 6 installer script â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
; Download Inno Setup 6 from: https://jrsoftware.org/isdl.php
; Compile: iscc.exe setup.iss
; Output:  installer\Output\TaskySetup.exe

#define AppName      "Tasky"
#define AppVersion   "1.0.5"
#define AppPublisher "Muhammad Aadil"
#define AppURL       "https://github.com/muaa3405-spec/tasky"
#define AppExeName   "Tasky.exe"
; Path to the published output of  dotnet publish -c Release -r win-x64 -o <PublishDir>
#define PublishDir   "..\Tasky\bin\Release\net8.0\win-x64\publish"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=TaskySetup
SetupIconFile=..\Tasky\Assets\icon.ico
; Icon shown in Windows Settings â†’ Apps â†’ Installed apps
UninstallDisplayIcon={app}\icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
; Run at startup without UAC elevation on next login
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";    Description: "{cm:CreateDesktopIcon}";    GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupentry";   Description: "Start Tasky with Windows";  GroupDescription: "Startup:"; Flags: unchecked

[Files]
; Publish the entire self-contained folder
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Copy the icon file explicitly â€” in a self-contained .NET 8 publish the .ico is
; compiled into the exe as a resource and is NOT present as a loose file in the
; publish folder, so shortcuts cannot extract it from {app}\Tasky.exe reliably.
Source: "..\Tasky\Assets\icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";           Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\icon.ico"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";     Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon

[Registry]
; Add to Windows startup (HKCU so no UAC needed)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "Tasky"; \
    ValueData: """{app}\{#AppExeName}"""; \
    Tasks: startupentry; Flags: uninsdeletevalue

[Run]
; Launch after install (as the logged-in user, not elevated)
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Kill the process gracefully before uninstall
Filename: "taskkill.exe"; Parameters: "/IM {#AppExeName} /F"; Flags: skipifdoesntexist runhidden
