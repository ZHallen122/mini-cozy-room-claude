# CLAUDE.md

Mini Cozy Room × Claude Code: the room in Mini Cozy Room: Lo-Fi (Steam app 3511030) follows what Claude Code is doing,
and Claude can operate the room. Two parts that talk only through files in `%LOCALAPPDATA%\CozyClaude\`:

| Part | Folder | What it is |
| --- | --- | --- |
| Game mod | `game-mod/` | MelonLoader 0.7.3 mod `CozyClaude.dll`, C# `net6.0` over Il2CppInterop (Unity 6000.3.22f1, IL2CPP) |
| Claude Code plugin | `claude-mod/` | Function-hooks plugin (mod) `cozy-room`: `hooks/register.ts` + pure logic in `state.ts` / `actions.ts` |

Files in the data folder: `sessions\<sid>.json` (plugin → game, rewritten on every event + 20 s heartbeat),
`secret.key` (random token every file must carry), `room.json` (game → plugin), `commands\` / `results\` (room
tools), `claude-notes.json` (ledger of what Claude added), `journal.json` (what to restore), `config.json`.

`MODLOG.md` is the full journal: game internals used, numbered gotchas, verification logs. Read it before touching
an area, and add to it when you learn something the next session would otherwise rediscover.

## Rules that never bend

- **Claude Code is never affected.** Every hook passes the event on (`next(e)`) and does its file work unawaited;
  every registration ends with `.catch(($, e, next) => next(e))`. Room tools wait at most 5 s and deny with a reason.
- **Breakage is visible, and partial.** Each feature runs under `Health`; one that stops matching the game is
  switched off alone, with a toast and a note on the line under the prompt.
- **Don't touch co-op or paid content.** Do nothing in a co-op room or with visitors; use only songs, ambience and
  pets the player owns (`DLCManager.IsDLCSoundAccessible`).
- **Saves and stats stay the player's.** Nothing the mod adds is saved for good: activity/ambience go back at
  session end (journal), Claude's notes are tracked in the ledger, and any int stat a game call bumps
  (`activityCount`, `createMemoCount`, `completeAmountTodolist`, ...) is written back (`StatSnapshot`).
  Automatic features never unmute audio: audible music/ambience earns exp.
- **Only this machine's Claude Code controls the room** (token-signed files, commands older than 8 s dropped).
- **Don't change the user's Claude Code settings** (install or runtime). The task tools are off on newer models;
  we document `CLAUDE_CODE_ENABLE_TODO_TOOLS=1` and leave it to the user.

## Develop

```bash
# game mod: build and copy into <game>\Mods (game must be closed; needs MelonLoader installed and the game
# started once, because it references MelonLoader\Il2CppAssemblies)
dotnet build game-mod -c Release -p:DeployToGame=true
# plugin: tests (state.test.ts, actions.test.ts) and what the engine will accept
claude plugin test claude-mod
claude plugin validate claude-mod
```

- Type-check the plugin with `tsc --strict` against the `claude-code.d.ts` the `plugin-authoring` skill writes for
  the running build (`tsc` isn't on PATH: `npx -p typescript tsc`). Keep logic in `state.ts` / `actions.ts` so it
  stays testable without the engine.
- The plugin is installed at user scope from the local folder marketplace `cozy-room-local` pointing at
  `claude-mod/` (`claude plugin list` shows "Read from" this folder). A new session or `/reload-plugins` picks up
  edits. Still bump `version` in `claude-mod/.claude-plugin/plugin.json` when shipping a change.
- Game mod version is in `game-mod/src/Mod.cs` (`MelonInfo`). Plugin and game mod versions move independently.
- Release DLLs: build with `-p:DebugType=none` (otherwise the PDB path leaks local paths), attach `CozyClaude.dll` to
  a GitHub release, and update the URL + SHA256 in both install prompts (`INSTALL.md`, `INSTALL.zh-CN.md`).

## Test in the real game

- **Back up the saves first** (`%USERPROFILE%\AppData\LocalLow\TesseractStudio\MiniCozyRoom\`), and dump stats before
  (`stats`) so you can diff after; restore anything a test changed (exp, play counts, notes, music, ambience).
- Close the game only with `CloseMainWindow` on `MiniCozyRoom.exe`, never by killing processes by name. Start it
  through Steam (`steam://rungameid/3511030`), not the exe.
