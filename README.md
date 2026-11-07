# CS2 Discord Presence

[Русская версия](README.ru.md)

Shows your **real-time Counter-Strike 2 match** in Discord: map, game mode, current round, score, your side (CT/T) and K/A/D — not just "Playing Counter-Strike 2".

```
Playing Counter-Strike 2
Mirage • Competitive
Round 7 • CT 4:2 • K/A/D 5/1/3
```

## How it works

CS2 has an official feature called **Game State Integration (GSI)**: the game sends match data as JSON to a local HTTP address. This tool listens on `127.0.0.1:3000`, reads that data and sets it as your Discord Rich Presence via the local Discord client.

- No game files are modified, nothing is injected into the game process.
- GSI is an official Valve feature — it is **VAC-safe**.
- Everything stays on your PC (only the final status text goes to Discord).

## Quick start

1. Download `CS2Presence.exe` from the [Releases](../../releases) page.
2. Run it **with the desktop Discord app open**. It finds your CS2 folder and installs `gamestate_integration_discord.cfg` automatically.
3. **Restart CS2** (the config is only read on game start).
4. Join a match. Your status updates automatically.

Make sure *Settings → Activity Privacy → Share your detected activity* is enabled in Discord.

If the CS2 folder isn't found automatically, copy `gamestate_integration_discord.cfg` (created next to the exe) to:
`...\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\cfg\`

## Use your own Discord application (optional)

1. Go to <https://discord.com/developers/applications> → **New Application**, name it `Counter-Strike 2` (this name is shown in the status).
2. Copy the **Application ID**.
3. Pass it as an argument (`CS2Presence.exe 123456789012345678`) or put it in `appid.txt` next to the exe.
4. *(Optional)* Under **Rich Presence → Art Assets** upload images named `cs2`, `de_mirage`, `de_dust2`, `de_inferno`, `de_ancient`, `de_nuke`, `de_anubis`, `de_vertigo`, `de_overpass`, … — the map image is chosen by the map name.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```
build.bat
```

or

```
dotnet publish src/CS2Presence/CS2Presence.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

The result is `dist/CS2Presence.exe`. Pushing a tag like `v1.0.0` makes GitHub Actions build the exe and attach it to a Release automatically.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Console says `[Discord] Failed to connect` | Start the **desktop** Discord app (not the browser version), and run both apps with the same privileges. |
| Status "CS2Presence is running / Waiting for CS2" never changes | Restart CS2 after the config was installed; check the `.cfg` is in `game\csgo\cfg`. |
| Empty requests in console | Normal while CS2 is in the main menu; data arrives once you're on a map. |
| Map has no picture | Upload an art asset named exactly like the map (e.g. `de_mirage`). |
| Port 3000 is busy | Change `Port` in `Program.cs` and rebuild. |

## Disclaimer

Not affiliated with or endorsed by Valve Corporation or Discord Inc. Counter-Strike is a trademark of Valve Corporation.

## License

[MIT](LICENSE)
