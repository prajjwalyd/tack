; Tack's installer, compiled by Inno Setup 6.7 for one architecture from a
; Native AOT publish folder:
;
;   ISCC /DAppVersion=0.3.0 /DAppArch=x64 /DPublishDir=<folder> installer\Tack.iss
;
; writes Tack_0.3.0_x64-setup.exe (AppArch x64 or arm64), the names 0.2's
; releases used. scripts/release/Build-Installer.ps1 runs it. The design is
; docs/distribution-design.md, section 4.
;
; It installs per user into %LOCALAPPDATA%\Tack, where Tack 0.2 is, and is
; also the bridge from 0.2: 0.2's updater downloads it, checks its signature
; and runs it with "/P /UPDATE /R /ARGS". Every step that is there for that
; is marked "0.2 BRIDGE". Never run it on a PC whose installed Tack you want
; to keep: it replaces it.

#if VER < EncodeVer(6, 7, 0)
  #error Tack's installer needs Inno Setup 6.7 or later
#endif
#ifndef AppVersion
  #error Pass the version: /DAppVersion=0.3.0
#endif
#ifndef PublishDir
  #error Pass the Native AOT publish folder: /DPublishDir=<folder>
#endif
#ifndef AppArch
  #error Pass the architecture: /DAppArch=x64 or /DAppArch=arm64
#endif

#if AppArch == "x64"
  ; x64-compatible: x64 Windows, and ARM64 Windows 11, which runs x64 apps.
  #define InnoArch "x64compatible"
#elif AppArch == "arm64"
  #define InnoArch "arm64"
#else
  #error AppArch must be x64 or arm64
#endif

#if !FileExists(AddBackslash(PublishDir) + "Tack.exe")
  #error The publish folder has no Tack.exe
#endif

; File version resources take numbers only: 0.3.0-beta.1 becomes 0.3.0.
#define NumericVersion Copy(AppVersion, 1, Pos("-", AppVersion + "-") - 1)

