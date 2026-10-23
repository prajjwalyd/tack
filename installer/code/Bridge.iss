[Code]

{ THE 0.2 BRIDGE: moving an installed Tack 0.2 (Tauri, NSIS installer) to
  this one, and leaving 0.2 alone on Windows 10.

  0.2 installed per user into %LOCALAPPDATA%\Tack (tack.exe, uninstall.exe),
  with the Apps entry HKCU\...\Uninstall\Tack, the key HKCU\Software\tack\Tack
  and the Start menu shortcut Programs\Tack.lnk. Its WebView2 profile
  is in %LOCALAPPDATA%\Tack\WebView2 and %LOCALAPPDATA%\app.tack.board.
  The person's things are kept: %APPDATA%\Tack (board.json), the Captures
  and Notes folders in %LOCALAPPDATA%\Tack, and the Run value "Tack" (the
  new Tack rewrites its path at start). }

const
  OldUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Tack';
  OldProductKey = 'Software\tack\Tack';
  OldProductParentKey = 'Software\tack';

{ Windows 11 is version 10.0 with build 22000 or later. }
function IsWindows11OrLater: Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major > 10) or ((Version.Major = 10) and (Version.Build >= 22000));
end;

{ 0.2 is installed when its Apps entry is there. }
function OldTackInstalled: Boolean;
begin
  Result := RegKeyExists(HKCU, OldUninstallKey);
end;

{ 0.2's folder, from its Apps entry (NSIS wrote it in quotes), or where it
  installs by default. }
function OldTackDir: String;
begin
  if not RegQueryStringValue(HKCU, OldUninstallKey, 'InstallLocation', Result) then
    Result := '';
  Result := RemoveQuotes(Trim(Result));
  if Result = '' then
    Result := ExpandConstant('{localappdata}\Tack');
  Result := RemoveBackslashUnlessRoot(Result);
end;

function OldTackExe: String;
begin
  Result := AddBackslash(OldTackDir) + 'tack.exe';
end;

function SamePath(const A, B: String): Boolean;
begin
  Result := AnsiLowercase(RemoveBackslashUnlessRoot(A)) = AnsiLowercase(RemoveBackslashUnlessRoot(B));
end;

{ WINDOWS 10: this Tack needs Windows 11, but 0.2's updater offers it to 0.2
  on Windows 10 too, after having quit. So, before anything is changed,
  start 0.2 again (when asked to with /R) once the quitting copy is gone. }
procedure RestartOldTack;
var
  Exe: String;
  Paths: TArrayOfString;
  ResultCode: Integer;
begin
  Exe := OldTackExe;
  if not FileExists(Exe) then
  begin
    Log('Not starting Tack 0.2 again: ' + Exe + ' is not there.');
    Exit;
  end;
  { Started while the old copy is still quitting, it would find that copy
    running and give way to it, leaving no Tack at all. }
  SetArrayLength(Paths, 1);
  Paths[0] := Exe;
  if WaitForTack(Paths, 10000) > 0 then
  begin
    Log('Tack 0.2 is still running; not starting another.');
    Exit;
  end;
  if not ExecAsOriginalUser(Exe, '', ExtractFileDir(Exe), SW_SHOWNORMAL, ewNoWait, ResultCode) then
    Log('Could not start Tack 0.2 again: ' + SysErrorMessage(ResultCode));
end;

{ Removes a folder 0.2's WebView2 used. Its browser processes quit shortly
  after the Tack that started them, and hold files until they do. }
procedure RemoveWebViewFolder(const Dir: String);
var
  Attempt: Integer;
begin
  for Attempt := 1 to 10 do
  begin
    if not DirExists(Dir) then
      Exit;
    DelTree(Dir, True, True, True);
    if not DirExists(Dir) then
      Exit;
    Sleep(500);
  end;
  Log('Could not remove all of ' + Dir + '.');
end;

{ Removes what 0.2 leaves behind once this Tack is installed. Runs after the
  files are copied, so an installation that fails leaves 0.2 working and
  uninstallable. }
procedure RemoveOldTack;
var
  Dir: String;
begin
  Dir := OldTackDir;
  Log('Removing what Tack 0.2 left in ' + Dir + '.');
  // Its exe was replaced by Tack.exe when 0.2 lived in the install folder
  // (names compare without case). Installed elsewhere by hand, its exe would
  // be a second, stale Tack. Only the install folder, or a folder that holds
  // tack.exe, is touched.
  if SamePath(Dir, ExpandConstant('{app}')) or FileExists(AddBackslash(Dir) + 'tack.exe') then
  begin
    { Run, 0.2's uninstaller would delete the new Tack.exe. }
    DeleteFile(AddBackslash(Dir) + 'uninstall.exe');
    if not SamePath(Dir, ExpandConstant('{app}')) then
    begin
      DeleteFile(AddBackslash(Dir) + 'tack.exe');
      RemoveDir(Dir);
    end;
  end;
  { A second "Tack" in Apps, whose uninstaller is gone. }
  RegDeleteKeyIncludingSubkeys(HKCU, OldUninstallKey);
  { NSIS's own note of the install folder. The parent goes only if empty:
    registry names ignore case, so it is also Software\Tack. }
  RegDeleteKeyIncludingSubkeys(HKCU, OldProductKey);
  RegDeleteKeyIfEmpty(HKCU, OldProductParentKey);
  { The C# Tack has no web view. }
  RemoveWebViewFolder(ExpandConstant('{localappdata}\Tack\WebView2'));
  RemoveWebViewFolder(ExpandConstant('{localappdata}\app.tack.board'));
  // 0.2's Start menu shortcut, Programs\Tack.lnk, is the one [Icons] writes,
  // so it has been replaced rather than left behind.
end;
