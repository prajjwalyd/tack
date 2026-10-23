[Code]

{ The installer's own arguments, read by the [Code] in Tack.iss.

  THE 0.2 BRIDGE: Tack 0.2's updater (tauri-plugin-updater 2.13) installs an
  update by starting the downloaded installer with NSIS-style arguments and
  quitting: "/P /UPDATE /R /ARGS <its own arguments>" (passive, an update,
  restart Tack after, and the old Tack's command line for NSIS to restart it
  with). The C# updater passes "/P /UPDATE /R" too, so one installer serves
  both. Inno Setup ignores arguments it does not know and hands them to this
  script through ParamStr, so they are read here:

    /P       passive: no questions, progress only
    /S       silent: nothing shown
    /UPDATE  an update: keep everything of the person's
    /R       start Tack when done
    /ARGS    the end of our arguments: it and everything after it are ignored

  Inno's own /SILENT, /VERYSILENT, /SUPPRESSMSGBOXES, /LOG work as usual. }

{ True when Name is one of the arguments before /ARGS, compared without case. }
function HasArg(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    { 0.2 BRIDGE: what follows /ARGS is the old Tack's own command line,
      never instructions for the installer. }
    if CompareText(ParamStr(I), '/ARGS') = 0 then
      Exit;
    if CompareText(ParamStr(I), Name) = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function IsUpdate: Boolean;
begin
  Result := HasArg('/UPDATE');
end;

function WantsRestart: Boolean;
begin
  Result := HasArg('/R');
end;

function WantsPassive: Boolean;
begin
  Result := HasArg('/P');
end;

function WantsSilent: Boolean;
begin
  Result := HasArg('/S');
end;

{ The command line for running this installer again in Inno's own silent
  mode: /S becomes /VERYSILENT and /P becomes /SILENT (a progress window
  and nothing else, which is what NSIS's passive mode shows). The other
  arguments before /ARGS are kept, so /UPDATE, /R and /LOG carry over. }
function SilentRunArgs: String;
var
  I: Integer;
  Arg: String;
begin
  if WantsSilent then
    Result := '/VERYSILENT'
  else
    Result := '/SILENT';
  Result := Result + ' /SUPPRESSMSGBOXES /NORESTART';
  for I := 1 to ParamCount do
  begin
    Arg := ParamStr(I);
    if CompareText(Arg, '/ARGS') = 0 then
      Break;
    if (CompareText(Arg, '/P') <> 0) and (CompareText(Arg, '/S') <> 0) then
      Result := Result + ' ' + AddQuotes(Arg);
  end;
end;

{ Inno decides whether it runs silently from its command line, before any
  script runs, and a script cannot change that. So for /P or /S this
  installer starts itself again with /SILENT or /VERYSILENT, waits for that
  run, and ends (exit code 1, "failed to initialize": nobody reads it, since
  0.2's updater and the C# one have quit by then; scripts that need the exit
  code use /SILENT or /VERYSILENT directly). Returns True when it handed
  over. If the second run cannot be started, this one goes on with the
  wizard, which still installs, only with a click. }
function HandedToSilentRun: Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if WizardSilent or not (WantsPassive or WantsSilent) then
    Exit;
  Log('Running again silently: ' + SilentRunArgs);
  if Exec(ExpandConstant('{srcexe}'), SilentRunArgs, '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
  begin
    Log('The silent run ended with exit code ' + IntToStr(ResultCode) + '.');
    Result := True;
  end
  else
    Log('Could not run silently (' + SysErrorMessage(ResultCode) + '); showing the wizard instead.');
end;