- Dev console: create `<game>\UserData\CozyClaude\dev\`, write commands to `dev\cmd.txt`, read `dev\out.txt`
  (`state`, `stats`, `find`, `amb`, `coop on|off`, `break <Feature>`, `langs`, `gmlang <index>`, ...). Delete the
  folder when done so it's off.
- Real Claude Code runs: `claude -p` with `CLAUDE*`, `CLAUDECODE`, `NO_COLOR`, `FORCE_COLOR`, `ANTHROPIC_*` removed
  from the environment (inherited ones give no colour and a child-session warning). To test task sync, pass
  `--allowedTools TaskCreate TaskUpdate` after the prompt (the flag is variadic and swallows a prompt placed after).
- To check which tools a session has, read the `system/init` event's `tools` from
  `claude -p --output-format stream-json --verbose --max-turns 1`. Don't trust memory or the local types file over
  the official docs (code.claude.com/docs) and a real run.
- Screenshots / recordings: `um win shot` / `um win record --exe MiniCozyRoom.exe` (window only, no desktop).
  The game window is a full-screen transparent overlay; the room UI is docked on the right.
- Logs: `<game>\MelonLoader\Latest.log` (`CozyClaude loaded`, `room ready, linking with Claude Code`,
  `phase X -> Y`).

## Gotchas (numbers refer to MODLOG)

Game and interop:
- The `version.dll` proxy never loads (a Windows compat shim loads System32's first): MelonLoader goes in as
  `winmm.dll` (1). `UserData\Loader.cfg`: `hide_console`, `disable_start_screen`, `force_offline_generation` = true.
- `ChangeActivityKey` alone doesn't redraw and bumps `activityCount`; call the `UpdateActivity` chain (4).
- Ambience: `PlayAmbientSound` unmutes; `UnmuteAmbientSound` re-plays the at-mute sounds a few frames later, so keep
  enforcing the wanted set for a few seconds (5, 12).
- `ChangePetVisualOnlyAsync` is a preview that goes blank; keys x0000 are "no pet" (6, 7).
- Interop optional `CancellationToken` defaults NRE: pass `CancellationToken.None` (8).
- The save lags the room by ~30 s: keep the journal until the save file is newer than the restore (9).
- Todo lists: a new list has one empty row (remove it, don't fill it); `SetData` flushes input fields over changed
  data, so redraw open windows with `RefreshList(true, true)` (13, 18).
- Language: read `LanguageManager.currentLanguage`; switch through `GameManager.ChangeLanguage(int)` whose index is
  NOT the `supportedLanguages` order (0 English, 1 ChineseSimplified, 4 Spanish) (22).
- `ScreenCapture.CaptureScreenshot` fails through the interop: screenshot from outside (3).

Claude Code side:
- The room summary is the `tail` of the `PromptHint` render site (terminal only). `$.ui.status` draws a yellow `⚠`
  + plugin-name notice, so it's only the desktop fallback, in a short form.
- Terminal width: CJK and default-emoji (🏠🎵⏰🍅) are 2 cells; avoid ambiguous-width symbols (⏸ ⏱ ⚠ ♪). The tail
  keeps the longest variant that fits, leaving `WORKING_PILLS` cells while Claude works (`fitLine`).
- Tool hooks have a 10 s budget and `$.clock.sleep` counts; `$.fs` has no delete, so an abandoned command is
  overwritten with `{"cancelled":true}` (17).
- Task list events: TaskCreate/TaskUpdate (+ `classic.TaskCreated`/`TaskCompleted`) and legacy `TodoWrite`; both are
  handled (21). The task tools are off by default on newer models; `permissions.allow` does NOT turn them on.
- Claude Code backgrounds a foreground `sleep N` and ends the turn: not a bug in the mod (11).

Windows tooling:
- PowerShell `Get-Content`/`Set-Content` without `-Encoding utf8` mangles Chinese text; edit with the Edit tool (10).
- `wt.exe`: global options (`--size`) go before `new-tab`.
- ffmpeg is at `%LOCALAPPDATA%\universal-modder\ffmpeg\bin\` (not on PATH); newer builds take `-/filter_complex file`
  instead of `-filter_complex_script`.

## Docs

- README and INSTALL exist in English and Chinese (`*.zh-CN.md`): change both together, same structure.
- `INSTALL.md` holds the agent install prompt; every SHA256, URL, log line and step number in it must match reality
  (check after any release or log-text change).
- Promo media lives in `docs/media/`. Keep personal info (desktop, username, email) out of anything recorded.
