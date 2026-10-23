[Code]

{ Finding, waiting for and ending a running Tack.

  Tack is found by the full path of its exe, through WMI, so only the Tack
  being replaced is touched: a development build or another copy elsewhere
  keeps running. Inno's own CloseApplications is off (Tack.iss): it would
  ask the person, and an update must not ask. }

const
  { What TackProcesses returns when WMI could not be asked. }
  TackUnknown = -1;

{ Counts the processes whose exe is one of ExePaths (compared without case),
  ending each one when EndThem is set. Returns TackUnknown if WMI fails. }
function TackProcesses(const ExePaths: TArrayOfString; EndThem: Boolean): Integer;
var
  Locator, Service, Found, Process: Variant;
  I, J: Integer;
  Path: String;
begin
  Result := 0;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    { WQL compares strings without case: this finds tack.exe and Tack.exe. }
    Found := Service.ExecQuery('SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE Name = ''tack.exe''');
    for I := 0 to Found.Count - 1 do
    begin
      Process := Found.ItemIndex(I);
      { Empty for processes this user may not inspect, which are not ours. }
      if VarIsNull(Process.ExecutablePath) then
        Continue;
      Path := Process.ExecutablePath;
      for J := 0 to GetArrayLength(ExePaths) - 1 do
        if AnsiLowercase(Path) = AnsiLowercase(ExePaths[J]) then
        begin
          Result := Result + 1;
          if EndThem then
          begin
            Log('Ending ' + Path + ' (process ' + IntToStr(Process.ProcessId) + ').');
            Process.Terminate(1);
          end;
          Break;
        end;
    end;
  except
    Log('Could not list the running processes: ' + GetExceptionMessage);
    Result := TackUnknown;
  end;
end;

{ Waits up to TimeoutMs for every Tack at ExePaths to exit. Returns how many
  are still running: 0 when none is, TackUnknown if WMI could not tell. }
function WaitForTack(const ExePaths: TArrayOfString; TimeoutMs: Integer): Integer;
var
  Waited: Integer;
begin
  Waited := 0;
  Result := TackProcesses(ExePaths, False);
  while (Result > 0) and (Waited < TimeoutMs) do
  begin
    Sleep(250);
    Waited := Waited + 250;
    Result := TackProcesses(ExePaths, False);
  end;
end;

{ Makes sure no Tack at ExePaths runs before its files are replaced or
  removed. First waits up to WaitMs for it to quit by itself (an updater
  quits right after starting the installer), then ends it, asking first when
  Ask is set. Returns '' when none runs, or why it could not go on. }
function CloseTack(const ExePaths: TArrayOfString; WaitMs: Integer; Ask: Boolean): String;
var
  Running: Integer;
begin
  Result := '';
  Running := WaitForTack(ExePaths, WaitMs);
  if Running = 0 then
    Exit;
  if Running = TackUnknown then
  begin
    { Nothing to end by path. Give a quitting Tack a moment; if one still
      holds its files, Setup says so when it replaces them. }
    Sleep(2000);
    Exit;
  end;
  if Ask and (SuppressibleMsgBox(CustomMessage('TackRunning'), mbConfirmation, MB_OKCANCEL, IDOK) <> IDOK) then
  begin
    Result := CustomMessage('TackStillRunning');
    Exit;
  end;
  TackProcesses(ExePaths, True);
  if WaitForTack(ExePaths, 5000) > 0 then
    Result := CustomMessage('TackStillRunning');
end;