[Setup]
; Setup's identity in Apps (HKCU\...\Uninstall\{AppId}_is1) and the key every
; later installer uses to find and update this installation. It must never
; change: a new AppId would install a second Tack beside the first.
AppId={{61E19669-5E3C-4153-A9BF-1D36EFE9BEA9}
AppName=Tack
AppVersion={#AppVersion}
AppVerName=Tack {#AppVersion}
AppPublisher=Tack
AppPublisherURL=https://github.com/prajjwalyd/tack
AppSupportURL=https://github.com/prajjwalyd/tack/issues
AppUpdatesURL=https://github.com/prajjwalyd/tack/releases
AppCopyright=The Tack contributors
VersionInfoVersion={#NumericVersion}
VersionInfoDescription=Tack Setup

; Per user, no elevation, always where 0.2 is: %LOCALAPPDATA%\Tack also
; holds the person's Captures and Notes, and Tack's identity (its folder,
; mutex and Run value) must not differ between versions.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Tack
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyMemo=yes
ArchitecturesAllowed={#InnoArch}
ArchitecturesInstallIn64BitMode={#InnoArch}

; WINDOWS 10 (0.2 BRIDGE): Tack needs Windows 11, but Inno checks MinVersion
; before any script runs and only shows an error, which would leave 0.2 on
; Windows 10 closed after its updater runs this. So Windows 10 is let in
; here and InitializeSetup decides (code\Bridge.iss).
MinVersion=10.0

; Inno would ask the person to close Tack; an update must not ask, so the
; script finds and ends it (code\Processes.iss). There is no SetupMutex
; either: /P and /S run a second, silent copy while the first waits.
CloseApplications=no
RestartApplications=no

UninstallDisplayName=Tack
UninstallDisplayIcon={app}\Tack.exe
SetupIconFile=..\assets\icons\icon.ico
WizardStyle=modern dynamic
ShowLanguageDialog=no

; A log in %TEMP% for every run, so a failed update can be looked into.
SetupLogging=yes
UninstallLogging=yes

OutputDir=..\artifacts\installer
OutputBaseFilename=Tack_{#AppVersion}_{#AppArch}-setup
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.NeedsWindows11=Tack needs Windows 11.%n%nThis PC has an older version of Windows, so nothing was installed.
english.TackRunning=Tack is running. Click OK to close it and go on, or Cancel to stop.
english.TackStillRunning=Tack is still running. Quit it from its icon in the tray, then run Setup again.
english.LaunchTack=Start Tack
english.UninstallCaption=Uninstall Tack
english.UninstallText=Tack will be removed from this PC. Your board, captures and notes stay unless you tick the box below.
english.DeleteAppData=Delete the application data
english.UninstallButton=Uninstall

[Files]
; Everything the publish wrote except the debug symbols and the API docs.
; Files a later version stops shipping stay behind: list them by name in an
; [InstallDelete] section. Never delete by wildcard in {app}: the person's
; Captures and Notes live there.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,\Tack*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Programs\Tack.lnk is also 0.2's shortcut, so the bridge replaces it. An
; update does not bring back a shortcut the person deleted.
Name: "{userprograms}\Tack"; Filename: "{app}\Tack.exe"; WorkingDir: "{app}"; Check: ShouldWriteShortcut

[Run]
; Run by hand, the last page offers to start Tack; with /R, CurStepChanged
; starts it instead.
Filename: "{app}\Tack.exe"; Description: "{cm:LaunchTack}"; Flags: nowait postinstall skipifsilent; Check: not WantsRestart

#include "code\Arguments.iss"
#include "code\Processes.iss"
#include "code\Bridge.iss"
#include "code\Uninstall.iss"

[Code]

function InitializeSetup: Boolean;
begin
  Result := False;
  { WINDOWS 10 (0.2 BRIDGE): change nothing. An update from 0.2 starts the
    old Tack again; run by hand, Setup says why it stops. Either way it
    exits with an error code. }
  if not IsWindows11OrLater then
  begin
    if IsUpdate then
    begin
      Log('Windows is older than 11: leaving Tack 0.2 as it is.');
      if WantsRestart then
        RestartOldTack;
    end
    else
      SuppressibleMsgBox(CustomMessage('NeedsWindows11'), mbCriticalError, MB_OK, IDOK);
    Exit;
  end;
  { /P and /S (0.2 BRIDGE, and the C# updater): run again silently. }
  if HandedToSilentRun then
    Exit;
  Result := True;
end;

function ShouldWriteShortcut: Boolean;
begin
  Result := not IsUpdate or FileExists(ExpandConstant('{userprograms}\Tack.lnk'));
end;

{ Before any file is replaced: no Tack may be running from here, nor (0.2
  BRIDGE) from where 0.2 is, if that is somewhere else. An update waits for
  the updater's Tack to quit by itself first. Run by hand, Setup asks before
  ending Tack, as 0.2's did. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Paths: TArrayOfString;
  WaitMs: Integer;
begin
  SetArrayLength(Paths, 1);
  Paths[0] := ExpandConstant('{app}\Tack.exe');
  if OldTackInstalled and not SamePath(OldTackDir, ExpandConstant('{app}')) then
  begin
    SetArrayLength(Paths, 2);
    Paths[1] := OldTackExe;
  end;
  if IsUpdate then
    WaitMs := 10000
  else
    WaitMs := 0;
  Result := CloseTack(Paths, WaitMs, not WizardSilent);
end;

procedure StartTack;
var
  ResultCode: Integer;
begin
  if not ExecAsOriginalUser(ExpandConstant('{app}\Tack.exe'), '', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ResultCode) then
    Log('Could not start Tack: ' + SysErrorMessage(ResultCode));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  { 0.2 BRIDGE: once the new files are in place. }
  if (CurStep = ssPostInstall) and OldTackInstalled then
    RemoveOldTack;
  if (CurStep = ssDone) and WantsRestart then
    StartTack;
end;

function InitializeUninstall: Boolean;
begin
  DeleteAppData := False;
  { Silent, the uninstaller never deletes the person's data. }
  if UninstallSilent then
    Result := True
  else
    Result := AskToUninstall;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  case CurUninstallStep of
    usUninstall: BeforeUninstall;
    usPostUninstall: AfterUninstall;
  end;
end;
