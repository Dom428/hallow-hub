# Hallow Hub

All your games in one place - part of the Hallow family with [Hallow Mixer](https://github.com/Dom428/hallow-mixer) and [Hallow Toolkit](https://github.com/Dom428/hallow-toolkit).

- **Finds your games by itself:** Steam, Battle.net and Epic Games, with real artwork from Steam's own on-disk cache (nothing is downloaded). Add anything else by its .exe.
- **Play from one place:** double-click a cover, or select it and press Play.
- **Playtime:** counted for any game running from its install folder, however you start it - today, this week, total, last played.
- **When a game starts:** switch Hallow Mixer to a preset you pick for that game, and/or turn on a Hallow Toolkit game session (Boost apps closed, High performance power). Everything goes back when the game closes.
- **Your Steam hours** show straight away; **update and size badges**; the same game in two stores is **one card** with a Play on choice.
- **Continue playing** row, a colour glow from each game's cover, and a **Stats** page (today, this week, 14-day chart, top games).
- **Game settings:** launch options, run as admin, and **collections** (your own groups, shown as filters).
- **Surprise me** picks something to play; **keyboard shortcuts** and **Xbox controller** support (only while Hub is in front).
- **Storage** page with clean-up suggestions; **session helpers** (apps that open with a game, break reminder, daily limit); **weekly recap**, streaks and CSV export; a **PC check** before you play.
- **Big Picture** full-screen mode for the couch (filters and collections on LB / RB, and Storage with the controller), and a **quick launcher** from the tray icon. Updates itself from GitHub.
- **Hallow family on every game page:** Saves (Hallow Vault: last backup, Back up now, Restore), Clips (Hallow Clips: this game's newest clips) and Overlay (Hallow Overlay on/off per game) - each only when that app is installed. Break reminders show on Hallow Overlay when it's running.
- **One Hallow icon:** Hub's tray icon hosts the whole family - each Hallow app (Mixer, Vault, Overlay, Clips, Remote, Keys and, from 2.13, Guard and Assist) gets a submenu with its own icon (its own actions, Open, Exit) and the others hide their own icons (Settings > One Hallow icon, on by default). From 2.13 any newer Hallow app that answers `menu` on its pipe gets a submenu too, without a Hub update.
- **Hallow Guard and Hallow Assist (2.13):** Guard's safety score is a small pill under *Your PC right now* (click it to open Guard), and Settings shows Guard's score, last scan and threats and whether Assist is in a session - all read from their status.json.
- **Game details:** description, genres, features, developer, release date and screenshots for Steam games (Steam's public store data, cached a month), and filters by genre and feature.
- **Play status and ratings:** Playing, Beaten, Completed, On hold, Abandoned, Plan to play and your own stars, as filters and sorts; a search that also finds genres, developers, notes and collections (genre: dev: status: col: note: store:).
- **Emulators and ROMs:** your own emulator profiles and ROM folders - every ROM becomes a game with playtime, collections and Big Picture.
- **News and charts:** patch notes and news on each Steam game's page (Steam's public news, plain text, Read more in your browser), and hours per week and per month in Stats.
- **Library backup:** collections, notes, pins, hidden games, your names and pictures, settings and playtime history in one zip - import it on another PC (games matched by store and name). No passwords or keys in it.
- **Sale alerts** (opt-in): Hub checks your public Steam wishlist once a day and tells you when something is on sale or below a price you set.
- **Hallow accent:** uses the colour you pick in Hallow Toolkit (game glows keep their own). **Crash reports** stay on this PC - after a crash Hub tells you once and can show the details.
- **What should I play, your way (2.12):** right-click Surprise me for From my backlog (Plan to play, and installed games you've never started - Ctrl+Shift+R), one of your favorites, or one you haven't played in a month. Stats gets a **Your library** section: play status split, backlog, finished games, your ratings.
- **Links on every game (2.12):** Steam store page, community hub and guides, PCGamingWiki and HowLongToBeat (both open the site in your browser - Hub fetches nothing from them), plus links of your own. Also on the right-click menu.
- **Close apps when a game starts (2.12):** pick apps (a browser, a sync app...) to close like clicking X when the game starts, opened again when it closes. Windows, game launchers and the Hallow apps are never closed. The When this game starts card lists everything that happens.
- **Filters that combine** (store, size, last played, playtime) with the chips, search and sort; **hide** tools and test servers (the Hidden filter brings them back). Favorites, search, sort by recent / most played / A to Z / size / store. Lives in the tray so playtime keeps counting.

Built with the C# compiler that ships with Windows (.NET Framework 4.8): run `build.ps1`. No admin needed to install or run.

## Credits

Icons: [Font Awesome Free](https://fontawesome.com) 7.3.1 by Fonticons, Inc., licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) (see FontAwesome-LICENSE.txt). Only the icon outlines Hallow Hub uses are included.
