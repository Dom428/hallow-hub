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

## Next

### 2.8 - Game details
- Description, genres, features (co-op, controller...), developer, publisher, release date, Metacritic and screenshots for Steam games, from Steam's public store data - fetched when you open a game (and slowly while Hub is idle), cached for a month
- Filter by genre and feature, together with the other filters

### 2.9 - Emulators and ROMs
- Your own emulator profiles (exe + arguments with {rom}) and ROM folders Hub scans; ROMs become games with playtime, collections and everything else

### 2.10 - Play status, ratings and a better search
- Play status per game (Playing, Beaten, Completed, On hold, Abandoned, Plan to play) and your own rating, both as filters and sorts
- Search that also looks at genres, features, developer, notes and collections, with genre: / dev: / status: words
### Gaps vs Playnite / GOG Galaxy
What Playnite, GOG Galaxy 2.0 and LaunchBox do that Hub doesn't (yet):
- Game details (descriptions, genres, tags, developer, release date, screenshots) and filtering by them - **buildable** (Steam's public store data, no key) - 2.8
- Emulators and ROM scanning (Playnite, LaunchBox) - **buildable** (local only) - 2.9
- Play status (beaten, completed, abandoned, backlog) and your own ratings (Playnite, Galaxy) - **buildable** - 2.10
- Search across tags, genres, developer and notes (Playnite) - **buildable** - 2.10
- Achievements (Galaxy, Playnite add-ons) - **not now**: Steam's no-key source only works for public profiles and gives one game per request; every other store needs a sign-in or key
- Friends, chat and activity feeds (Galaxy) - **not buildable**: needs each store's sign-in, and Hub won't hold your passwords
- Owned-but-not-installed games from every store (Galaxy, Playnite) - **not buildable** without store sign-ins or API keys (Steam's owned-games list needs a Web API key)
- Consoles and other launchers through integrations (PlayStation, Xbox cloud, Amazon, itch.io, Humble - Galaxy, Playnite) - **mostly not buildable**: sign-ins; itch.io / Amazon local installs could be added later
- Import from Playnite or GOG Galaxy - **not buildable here**: Playnite keeps its library in a LiteDB database and Galaxy in SQLite - both binary formats Hub can't read without bundling a third-party library (and neither is installed on this PC to test)
- Installing and updating games for stores other than Steam (Galaxy for GOG) - **not buildable**: the store's own client does it
- Add-ons, scripting and custom themes (Playnite, LaunchBox) - **not planned**: Hub stays one small app; it has the Hallow accent and cover size / list view
- Cloud sync of the library between PCs (Galaxy) - **partly done**: 2.7 library backup moves it by zip; no cloud without an account
- Videos, 3D boxes and arcade cabinets (LaunchBox Big Box) - **not planned**: needs large media downloads
- HowLongToBeat times (Playnite add-on) - **not buildable**: no public API
### Fixes from real use
- Things found while playing with Hub (Big Picture animations, companion apps, launch options and the play flow haven't been tried with real games yet)

### Later
- Friends and achievements: need each store's sign-in or an API key, so only if it can be done without Hub holding your passwords
