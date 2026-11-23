# Quick Match

Find a Slay the Spire 2 co-op partner without chasing lobby links. Open **Multiplayer > Quick Match** and
press **Play Now** to meet players looking for the same run. If someone disconnects, **Rejoin Run** brings
them back. If the host leaves, another player can pick up the last autosave.

Windows and macOS players can join the same run.

## Features

- **Match Options**: the game mode (Standard, or Blitz and Dread once unlocked), the party size (2, 3, 4 or
  Any), the Ascension and voice chat on or off. You only meet players who chose the same.
- **Voice chat** starts with your mic muted. Mute another player or adjust their volume during a run.
  Choose **No Voice** to play without sending or receiving voice.
- **Emotes**: the button beside your name pops a status icon over your character, so everyone sees that your
  hand can apply Vulnerable, Weak, Strength or Poison, or that you are asking who can. It works with or without
  voice, uses the game's own icons, and you can hide any player's emotes.
- **Invite links**: on desktop, use **Copy Invite Link** in a lobby to invite a friend through Discord or chat.
- **Starting short**: a lobby that has waited 2 minutes one or two players short can start with those there,
  if everyone agrees.
- **Blocking and Vote to Remove**: block a player and you are never matched with them again. A vote takes a
  player out of a lobby or a run.
- **Recent players**: the Quick Match window lists the last players you finished a run with, each with a
  button to their Steam profile, so you can add a good teammate as a friend. The list stays on your device.

## Installing

Subscribe on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3843352549).
If you previously installed Quick Match by hand, remove its folder from the game's `mods` directory.
Start the game with Steam running and allow mods when prompted.

## Compatibility

- Everyone needs Quick Match 0.30.0, the same game version and the same gameplay mods. Quick Match itself
  needs no other mod.
- Supports stable v0.111.1 and the v0.113 beta. Blitz and Dread need the beta.
- The game uses a separate save profile when mods are enabled. Unlocks start fresh there.
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
export STS2_STABLE_DATA="/path/to/stable/data_sts2_linuxbsd_x86_64"
mod/build.sh                               # builds mod/out/QuickMatch
mod/QuickMatch/install.sh                  # builds and copies it into the game's mods folder (macOS)
cd mod/QuickMatch.Tests && dotnet test     # the tests, against the installed game
```

`mod/QuickMatch/Directory.Build.props` holds the macOS Steam paths. On Windows, set `Sts2DataPath` to
`<install>\data_sts2_windows_x86_64`. If the installed game is on the stable branch, also set `STS2_BETA_DATA`
to a copy of the beta's data folder.

## License

MIT, see `LICENSE`. Quick Match contains no code or files from the game.
The bundled Concentus Opus codec carries its own license in `LICENSE-Concentus`.
