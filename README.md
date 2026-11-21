# Quick Match

Random co-op matchmaking for Slay the Spire 2. Press **Play Now** and you are grouped with other players who
want the same run, straight into the game's own co-op character select. macOS and Windows players play
together.

## Features

- **One button**: open **Multiplayer**, pick **Quick Match** and press **Play Now**.
- **Match Options**: the game mode (Standard, or Blitz and Dread once unlocked), the party size (2, 3, 4 or
  Any), the Ascension and voice chat on or off. You only meet players who chose the same.
- **Voice chat** over Steam, with nothing to set up. Your mic starts muted, and you can mute anyone or turn
  them down. A player who chose no voice is never heard, whatever they press.
- **Invite links**: **Copy Invite Link** in a lobby gives a link to paste into Discord or a chat.
- **Starting short**: a lobby that has waited 2 minutes one or two players short can start with those there,
  if everyone agrees.
- **Blocking and Vote to Remove**: block a player and you are never matched with them again. A vote takes a
  player out of a lobby or a run.
- **Drops**: a player who drops comes back with **Rejoin Run**, and when the host leaves, another player takes
  the run over.

## Installing

Subscribe on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3843352549) and start
the game. The first time, the game asks whether to load mods: accept.

- Everyone in the group needs Quick Match and the same game version. No other mod is needed.
- If you installed Quick Match by hand before, delete its folder from the game's `mods` folder, or the game
  keeps using that copy.
- With mods loaded the game uses a separate save profile, so its unlocks start fresh there.
- Everyone in a group needs the same gameplay mods turned on. Quick Match itself does not count.
- Steam must be running.

## Compatibility

- The game's stable branch (v0.111.1) and beta (v0.113.0 or later). Stable and beta players never meet, and
  Blitz and Dread need the beta.
- Quick Match 0.27.0 to 0.28.0 play together, and with no earlier release.
- A game update can turn a Quick Match feature off until Quick Match is updated. The log says which.

## Limits

- Blocks and votes go by Steam ID, so a new Steam account is a new player. They keep a player away from you in
  Quick Match; they do not stop someone playing badly or leaving.
- When the host leaves, the run carries on from its last autosave, so a fight in progress starts over.

## Reporting a problem

Post in the Workshop page's Discussions, or open an issue here. Say what you did and what happened, and paste
the lines starting with `[QuickMatch]` from the game's log:

- macOS: `~/Library/Application Support/SlayTheSpire2/logs/godot.log`
- Windows: `%appdata%\SlayTheSpire2\logs\godot.log`

## Building from source

Needs the .NET SDK 10.0 (`global.json`) and the game from Steam. Quick Match is built once for each supported
game version, against that version's game files, so the build also needs the stable branch's files. Download
them with Steam's console (`steam://open/console`, then `download_depot 2868840 2868843`).

```sh
export STS2_STABLE_DATA=<stable files>/data_sts2_linuxbsd_x86_64
mod/build.sh                               # builds mod/out/QuickMatch
mod/QuickMatch/install.sh                  # builds and copies it into the game's mods folder (macOS)
cd mod/QuickMatch.Tests && dotnet test     # the tests, against the installed game
```

`mod/QuickMatch/Directory.Build.props` holds the macOS Steam paths. On Windows, set `Sts2DataPath` to
`<install>\data_sts2_windows_x86_64`. If the installed game is on the stable branch, also set `STS2_BETA_DATA`
to a copy of the beta's data folder.

## License

MIT, see `LICENSE`. Quick Match contains no code or files from the game.
