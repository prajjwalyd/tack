# Privacy: what goes over the network

## Tack itself

Nothing goes out. Tack has no HTTP client, no update check and no analytics,
and it never connects anywhere. The board is a local page whose content
security policy only allows Tack's own files, `data:` images and the local
IPC channel, so even a bug in the page could not reach the internet. Your
board lives in `%APPDATA%\Tack\board.json`, unsaved captures in
`%LOCALAPPDATA%\Tack\Captures` and notes in `%LOCALAPPDATA%\Tack\Notes`.

Out of the box Tack does not listen for connections either. The one thing
that does is yours to switch on: **Use on your phone** (below).

## Your board on your phone

With **Use on your phone** on, Tack serves the board to your own devices
over [NetBird](https://netbird.io), a private network built on WireGuard.
Tack still makes no outgoing connections; your phone connects to your PC.
Who gets in, layer by layer:

1. **NetBird decides who can reach this PC at all.** Only devices in your
   NetBird network, allowed by its access policies, can send this PC
   anything; NetBird drops the rest before Tack sees it. For the tightest
   setup, give your phone and PC a NetBird group of their own and a policy
   that allows only the phone to reach the PC on TCP port 7717.
2. **Tack listens only on the PC's NetBird address** (100.x.y.z, port
   7717): never on your Wi-Fi, office network or the internet.
3. **Every request must come from a device NetBird lists**, which Tack reads
   from NetBird's own client (`netbird status`, run from NetBird's install
   folder only). WireGuard ties each address to a device's key, so the
   sender is who NetBird says it is.
4. **Nothing of the board reaches a device you have not allowed.** The first
   time a device asks, the PC shows "Let pixel use your board?". Tack
   remembers the answer by the device's WireGuard key, not its name or
   address, so another device cannot pass for it. Remove a device in the
   same window and it has to ask again. A device you turn down can ask again
   no more than every 30 seconds, and no more than three can wait at once.
5. **Other websites on your phone cannot use it.** Requests must name your
   PC in the Host header (which defeats DNS rebinding) and, when the browser
   says where they come from (Fetch Metadata, Origin), come from Tack's own
   page. The page itself loads nothing but its own files.
6. **What a device can send is limited:** JPEG, PNG or text only, 40 MB at
   most, and 30 pins a minute.

The connection is plain HTTP inside NetBird's tunnel. WireGuard encrypts it
from your phone to your PC, so your carrier or a café's Wi-Fi sees only
encrypted traffic; the browser still says "Not secure", because it cannot
see the tunnel. Your board never leaves your PC except to be shown on your
allowed devices, and photos you pin from the phone are saved on the PC like
any capture. Turn the switch off and the port closes.

## Text, the clipboard and notes

Text reaches the board only when you put it there: **Win+Alt+C** on a
selection, or text or a link dropped on the board. Tack never reads text
off the clipboard on its own. Its clipboard listener only looks at
pictures Snipping Tool puts there (see the README), and it ignores the
clipboard altogether while Win+Alt+C runs.

What Win+Alt+C does with the clipboard:

- It asks the app in front to copy the selection (it sends that app
  Ctrl+C), reads that copy once, and then puts back what you had on the
  clipboard before, so a paste still pastes what it did. The copy put back
  is marked to stay out of Windows' clipboard history and cloud clipboard,
  which already hold the original, so it does not show up twice.
- The app's own copy is an ordinary copy: if clipboard history (Win+V) is
  on, the selection appears there, as it would after Ctrl+C.
- What comes back is every part of the clipboard that is plain data: text,
  pictures, HTML and rich text, copied files, and any privacy markers. A
  live object copied from Office as an embedded object comes back as its
  text, rich text and picture, but no longer as an object.
- Content the copying app marked private is never pinned: password
  managers mark their copies with `ExcludeClipboardContentFromMonitorProcessing`,
  `CanIncludeInClipboardHistory` set to 0, or `Clipboard Viewer Ignore`, and
  Tack honours all three ("Not pinned: marked private"). If what you had on
  the clipboard before was marked private, it goes back with its marks.
- If nothing was selected, nothing is read and the clipboard is left as it
  was.

Notes are plain UTF-8 text files, at most 20 KB each, in
`%LOCALAPPDATA%\Tack\Notes`; board.json holds only their paths, never their
text. Like captures, they leave with their print: unpinned, cleared, or
aged out after a week unless kept, a note's file goes to the Recycle Bin,
never deleted outright.

Links are never fetched: no page title, no preview, no icon. The domain a
link note shows is read from the link itself. Opening one (double-click,
Ctrl+Enter, or Open link) hands it to your default browser, and only links
that start with `http://` or `https://` are ever opened. A picture dropped
on the board is saved as it is in `Captures`; a dragged web image that
arrives only as its address is pinned as a link, never downloaded.

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
