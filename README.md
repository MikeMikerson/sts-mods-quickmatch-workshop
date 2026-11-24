# Quick Match

Find a Slay the Spire 2 co-op partner without chasing lobby links. Open **Multiplayer > Quick Match** and
press **Play Now** to meet players looking for the same run. On desktop, you can also invite a friend
who plays without Quick Match. Choose **Copy Invite Link** in the lobby and share it through Steam or Discord.
Your friend opens the link, picks a character and presses Embark as usual.

Windows and macOS players can join the same run.

## Features

- **Match Options**: the game mode (Standard, or Blitz and Dread once unlocked), the party size (2, 3, 4 or
  Any), the Ascension and voice chat on or off. Searches match those preferences.
- **Voice chat** starts with your mic muted. Mute another player or adjust their volume during a run.
  Choose **No Voice** to play without sending or receiving voice.
- **Emotes**: the button beside your name pops a status icon over your character, so teammates with Quick Match see that your
  hand can apply Vulnerable, Weak, Strength or Poison, or that you are asking who can. It works with or without
  voice, uses the game's own icons, and you can hide any player's emotes.
- **Guests** use the regular co-op interface. Players with Quick Match can still talk to each other and exchange
  emotes. Guests count toward the party size, and matchmaking can fill the remaining seats.
- **Starting short**: when everyone has Quick Match, a lobby that has waited 2 minutes one or two players short
  can start early if everyone agrees. A party with a guest stays together and waits for its chosen size.
- **Blocking and Vote to Remove**: block a player and you are never matched with them again. A vote takes a
  player out of a lobby or run, including a guest. A majority of eligible Quick Match players other than
  the target must vote yes.
- **Recent players**: the Quick Match window lists the last players you finished a run with, each with a
  button to their Steam profile, so you can add a good teammate as a friend. The list stays on your device.

## Installing

Subscribe on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3843352549).
If you previously installed Quick Match by hand, remove its folder from the game's `mods` directory.
Start the game with Steam running and allow mods when prompted.

## Compatibility

- Everyone needs the same game version and gameplay mods. Players using Quick Match need version 0.31.0
  to play with this release. Friends using the base game can join a desktop host by Steam invite or link.
- Supports stable v0.111.1 and the v0.113 beta. Blitz and Dread need the beta.
- The game uses a separate save profile when mods are enabled. Unlocks start fresh there.
- A game update can turn a Quick Match feature off until Quick Match is updated. The log says which.

## Limits

- Blocks and votes go by Steam ID, so a new Steam account is a new player. They keep a player away from you in
  Quick Match; they do not stop someone playing badly or leaving.
- When everyone embarks with Quick Match, **Rejoin Run** can bring a disconnected player back, and a new
  host can reopen the last autosave. When a guest embarks, the group uses the game's normal disconnect
  behavior for the rest of that run. To get back together, the original host reopens the multiplayer save
  and shares a fresh invite.

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
