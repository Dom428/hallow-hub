# Hallow Hub roadmap

| Version | What |
|---|---|
| **1.0** | Library (Steam, Battle.net, Epic, added games), playtime, Mixer preset and Toolkit game session per game, tray |
| **1.1** | Merged duplicates (Play on), Steam hours, update and size badges, Continue playing, colour glow, Stats |
| **1.2** | Game settings (launch options, run as admin), collections, Surprise me, keyboard, Xbox controller |
| **1.3** | Updates from GitHub and What's new, notices installs and updates by itself, Steam download progress, --play and desktop shortcuts |
| **1.4** | Storage page with drive bars, clean-up suggestions, uninstall through the launcher, Biggest first, Needs update / Not played lately filters |
| **1.5** | EA app, Ubisoft Connect, GOG, Riot and Xbox games; your own name, cover and banner per game |
| **1.6** | Apps that open and close with a game, break reminder, daily limit per game, session history |
| **1.7** | List view, cover size, moving banner, notes per game, pins |
| **1.8** | Weekly recap, play streaks, export to CSV, opt-in Discord posts (through Hallow Toolkit's link) |
| **1.9** | PC check next to Play (CPU, RAM, GPU temperature, free space); start recent games from the phone dashboard (Hallow Toolkit 5.3) |
| **2.0** | Big Picture full-screen controller mode, quick launcher from the tray icon |
| **2.1** | Play on picks the copy you actually play (Steam and Battle.net last played), clean-up offer for an unused second copy |
| **2.2** | Hallow family cards on every game page: Saves (Vault), Clips, Overlay on/off; Hub pipe (ping, status) and status.json; reminders on Overlay |
| **2.3** | Hide and unhide games, combined filters (store, size, last played, playtime), Big Picture collections and Storage with the controller |
| **2.4** | One Hallow icon: Hub hosts the family tray menu (tray-host, notify, per-app submenus, Start / Exit, Open Hallow Toolkit) |
| **2.5** | Hallow accent from Hallow Toolkit (live), crash reports kept on this PC with a notice, lastCrash in status |
| **2.6** | Steam news and patch notes on game pages (cached 6 h), playtime charts per week and month |
| **2.7** | Library backup and import (one zip, no secrets), opt-in Steam wishlist sale alerts |
| **2.8** | Game details from Steam (description, genres, features, developer, release, screenshots), genre and feature filters |
| **2.9** | Emulator profiles and ROM folders - every ROM a game with playtime, collections and Big Picture |
| **2.10** | Play status and your own rating (filters and sorts), search across genres, developer, notes, collections and status |
| **2.11** | Hallow Keys in the Hallow icon (its submenu, Start when installed) |
| **2.12** | Surprise me from your backlog, links per game (Steam, PCGamingWiki, HowLongToBeat, your own), close apps when a game starts, Your library on Stats, pipe open / quit |
| **2.13** | Hallow Guard and Hallow Assist in the Hallow icon (tray-host lists them; any Hallow app that answers `menu` is hosted), app icons in the menu, Guard's safety score next to the PC check, Guard / Assist status in Settings |
| **2.14** | Power plan while a game runs (per game or all games, back after; powercfg /setactive only), family cards for Hallow Guard (score, weekly change, hardening), Hallow Shield (blocked today / week) and Hallow Assist (your PCs, Connect) |
| **2.15** | Light and dark mode: follows the family mode from Hallow Toolkit (light / dark / same as Windows, live), Settings > Look (Follow Hallow / Light / Dark), light palette with a darkened accent for text, no glows, title bar to match |
| **2.16** | Light mode readability fixes: green / amber / lilac and the accent as text darkened to >= 4.5:1 on every light panel (accent darkened against Panel 2, same rule as Hallow Toolkit 5.16), filled accents that read under 4.5:1 (violet) get a darker fill with white text, open dialogs restyle live on a mode or accent change |
| **2.17** | The whole family: a Family page with every Hallow app (Play / Protect / Everyday) - installed / running / not installed, version, a live line from each status.json through a privacy filter (counts and states only), quick actions over the pipes (VPN Connect fastest / Disconnect on IKEv2 only, Torrents Pause / Resume all, Financials Lock now, Open, Settings), Install hands off to Hallow Setup or the releases page, Hallow Places health from your own address; newer apps in a fixed order in the Hallow icon with Start a Hallow app |
| **2.18** | Switch Steam accounts: the accounts Steam remembers (loginusers.vdf) on a Steam button and in the Hallow icon; Steam closed, AutoLoginUser set and Steam reopened (never during a game, no passwords) |
| **2.19** | Keep the family running after updates: Hub remembers the running Hallow apps (app, exe, last seen) and starts one again with --tray when it went away and its exe changed within 5 minutes (new exe stable for 5 s, one retry); never an app you quit or one that wasn't running; Hallow Browser gets a note only; one note, held during games; Settings switch + per-app opt-out; Start the apps I usually run on the Family page; pipe `family-watch`; percentages without a space (45%) everywhere Hub shows them |
| **2.20** | The newer apps' newer data on the Family page: the privacy filter reads VPN 1.2.6 `nextProtocol` / `needsUac` (two flags) / `target` (a target word, country or server name only) / `windowsLogin`, Tasks `busyApps` / `busyPercent` / `notResponding`, Home `demo`, Email 1.1 per-account `label` (only when `labelUserChosen` and not `labelHasAddress` / no @, else "Account N") / `unread` / attention (its plain text, else Hub's own words), Gear devices (kind, name, battery, charging, `lastKnown`, `waiting`, time - never `id`) and `game`, Mixer 3.27 `spotifyAdMuted` / `adsMutedToday`; VPN buttons from `needsUac` and `windowsLogin`, `connect fastest --keep-target` for 1.2.6+ (plain `connect fastest` before); Tasks' pipe `quick` on a background thread (used while < 10 s old, asked at most every 2 s); a Gear card in Play with Refresh (`refresh`, its answer in words through the status line; off during a game) and Settings; Tasks' "Not responding..." opens its Processes; Home Demo; Mixer card |
| **2.21** | Family checkup: Family page > Run checkup and a quiet weekly run (Settings, on; never during a game; one note when something needs a look) - 8 areas, each OK / look at this / problem with a fix or steps: versions (Setup 1.18 `--list --check --json`, numeric compare; Update -> `HallowSetup.exe --update <App>`), the usual apps not running (Start --tray), Hallow data in `Packages\Claude_*\LocalCache\{Roaming,Local}\Hallow*` newer than / missing from the real folder (status.json, *.tmp and crash reports aside) with Bring settings back (quit over the pipe and wait, back up to `%USERPROFILE%\Hallow data backup\<date>` - never Documents, newer files only, opaque byte copies, restart if it was running; an app that won't quit or can't be backed up is left as it is; Hub's own folder at its next start), VPN `account.dat` exists (never read), Keys vault exists, Gear running, Windows' per-app notification switch (read only), Vault 1.16 `family`, Keys 2.14 `safety`, the Authenticator's newest backup on the PC, Guard 1.10 `cfaBlocked` and Vault's blocked destinations, HKCU Run + StartupApproved for Hub, Gear and the usual apps; status.json `checkup`, pipe `checkup` / `checkup run` |
| **2.22** | Real-world checks on the Family page: 9 checks, each turning green by itself from the app's own files - VPN `connected` + `protocol` NordLynx, VPN `leakCheck`, Torrents `leak-drill.json`, Torrents `vpnAuto.state` / `p2pOnly.serverP2p`, Authenticator `autobackups\auto-*.hallowauth`, Browser settings.json `Extensions[]` NordPass (`Store` + `Enabled`), Mixer `adsMutedToday` / `spotifyAdMuted`, Clips `lastSave` auto "Kill sound" (a heard kill is a hint), Overlay `live.fpsLow` (a hint only - you confirm the line). A pass is kept, a later failure turns it red; Worked / Didn't work / Reset; Try it = `open <page>` over the app's pipe. `real-checks.json`, pipe `realchecks` |

## Next

### Gaps vs Playnite / GOG Galaxy
What Playnite, GOG Galaxy 2.0 and LaunchBox do that Hub doesn't (yet):
- Game details (descriptions, genres, tags, developer, release date, screenshots) and filtering by them - **buildable** (Steam's public store data, no key) - 2.8
- Emulators and ROM scanning (Playnite, LaunchBox) - **buildable** (local only) - 2.9
- Play status (beaten, completed, abandoned, backlog) and your own ratings (Playnite, Galaxy) - **buildable** - 2.10
- Search across tags, genres, developer and notes (Playnite) - **buildable** - 2.10
- Random game from your backlog (Playnite's random game picker, Galaxy's backlog view) - **buildable** - 2.12
- Links per game (Playnite's Links: store pages, wikis, guides, your own) - **buildable** (links only, nothing fetched) - 2.12
- Actions before a game starts (Playnite's game scripts: close apps, power plan) - **buildable** - 2.12 closes the apps you pick and opens them again after; 2.14 switches to the power plan you pick (per game or all games) and back after
- Library statistics (Playnite's statistics: completion, status split, ratings) - **buildable** - 2.12
- Achievements (Galaxy, Playnite add-ons) - **not now**: Steam's no-key source only works for public profiles and gives one game per request; every other store needs a sign-in or key
- Friends, chat and activity feeds (Galaxy) - **not buildable**: needs each store's sign-in, and Hub won't hold your passwords
- Owned-but-not-installed games from every store (Galaxy, Playnite) - **not buildable** without store sign-ins or API keys (Steam's owned-games list needs a Web API key)
- Consoles and other launchers through integrations (PlayStation, Xbox cloud, Amazon, itch.io, Humble - Galaxy, Playnite) - **mostly not buildable**: sign-ins; itch.io / Amazon local installs could be added later
- Import from Playnite or GOG Galaxy - **not buildable here**: Playnite keeps its library in a LiteDB database and Galaxy in SQLite - both binary formats Hub can't read without bundling a third-party library (and neither is installed on this PC to test)
- Installing and updating games for stores other than Steam (Galaxy for GOG) - **not buildable**: the store's own client does it
- Add-ons, scripting and custom themes (Playnite, LaunchBox) - **not planned**: Hub stays one small app; it has the Hallow accent and cover size / list view, and light and dark mode - 2.15
- Cloud sync of the library between PCs (Galaxy) - **partly done**: 2.7 library backup moves it by zip; no cloud without an account
- Videos, 3D boxes and arcade cabinets (LaunchBox Big Box) - **not planned**: needs large media downloads
- HowLongToBeat times (Playnite add-on) - **not buildable**: no public API (2.12 adds a HowLongToBeat link that opens the site's search in your browser)
### Fixes from real use
- Family apps stayed closed after updates installed from a sandboxed session (they may be stopped there but not started) - **done in 2.19**: Hub, running on your PC, starts them again
- Settings lost when apps were started inside Claude's sandbox (their AppData went to Claude's LocalCache), Gear not running, Ransomware protection blocking backups into Documents, VPN asking for credentials again - **done in 2.21**: the Family checkup finds them, and Bring settings back moves the sandbox's newer files home
- Things found while playing with Hub (Big Picture animations, companion apps, launch options and the play flow haven't been tried with real games yet)

### Family page: what the apps could share later (2.17 shows less where a field doesn't exist)
- Hallow Setup: a command-line way to install one app (Hub opens Setup and you tick the app), and a fixed install folder Hub can find
- ~~Hallow VPN: say whether the next connect / disconnect needs Windows (UAC); a `connect` that doesn't change the remembered target~~ - **done in 2.20** (VPN 1.2.6 `needsUac`, `--keep-target`)
- Hallow Browser: an `open settings` pipe command, and Shield's blocked count for Hallow Browser itself (Shield's bridge only names Chrome, Edge, Brave, Opera, Vivaldi, Chromium and Firefox)
- Hallow Places: a PC-side status (it's a server with only `/healthz`; Hub asks for its address)
- ~~Hallow Mixer: a status.json~~ - **done in 2.20** (Mixer 3.27: Spotify ads muted); Hallow Remote and Hallow Keys: `open settings`

### Later
- Friends and achievements: need each store's sign-in or an API key, so only if it can be done without Hub holding your passwords
