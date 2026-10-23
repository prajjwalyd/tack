[Code]

{ /P, passive (0.2 BRIDGE, and the C# updater): a progress window and
  nothing else, as NSIS's passive mode shows.

  Inno decides whether it runs silently from its own command line, before
  any script runs, and does not let a script start Setup's own exe again
  before installing (Exec returns "Access is denied"). So a passive run is
  an ordinary run that skips every page: Inno stops on the Ready page until
  the wizard is visible, so Next is pressed there by a click posted to the
  wizard, which is handled once it shows. Nothing asks a question either:
  see PrepareToInstall in Tack.iss. }

const
  WM_COMMAND = $0111;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := WantsPassive;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  { What a click on Next tells the wizard: WM_COMMAND, BN_CLICKED (0), from
    the button's window. }
  if (CurPageID = wpReady) and WantsPassive then
    PostMessage(WizardForm.Handle, WM_COMMAND, 0, WizardForm.NextButton.Handle);
end;
