; Inno Setup script for the WhatDisk installer, compiled by build/windows/package.sh, which defines:
;   AppVersion      the full version, for example 0.1.3-alpha
;   NumericVersion  the version without its prerelease label, for example 0.1.3
;   Arch            x64 or arm64
;   SourceDir       the published WhatDisk folder
;   OutputDir       where to write the installer
;   OutputBaseName  the installer's file name without .exe
;
; The installer installs for the current user by default (no administrator rights needed), into
; %LOCALAPPDATA%\Programs\WhatDisk, and adds a Start menu entry and an uninstaller.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif

[Setup]
AppId={{6F3D9A42-5C1B-4E8F-A2D7-9B0E4C6A1F35}
AppName=WhatDisk
AppVersion={#AppVersion}
AppVerName=WhatDisk {#AppVersion}
AppPublisher=Spiky Ball Software
AppCopyright=Copyright (c) 2026 Spiky Ball Software
VersionInfoVersion={#NumericVersion}
VersionInfoProductName=WhatDisk
DefaultDirName={autopf}\WhatDisk
DefaultGroupName=WhatDisk
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed={#Arch}
ArchitecturesInstallIn64BitMode={#Arch}
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\WhatDisk.exe
UninstallDisplayName=WhatDisk

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\WhatDisk"; Filename: "{app}\WhatDisk.exe"

[Run]
Filename: "{app}\WhatDisk.exe"; Description: "Start WhatDisk"; Flags: nowait postinstall skipifsilent
