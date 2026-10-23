[Code]

{ The installer's own arguments, read by the [Code] in Tack.iss.

  THE 0.2 BRIDGE: Tack 0.2's updater (tauri-plugin-updater 2.13) installs an
  update by starting the downloaded installer with NSIS-style arguments and
  quitting: "/P /UPDATE /R /ARGS <its own arguments>" (passive, an update,
  restart Tack after, and the old Tack's command line for NSIS to restart it
  with). The C# updater passes "/P /UPDATE /R" too, so one installer serves
  both. Inno Setup ignores arguments it does not know and hands them to this
  script through ParamStr, so they are read here:

    /P       passive: no questions, progress only (code\Passive.iss)
    /UPDATE  an update: keep everything of the person's
    /R       start Tack when done
    /ARGS    the end of our arguments: it and everything after it are ignored

  NSIS's /S (silent) is not taken: nothing passes it. Inno's own /SILENT,
  /VERYSILENT, /SUPPRESSMSGBOXES and /LOG work as usual. }

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
