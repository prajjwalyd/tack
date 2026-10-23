[Code]

{ The uninstaller's own steps, as 0.2's (crates/tack-app/windows/hooks.nsh
  and Tauri's NSIS template): end a running Tack, remove the Run value, and
  delete the person's data only when they tick "Delete the application data".

  An update never runs the uninstaller: Setup installs over the old version.
  An uninstaller started with /UPDATE (as 0.2's hook allowed for) never
  deletes the data either. }

const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

var
  DeleteAppData: Boolean;

{ Asks whether to uninstall, with "Delete the application data" unticked.
  Returns False when the person cancels. Sets DeleteAppData. }
function AskToUninstall: Boolean;
var
  Form: TSetupForm;
  Prompt: TNewStaticText;
  DataBox: TNewCheckBox;
  UninstallButton, CancelButton: TNewButton;
  Width: Integer;
begin
  Form := CreateCustomForm(ScaleX(400), ScaleY(150), False, False);
  try
    Form.Caption := CustomMessage('UninstallCaption');

    Prompt := TNewStaticText.Create(Form);
    Prompt.Parent := Form;
    Prompt.Left := ScaleX(12);
    Prompt.Top := ScaleY(12);
    Prompt.Width := Form.ClientWidth - ScaleX(24);
    Prompt.WordWrap := True;
    Prompt.Caption := CustomMessage('UninstallText');

    DataBox := TNewCheckBox.Create(Form);
    DataBox.Parent := Form;
    DataBox.Left := ScaleX(12);
    DataBox.Top := Prompt.Top + Prompt.Height + ScaleY(14);
    DataBox.Width := Form.ClientWidth - ScaleX(24);
    DataBox.Height := ScaleY(20);
    DataBox.Caption := CustomMessage('DeleteAppData');
    DataBox.Checked := False;

    UninstallButton := TNewButton.Create(Form);
    UninstallButton.Parent := Form;
    UninstallButton.Caption := CustomMessage('UninstallButton');
    UninstallButton.ModalResult := mrOk;
    UninstallButton.Default := True;

    CancelButton := TNewButton.Create(Form);
    CancelButton.Parent := Form;
    CancelButton.Caption := SetupMessage(msgButtonCancel);
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;

    Width := Form.CalculateButtonWidth([UninstallButton.Caption, CancelButton.Caption]);
    UninstallButton.Width := Width;
    CancelButton.Width := Width;
    UninstallButton.Height := ScaleY(23);
    CancelButton.Height := ScaleY(23);
    CancelButton.Left := Form.ClientWidth - ScaleX(12) - Width;
    UninstallButton.Left := CancelButton.Left - ScaleX(6) - Width;
    CancelButton.Top := Form.ClientHeight - ScaleY(12 + 23);
    UninstallButton.Top := CancelButton.Top;

    Form.ActiveControl := UninstallButton;
    Result := Form.ShowModal = mrOk;
    DeleteAppData := Result and DataBox.Checked;
  finally
    Form.Free;
  end;
end;

{ Ends a running Tack and removes the Run value, before the files go. }
procedure BeforeUninstall;
var
  Paths: TArrayOfString;
begin
  SetArrayLength(Paths, 1);
  Paths[0] := ExpandConstant('{app}\Tack.exe');
  if CloseTack(Paths, 0, False) <> '' then
    Log('Tack is still running; its files may stay behind.');
  { Written by Tack itself ("Start with Windows"), so not in the uninstall
    log; removed as 0.2's uninstaller did. }
  if RegValueExists(HKCU, RunKey, 'Tack') then
    RegDeleteValue(HKCU, RunKey, 'Tack');
end;

{ Deletes the board, captures and notes, when the person asked for it. }
procedure AfterUninstall;
begin
  if not DeleteAppData then
    Exit;
  if IsUpdate then
  begin
    Log('An update: keeping the application data.');
    Exit;
  end;
  Log('Deleting the application data.');
  DelTree(ExpandConstant('{userappdata}\Tack'), True, True, True);
  DelTree(ExpandConstant('{localappdata}\Tack'), True, True, True);
end;
