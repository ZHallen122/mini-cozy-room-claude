# Install

**English** | [中文](INSTALL.zh-CN.md)

## What you need

- Windows 10/11 and the Steam version of [Mini Cozy Room: Lo-Fi](https://store.steampowered.com/app/3511030/) (started at least once).
- [Claude Code](https://claude.com/claude-code) 2.1.299 or newer.
- [Git](https://git-scm.com/download/win).
- Optional: the [.NET SDK](https://dotnet.microsoft.com/download) 6 or newer, only to build the game mod from source instead of using the prebuilt DLL from the [releases](https://github.com/ZHallen122/mini-cozy-room-claude/releases/latest).

## Let an AI agent install it (recommended)

Start a new Claude Code session and paste the whole block below. The agent checks your setup and backs up your saves first, and asks before it changes anything in the game folder.

````text
Please install the "Mini Cozy Room × Claude Code" mod on this Windows PC. The repository is https://github.com/ZHallen122/mini-cozy-room-claude . Follow the steps below in order, check the result of each step, and don't skip any.

Rules:
- Before changing anything in the game folder or closing the running game, tell me what you are about to do and wait for my OK.
- Never modify, delete or overwrite save files; only back them up.
- Close the game only by closing its main window (PowerShell CloseMainWindow), never by killing processes by name.
- If the same problem fails 3 times, stop and tell me where you are stuck and what you tried.
- Don't git commit or push anything.

1. Check the setup: Windows, git, claude (`claude --version`, needs 2.1.299 or newer). If something is missing, tell me to install it; don't install system software for me. The .NET SDK is only needed if step 7 has to build from source.

2. Find the game folder: by default `C:\Program Files (x86)\Steam\steamapps\common\Mini Cozy Room`; otherwise read Steam's `steamapps\libraryfolders.vdf` to find the library holding app 3511030. Confirm the folder contains `MiniCozyRoom.exe` and `GameAssembly.dll`, and read `Version.txt`.

3. Back up the saves: copy the whole `%USERPROFILE%\AppData\LocalLow\TesseractStudio\MiniCozyRoom\` folder to `%USERPROFILE%\MiniCozyRoom-save-backup\<today's date>\`, check that the file counts match, and tell me the backup path.

4. Install MelonLoader v0.7.3:
   - If the game folder already has a `MelonLoader\` folder: read the version in the first lines of `MelonLoader\Latest.log`. If it is v0.7.3 and the game folder has `winmm.dll`, skip this step; if it is another version or the loader file isn't winmm.dll, ask me first.
   - Download https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.x64.zip from that official address into a temporary folder. Its SHA256 must be 5B2B2F3D1CD42B59EC886C5BDC2663EDAE87A0097A4F4A8F58C0965A99DDA416 ; stop if it isn't.
   - Extract it into the game folder, then rename `version.dll` in the game folder to `winmm.dll` (required: a Windows compatibility setting makes the game skip version.dll; winmm.dll loads reliably). If the game folder already had a winmm.dll, stop and ask me.

5. Generate MelonLoader's assemblies: start the game through Steam, for example in PowerShell `Start-Process "steam://rungameid/3511030"` (don't run the exe directly). Wait until `<game folder>\MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll` exists and `<game folder>\MelonLoader\Latest.log` contains `Support Module Loaded` (1–2 minutes the first time). Then close the game (with my OK, through CloseMainWindow) and wait for the process to exit. If the game crashes or exits on a black screen, show me the last 30 lines of Latest.log and delete `winmm.dll` to undo the install.
   Then edit the newly created `<game folder>\UserData\Loader.cfg` and change only these three settings: `hide_console = true` (no MelonLoader console window), `disable_start_screen = true` (no MelonLoader start screen), `force_offline_generation = true` (no call to MelonLoader's server at every start; it has no data for this game anyway).

6. Get the source: clone the repository into a `mini-cozy-room-claude` folder in my user folder (called "the clone" below; if it already exists, run `git pull` in it). If cloning fails, tell me the error.

7. Install the game mod (the game must be closed):
   - If `Version.txt` (step 2) is `1.07.01`: download https://github.com/ZHallen122/mini-cozy-room-claude/releases/download/v0.4.2/CozyClaude.dll into a temporary folder. Its SHA256 must be a4a164da45708b51ea301a38364f856e378669d2f7b334c38c597990435c1af9 ; stop if it isn't. Copy it to `<game folder>\Mods\CozyClaude.dll`.
   - Otherwise, or if step 10 shows the mod failing to load: build it from source instead. That needs the .NET SDK 6 or newer (`dotnet --list-sdks`; if missing, tell me to install it). In the clone, run `dotnet build game-mod -c Release -p:DeployToGame=true -p:GameDir="<game folder>"`; it must finish with 0 errors.
   - Either way, `<game folder>\Mods\CozyClaude.dll` must exist.

8. Install the Claude Code plugin (use the clone's absolute path):
   `claude plugin marketplace add "<clone>\claude-mod"`
   `claude plugin install cozy-room@cozy-room-local --scope user`
   Then confirm with `claude plugin list` that cozy-room is enabled.

9. Turn on Claude's task tools so the task list can sync into the room (Claude Code doesn't give Claude `TaskCreate` / `TaskUpdate` by default). Tell me first and wait for my OK. Then in `%USERPROFILE%\.claude\settings.json`:
   - If the file exists, copy it to `settings.json.bak` next to it first. If it doesn't exist, create it.
   - Add `"TaskCreate"` and `"TaskUpdate"` to the `permissions.allow` array (create `permissions` and `allow` if missing). Keep every other key and every existing entry; don't add duplicates.
   - Parse the file again to confirm it is valid JSON, and show me the `permissions` part.

10. Verify:
   - Start the game through Steam again, wait 20 seconds, and confirm `MelonLoader\Latest.log` contains `CozyClaude loaded` and `room ready, linking with Claude Code` and no `[ERROR]`.
   - Confirm `%LOCALAPPDATA%\CozyClaude\room.json` exists and its `updatedAt` (a millisecond timestamp) is from the last few seconds.
   - Run `claude -p "Reply with the single word ok."`, then confirm a new .json file appeared in `%LOCALAPPDATA%\CozyClaude\sessions\` and Latest.log has lines like `phase ... -> Working` and `-> Off`.
   - Leave the game running.

11. Finally tell me in a few sentences: where the save backup is, what was installed, which folders and files changed (including settings.json), the result of each check, and "Please start a new Claude Code session so the status line and the room tools appear."
````

## Manual install

1. **Back up your saves**: copy `%USERPROFILE%\AppData\LocalLow\TesseractStudio\MiniCozyRoom\` somewhere else.
2. **Install MelonLoader v0.7.3**: download [MelonLoader.x64.zip](https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.x64.zip), extract it into the game folder, and **rename `version.dll` to `winmm.dll`**.
3. **Start the game once**: start it from Steam and wait 1–2 minutes until `MelonLoader\Il2CppAssemblies\` exists, then close the game.
   Open `UserData\Loader.cfg` in the game folder and set `hide_console`, `disable_start_screen` and `force_offline_generation` to `true`: no console window, no start screen, no server call at startup.
4. **Install the game mod**: download `CozyClaude.dll` from the [latest release](https://github.com/ZHallen122/mini-cozy-room-claude/releases/latest) and put it in the game's `Mods\` folder. The release notes list the SHA256 and the game version it was tested with.
   To build it from source instead (needs the .NET SDK 6+), run this in a clone of this repository (add `-p:GameDir="..."` if the game isn't in the default folder):

```bash
dotnet build game-mod -c Release -p:DeployToGame=true
```

5. **Install the Claude Code plugin**: clone this repository, then run (replace the path with where you cloned it):

```bash
claude plugin marketplace add "C:/path/to/mini-cozy-room-claude/claude-mod"
```

```bash
claude plugin install cozy-room@cozy-room-local --scope user
```

6. **Turn on the task tools** (for task list sync): add `TaskCreate` and `TaskUpdate` to `permissions.allow` in `%USERPROFILE%\.claude\settings.json`, keeping what is already there:

```json
{
  "permissions": {
    "allow": ["TaskCreate", "TaskUpdate"]
  }
}
```

7. Start the game and a new Claude Code session; when the status line shows `🏠 …`, you are done.

## Update

With the game closed, replace `Mods\CozyClaude.dll` with the one from the [latest release](https://github.com/ZHallen122/mini-cozy-room-claude/releases/latest) (or rebuild it from source). Then pull the latest code in the clone:

```bash
git pull
```

Update the plugin and start a new Claude Code session:

```bash
claude plugin marketplace update cozy-room-local
```

```bash
claude plugin update cozy-room@cozy-room-local
```

(Maintainers: after changing `claude-mod/`, bump `version` in `claude-mod/.claude-plugin/plugin.json`, or `plugin update` won't pick up the new code. Build release DLLs with `-p:DebugType=none` so no local paths end up in them.)

## Uninstall

1. Close all Claude Code sessions, start the game once so the room is restored (Claude's task lists are removed too), then close it. To remove the memos and todo lists Claude added, ask Claude to use `room_clear_claude_notes` before that.
2. Remove the Claude Code plugin:

```bash
claude plugin uninstall cozy-room@cozy-room-local
```

```bash
claude plugin marketplace remove cozy-room-local
```

3. Delete `Mods\CozyClaude.dll` in the game folder. To remove MelonLoader as well, also delete `winmm.dll`, `MelonLoader\`, `Mods\`, `Plugins\`, `UserData\` and `UserLibs\`.
4. Delete `%LOCALAPPDATA%\CozyClaude\`. Optionally remove `TaskCreate` and `TaskUpdate` from `permissions.allow` in `~/.claude/settings.json`.

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| No `MelonLoader\Latest.log` appears in the game folder | MelonLoader wasn't loaded. Check whether the file is still called `version.dll` and rename it to `winmm.dll` |
| The game exits on a black screen after installing MelonLoader | The game updated to a Unity version MelonLoader doesn't support yet. Delete `winmm.dll` to undo, and wait for a MelonLoader update |
| The build can't find dlls in `Il2CppAssemblies` | The game hasn't been started since MelonLoader was installed, or `GameDir` is wrong |
| The status line says the room is offline (房间离线) | The game isn't running or the mod didn't load. Check `MelonLoader\Latest.log` for `CozyClaude loaded` |
| The status line shows `⚠ … 不兼容` (incompatible) | That feature no longer matches the game after an update; the mod switched it off and everything else keeps working. The log says why |
| No `🏠` in the status line, Claude has no room tools | Start a new Claude Code session after installing; check with `claude plugin list` that cozy-room is enabled |
| Claude says you are in a co-op room | You are in a co-op room or have visitors; by design the mod leaves the room alone |
| No rain when a tool fails | With muted ambience the rain stays silent, so the game's audible-time experience isn't affected. Set `RainUnmutes` to `true` in `config.json` to unmute for the rain |
| No task list from Claude | It only appears when Claude splits its work into tasks (TaskCreate / TodoWrite), in the game's todo panel, and is removed when the session ends. Check that `TaskCreate` and `TaskUpdate` are in `permissions.allow` in `~/.claude/settings.json` (Claude doesn't get them by default), and that `SyncTaskList` is `true` in `config.json` |
| A black MelonLoader window opens at every start | Set `hide_console = true` in `UserData\Loader.cfg`. Set it back to `false` to watch the log live while troubleshooting; the log is always written to `MelonLoader\Latest.log` |
| The game starts slower than before | MelonLoader starts the .NET runtime and loads its assemblies at every start. Measured on back-to-back starts it adds about 3 seconds (3.1–3.4 s without it, 6.1–6.8 s with it, to the game's own "init done"); the first start after a long break (files not in the system cache) can take around 20 seconds, later starts are fast again |
