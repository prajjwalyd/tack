# Privacy: what goes over the network

## Tack itself

Nothing. Tack's code has no network access: no HTTP client, no update check,
no analytics. The board is a local page whose content security policy only
allows Tack's own files, `data:` images and the local IPC channel, so even a
bug in the page could not reach the internet. Your board lives in
`%APPDATA%\Tack\board.json` and unsaved captures in
`%LOCALAPPDATA%\Tack\Captures`.

## The WebView2 runtime

Tack draws the board with Microsoft Edge WebView2, the Windows component
that many apps use to show web pages. It is a cut-down Microsoft Edge, and
some of Edge's online services start with it whatever the app shows. Tack
turns off every one we could find and measure.

How it was measured: Windows 11 (build 26200), WebView2 runtime
154.0.4258.62, Windows diagnostic data set to "Required only", the PC's DNS
servers set to Google's 8.8.8.8 and 8.8.4.4. Every TCP connection and UDP
socket of `tack.exe` and its `msedgewebview2.exe` children was recorded once a
second for 3 minutes after launch, the runtime's own network log
(`--log-net-log`) gave the names its network service looked up, and the
Windows DNS cache gave the names the browser process looked up.

| What was seen | Cause | Off in Tack? | How |
|---|---|---|---|
| HTTPS to `substrate.office.com` (40.99.x.x, 40.104.x.x, 52.97/52.98.x.x), 1 to 3 s after launch, from the browser process; on some launches also `odc.officeapps.live.com`, and at the same moment a lookup of `login.live.com`, most likely Windows' account broker fetching a token for it | Edge's sign-in component (OneAuth, through the Windows Web Account Manager) looks up the Microsoft account signed in to Windows and fetches its profile | Yes | `--disable-features=msOneAuthWAM` |
| HTTPS and QUIC to `dns.google` (8.8.8.8, 8.8.4.4 port 443), 5 to 9 s after launch, from the network service | Chromium's automatic secure DNS: the PC uses Google's DNS servers, so the runtime upgrades to Google's DNS over HTTPS and sends it a test query (`www.gstatic.com`). It was not looking up Microsoft's servers: the page looks nothing up, and the sign-in lookup above goes through Windows' own resolver | Yes | `--disable-features=DnsOverHttpsUpgrade` |
| A lookup of `wpad` on the local network | Windows' "Automatically detect settings" proxy option, which the runtime follows | Yes | `--no-proxy-server` |
| SmartScreen (`*.smartscreen.microsoft.com`) | Checks pages and downloads against Microsoft's lists | Yes (not seen) | `msSmartScreenProtection` in `--disable-features`, and the `IsReputationCheckingRequired` setting off |
| Edge configuration and experiments (`config.edge.skype.com`), component updates | Downloads settings, experiments, certificate lists and the like | Yes (not seen) | `--disable-background-networking --disable-component-update` |
| Autofill and password saving (not seen) | Edge features that can consult online services | Yes | Both settings off (the board has no forms) |

With all of these, Tack and its web view processes opened **no connections and
no sockets at all** in 3 minutes, nor in 150 s of a first launch with a new
profile, nor while the board was revealed and tucked. Before, every launch opened 3 to
6 TCP connections (to Microsoft and to Google's DNS) and a UDP socket (QUIC to
Google's DNS).

The arguments are in `crates/tack-app/tauri.conf.json`
(`additionalBrowserArgs`), the settings in
`crates/tack-app/src/webview_privacy.rs`.

Tried and left out, because they changed nothing measurable here (the
services they control were not running): `--no-pings`,
`--disable-domain-reliability`, `--disable-client-side-phishing-detection`,
`--safebrowsing-disable-auto-update`, `--disable-spell-checking` and
`SpellCheck`, `--metrics-recording-only`, `--disable-field-trial-config`,
`--disable-sync`, `--no-first-run`, `--no-default-browser-check`,
`--disable-default-apps`. These did not stop the two connections above:
`--dns-over-https-mode=off` (not a switch this runtime knows), and the
`DnsOverHttps`, `msImplicitSignin`, `msEdgeOSAccountInfoSubstrate`,
`msLoadOneAuthInBackground`, `msWebView2EnableFamilySafety`,
`msPrimaryOSAccountInfoCache` and `msEdgeOSAccountInfoManagerCache` features.
`DohProviderGoogle` stops the DNS over HTTPS too, but only for Google's
servers; `DnsOverHttpsUpgrade` stops it for every provider (the runtime's
network log then shows no DNS over HTTPS servers at all).

## What is left

These are outside what an app can switch off:

- **Diagnostic data.** WebView2 follows the Windows diagnostic data setting,
  and Microsoft gives apps no way to override it. With "Send optional
  diagnostic data" on, the runtime can send usage and diagnostic events to
  Microsoft (`*.events.data.microsoft.com`). It was off on the test PC, and
  nothing was sent.
- **Crash reports.** If a WebView2 process crashes, Windows can send a crash
  dump to Microsoft. WebView2 can be told not to
  (`IsCustomCrashReportingEnabled`), but only when the app creates the
  runtime's environment, and Tauri creates it without exposing that option.
- **Runtime updates.** Microsoft Edge Update keeps the WebView2 runtime up to
  date for every app on the PC. It runs on its own, not as part of Tack.
- **Future runtimes.** `msOneAuthWAM` and `DnsOverHttpsUpgrade` are internal
  feature names that a later runtime could rename, and a later runtime could
  add services. The measurement is repeatable (below) and is worth repeating
  after a major runtime update.

WebView2's own group policies were not used: the one that would matter here
(`ExperimentationAndConfigurationServiceControl`) applies to every WebView2
app of the user or the PC, not to Tack alone, and writing machine-wide
policies needs administrator rights.

## What you can do

- Settings > Privacy & security > Diagnostics & feedback: turn off "Send
  optional diagnostic data". This covers WebView2 in every app, not just
  Tack.
- To see for yourself: Resource Monitor (`resmon`) > Network >
  TCP Connections, with the `msedgewebview2.exe` processes whose command line
  contains `--webview-exe-name=tack.exe` ticked.

A firewall rule cannot block Tack's web view on its own: the connections come
from `msedgewebview2.exe`, the same program every other WebView2 app runs.

## Re-measuring

With Tack running (the only `tack.exe`), this lists its web view processes'
connections once a second for 3 minutes:

```powershell
$tack = (Get-Process tack).Id
$seen = @{}
1..180 | ForEach-Object {
    $ids = @($tack) + @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" |
        Where-Object { $_.CommandLine -match 'webview-exe-name=tack\.exe' } | ForEach-Object ProcessId)
    $ids += Get-CimInstance Win32_Process | Where-Object { $ids -contains $_.ParentProcessId } | ForEach-Object ProcessId
    Get-NetTCPConnection | Where-Object { $ids -contains $_.OwningProcess -and $_.RemoteAddress -notin '0.0.0.0', '::', '127.0.0.1', '::1' } |
        ForEach-Object { $seen["$($_.RemoteAddress):$($_.RemotePort)"] = $_.OwningProcess }
    Start-Sleep 1
}
$seen
```

To see which names the runtime looks up, start Tack with
`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` set to the configured arguments plus
`--log-net-log=C:\path\netlog.json` (the variable replaces the configured
arguments, and no web view process from a previous run may still be alive),
then search the file for `"host"` and `DNS_CONFIG_CHANGED`. Names looked up
by the browser process itself (the sign-in component) do not appear there;
`Get-DnsClientCache`, read while Tack starts, shows them.
