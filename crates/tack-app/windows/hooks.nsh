; Extra uninstall steps for the NSIS installer (see bundle.windows.nsis in
; tauri.conf.json).
;
; Tauri's own "Delete the application data" step only removes folders named
; after the app identifier. Tack keeps its data in %APPDATA%\Tack (the board)
; and %LOCALAPPDATA%\Tack (captures, notes, WebView2 profile), so it goes here.
; Tauri's template already stops a running Tack first and removes the
; "Start with Windows" Run value on uninstall.
!macro NSIS_HOOK_POSTUNINSTALL
  ; An update runs the old uninstaller with /UPDATE and must keep the board.
  ${If} $DeleteAppDataCheckboxState = 1
  ${AndIf} $UpdateMode <> 1
    SetShellVarContext current
    RmDir /r "$APPDATA\Tack"
    RmDir /r "$LOCALAPPDATA\Tack"
  ${EndIf}
!macroend
