# Privacy: what goes over the network

## Tack itself

Tack makes one kind of outgoing connection: looking for a new version of
itself, and downloading it when you choose to install it ([Updates](#updates),
below, and you can turn the check at start off). It has no analytics and
sends nothing about you or your board anywhere. Tack draws the board itself,
with no web engine inside, so nothing on the board can reach the internet.
Your board lives in `%APPDATA%\Tack\board.json` (the list of prints and your
settings, including the devices you allowed to use the board from a phone:
their NetBird name, WireGuard public key and when you allowed them), unsaved
captures in `%LOCALAPPDATA%\Tack\Captures` and notes in
`%LOCALAPPDATA%\Tack\Notes`. Tack keeps no log: a released build writes
nothing about what it does anywhere.

Out of the box Tack does not listen for connections either. The one thing
that does is yours to switch on: **Use on your phone** (below).

## Updates

Tack can update itself. By default it looks for a new version once, about 45
seconds after it starts, and never again on its own. Settings has a switch
for that check at start and a button to check now (the tray menu's "Check for
updates" does the same). With the switch off Tack makes no request until you
ask for one.

What a check sends: one HTTPS request to
`github.com/prajjwalyd/tack/releases/latest/download/latest.json`, a small
file that names the newest release, its notes and the address and signature
of its installer. GitHub answers by passing the request on to its download
servers (`githubusercontent.com`); Tack follows that only over HTTPS, at most
five hops. The request carries what any HTTPS request does: your IP address,
the time, and a `User-Agent` of `Tack/<version>` (for example `Tack/0.3.0`).
It does not carry your Windows version, an identifier, a cookie, or anything
from your board. The check and the installer it names both come from Tack's
GitHub releases; Tack insists on HTTPS for every address rather than on a
host name, and it is the signature below, not the address, that decides
whether anything is installed. Tack keeps no record of when it checked
beyond that run. GitHub, as any web host, can see the requests it receives.
A check gives up after 30 seconds and reads at most 1 MB; a download gives
up after 10 minutes.

The installer is downloaded only when you press **Install** on a new
version, from the same release, and Tack does not install it unless all of
this holds:

- the address is HTTPS, and so is every redirect it goes through;
- the version is newer than the one you run (an older release is never
  offered, so an update cannot be turned into a downgrade);
- the installer's signature verifies against the public key built into Tack,
  and was made for that very version. Only whoever holds the matching
  private key, kept as a secret of the repository's release workflow, can
  sign an update. A download that fails the check is thrown away and Tack
  says so.

The installer is saved in a folder of its own under `%TEMP%`
(`tack-update-…`) and removed by a later start of Tack. It runs for your user
alone (no administrator prompt), shows a progress bar and starts Tack again;
like any Inno Setup installer it leaves a `Setup Log` text file in `%TEMP%`.
It keeps your board, your captures and notes and your settings, including
Start with Windows; it never touches your Screenshots folder. Tack follows
Windows' proxy settings for these requests.

## Your board on your phone

With **Use on your phone** on, Tack serves the board to your own devices
over [NetBird](https://netbird.io), a private network built on WireGuard.
For this feature Tack makes no outgoing connections; your phone connects to
your PC.
Who gets in, layer by layer:

1. **NetBird decides who can reach this PC at all.** Only devices in your
   NetBird network, allowed by its access policies, can send this PC
   anything; NetBird drops the rest before Tack sees it. For the tightest
   setup, give your phone and PC a NetBird group of their own and a policy
   that allows only the phone to reach the PC on TCP port 7717.
2. **Tack listens only on the PC's NetBird address** (100.x.y.z, port
   7717): never on your Wi-Fi, office network or the internet.
3. **Every request must come from a device NetBird lists**, which Tack reads
   from NetBird's own client (`netbird status`, run from NetBird's folder
   in Program Files only, which Windows names, and given 5 seconds to
   answer). A connection from anything else is closed before Tack reads a
   byte of it. WireGuard ties each address to a device's key, so the sender
   is who NetBird says it is, and Tack checks that again, against a list at
   most 10 seconds old, before it answers an API request.
4. **Nothing of the board reaches a device you have not allowed.** The first
   time a device asks, Settings opens at its phone page showing "pixel wants
   to use your board" with the device's name and address in NetBird and a
   four-digit code, and the phone shows the same code, so you can tell your
   phone from a look-alike. The window opens without taking the keyboard (if
   it is already open, it flashes in the taskbar instead) and Allow only
   works after a second, so a key you were pressing cannot answer it. Tack
   remembers the answer by the device's WireGuard key, not its name or
   address, so another device cannot pass for it. Remove a device on the
   same page and it has to ask again. A request nobody answers lapses after
   10 minutes. A device you turn down can ask again no sooner than 30
   seconds later, twice as long after each further refusal (an hour at
   most), even if you switch the feature off and on, until Tack restarts; no
   more than three can wait at once.
5. **Other websites on your phone cannot use it.** Requests must name your
   PC in the Host header (which defeats DNS rebinding), and one that says
   it comes from another site (Origin) is refused. Browsers do not send
   Fetch Metadata over plain HTTP, so Tack does not rely on it. Instead,
   anything that acts or may ask you a question must carry a header
   (`X-Tack`) that Tack's own page adds and another site's page cannot add
   to a request without a permission check this server never grants.
   Without it, a request is served only to a device you already allowed and
   never asks. The page itself loads nothing but its own files.
6. **What a device can send is limited:** JPEG, PNG or text only, 20 MB at
   most, 30 pins a minute per device, two at once. The server is strict
   about the rest as well: a request's head is at most 16 KB, a body is read
   only for a pin and only up to its limit, every read and write has a time
   limit, a connection carries one request, at most 16 are open at once, and
   a picture that claims to be huge is refused before it is decoded.

The connection is plain HTTP inside NetBird's tunnel. WireGuard encrypts it
from your phone to your PC, so your carrier or a café's Wi-Fi sees only
encrypted traffic; the browser still says "Not secure", because it cannot
see the tunnel. Your board never leaves your PC except to be shown on your
allowed devices, and photos you pin from the phone are saved on the PC like
any capture. Your phone's browser keeps the pictures of your prints it has
fetched (marked private to that browser, and named by their content), as it
would any page's images; the board's list and the page are not kept. Turn
the switch off and the port closes.

## Text, the clipboard and notes

Text reaches the board only when you put it there: **Win+Alt+C** (or the pin
shortcut you chose) on a selection, text or a link dropped on the board, or
text pinned from an allowed phone. Tack never reads text off the clipboard
on its own. Its clipboard listener only looks at
pictures Snipping Tool puts there (see the README), recognised by its
Windows package or by its program's full path in Windows' own folders, never
by name alone, and skips any copy marked private. It ignores the clipboard
altogether while Win+Alt+C runs.

What Win+Alt+C does, and when it touches the clipboard:

- Selected text is read through UI Automation, the way a screen reader
  reads it, from the app that was in front when you pressed the shortcut.
  Nothing is copied: the clipboard and clipboard history are not touched.
  Browsers, Office, Notepad, terminals and most editors answer this way.
  Text selected in a password box is never read ("Not pinned: marked
  private").
- Anything else (a picture, files, or text in an app UI Automation cannot
  read) is copied by that app: Tack sends that window Ctrl+C, or Ctrl+Insert
  in a terminal, where Ctrl+C would interrupt the running command, and sends
  nothing if another window has come to the front meanwhile. It takes the
  copy only if that app wrote it, reads it once, and then puts back what you
  had on the clipboard before, so a paste still pastes what it did. It puts
  it back the way Windows offers for this, kept out of clipboard history and
  the cloud clipboard, which already hold the original, so it does not show
  up twice. If another app wrote to the clipboard in the meantime, Tack
  leaves that alone instead of putting your old content over it; if putting
  yours back fails, the board says "Couldn't restore your clipboard".
- That copy is the app's own, an ordinary copy: if clipboard history
  (Win+V) is on, the selection appears there, as it would after Ctrl+C, and
  if you turned on clipboard sync across your devices, Windows may send it
  to the cloud clipboard too. Tack's code cannot prevent that.
- What comes back is every part of the clipboard Windows will hand over:
  text, pictures, HTML and rich text, copied files, and any privacy markers
  it carried. A live object copied from Office as an embedded object comes
  back as its text, rich text and picture, but no longer as an object.
- Content the copying app marked private is never pinned: password
  managers mark their copies with `ExcludeClipboardContentFromMonitorProcessing`,
  `CanIncludeInClipboardHistory` set to 0, or `Clipboard Viewer Ignore`, and
  Tack honours all three ("Not pinned: marked private"). If what you had on
  the clipboard before was marked private, it goes back with its marks.
- If nothing was selected, nothing is read and the clipboard is left as it
  was.
- Tack reads the clipboard through Windows' own clipboard and never opens
  it itself, so it never keeps other apps (Snipping Tool, clipboard history)
  out of it. Once Tack has written to it (a print you copy, or your
  clipboard put back), no window of Tack's is left owning it, so no app's
  next copy waits on Tack.
- Copied files are pinned only if they are PNG or JPEG files on a drive
  letter. A network path is never opened: Windows would connect to that
  server and offer it your sign-in.

When a snip from Snipping Tool arrives, Tack looks at the screen around the
pointer to find where the snip came from, so its print can fly from there.
That look stays in memory and is dropped at once: Tack never saves it,
shows it or sends it anywhere.

Notes are plain UTF-8 text files, at most 20 KB each, in
`%LOCALAPPDATA%\Tack\Notes`; board.json holds only their paths, never their
text. Like captures, they leave with their print: unpinned, cleared, or
aged out after a week unless kept, a note's file goes to the Recycle Bin,
never deleted outright.

Tack's files stay on this PC, and Tack only ever cleans up its own:

- A capture or note goes to the Recycle Bin when its print leaves the board.
  Nothing is deleted outright, with two exceptions, both for a file Tack
  wrote a moment ago and never showed: the copy of a snip whose pixels match
  the auto-saved screenshot file pinned in its place, and a note whose text
  is already on the board.
- At startup, captures and notes that board.json no longer lists (left
  behind when a move to the Recycle Bin failed) go to the Recycle Bin too,
  but only if all of these hold: board.json was read and understood and has
  a list of prints; every backup of it (`board.json.bad-*`) could be read,
  and none of them names the file either; the file was last changed more
  than 8 days ago, and before board.json was last saved; and it is a plain
  file, not a link, in a folder that is not a link.
- "Save to Pictures" moves a capture into your Screenshots folder under a
  free name; it never replaces a file that is already there. From then on
  Tack never touches it.
- board.json is written to a temporary file, flushed to disk and swapped in,
  so a crash leaves the old one whole, by a thread of its own (a few times a
  second at most) and once more when Tack quits. One that Tack cannot read
  is copied to `board.json.bad-*` before anything is written over it. When
  Tack starts, a print whose file cannot be read yet stays listed for next
  time, and only files Tack could have written (pictures; `.txt` notes in
  its notes folder) are put back on the board.
- Uninstalling Tack removes its "Start with Windows" entry. Its data stays
  unless you tick "Delete the application data" in the uninstaller, which
  then deletes `%APPDATA%\Tack` and `%LOCALAPPDATA%\Tack`. Updating Tack
  never deletes anything, and your Screenshots folder is never touched.

Links are never fetched: no page title, no preview, no icon. The domain a
link note shows is read from the link itself. Opening one (double-click,
Ctrl+Enter, or Open link) hands it to your default browser, and only links
that start with `http://` or `https://` are ever opened. A picture dropped
on the board is saved as it is in `Captures`; a dragged web image that
arrives only as its address is pinned as a link, never downloaded.

## Checking it yourself

Every connection Tack makes comes from `Tack.exe` itself. With Tack running,
this lists them once a second for 3 minutes. Turn off the update check in
Settings first, or the list also shows the check of GitHub (443) about 45
seconds in. With **Use on your phone** on, it also shows your devices
(100.x.y.z addresses) connecting to port 7717, and Tack runs NetBird's own
`netbird.exe status` now and then to read the list of devices:

```powershell
$tack = (Get-Process tack).Id
$seen = @{}
1..180 | ForEach-Object {
    Get-NetTCPConnection -OwningProcess $tack -ErrorAction SilentlyContinue |
        Where-Object { $_.RemoteAddress -notin '0.0.0.0', '::', '127.0.0.1', '::1' } |
        ForEach-Object { $seen["$($_.RemoteAddress):$($_.RemotePort)"] = $true }
    Start-Sleep 1
}
$seen.Keys
```

A firewall rule for `Tack.exe` blocks all of it (or switch the check off in
Settings).
