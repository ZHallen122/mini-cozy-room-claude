# MODLOG — Mini Cozy Room × Claude Code

Journal for the mod. Anything not written here is lost at the next context compaction.

## Facts

- Game: Mini Cozy Room: Lo-Fi, Steam app 3511030, `Version.txt` = 1.07.01.
- Install: `C:\Program Files (x86)\Steam\steamapps\common\Mini Cozy Room`.
- Engine: Unity 6000.3.22f1, IL2CPP (`GameAssembly.dll`). No anti-cheat. Co-op over Steam P2P (`TSMultiPlay.*`).
- Saves: `%USERPROFILE%\AppData\LocalLow\TesseractStudio\MiniCozyRoom\` (Easy Save 3 `.es3`, Steam Cloud synced).
  `Player.log` is there too. The game saves on change with a ~30 s cooldown.
- Loader: MelonLoader 0.7.3 (GitHub release, sha256 of `MelonLoader.x64.zip` =
  5B2B2F3D1CD42B59EC886C5BDC2663EDAE87A0097A4F4A8F58C0965A99DDA416), installed as **`winmm.dll`**.
- Decompiled interop: `%USERPROFILE%\minicozyroom-decomp\` (outside the repo, never commit);
  `sig-index.txt` there is a grep-able list of every public member.
- Claude Code 2.1.299 function-hooks plugin API (`register.ts`, `$`), not settings.json command hooks.

## Backups / restore

- `um backup` zip: `%USERPROFILE%\.universal-modder\backups\minicozyroom-saves\20261021-150141.zip` (15 files).
- Plain copy: `C:\Code_Project\Open_source\mini-cozy-room-mod-save-backup\2026-10-21\`.
- Restore: close the game, copy the backup over the save folder (or `um backup restore`).
- Game folder before MelonLoader: 559 files (list was kept in the session scratchpad).
  To uninstall the loader: delete `winmm.dll`, `MelonLoader\`, `Mods\`, `Plugins\`, `UserData\`, `UserLibs\`.
- The player's own state before testing: activity 10011 (beanbag reading), activityCount 94, exp 315, level 43,
  ambience muted (fireplace 20001 flagged), pet 10002. All verified back to these values at the end.

## Game internals used

| What | API |
| --- | --- |
| character activity | `Player.Instance.ChangeActivityKey(k)` then `World.characterScript.UpdateActivity()`, `World.petScript.UpdateActivity()`, `World.UpdateActivityEffect()`; read `GetCurrentActivityKey()` |
| activity table | `Player.ActivitySystem.GetActivityInfoDic()`: 10002/10003 Desk, 10011/10012 Beanbag, 10021/10024 Bed, 10031/10032 Sofa, 10099 Stand, plus 100x1 variants for Bridge/Seat/Tube |
| furniture present | `World.CurrentRoom.IsDecoInteractable(decorableType)` |
| stats | `Player.UserData.playerUserData.activityCount` (+1 per ChangeActivityKey), `.exp` |
| co-op | `Singleton<MultiplayManager>.Instance`: `IsMultiplayMode`, `IsInLobby()`, `IsTransitioning` |
| pet | `World.petScript` (`Pet`): `TouchPetReAction()`; sprites via `Singleton<PetSpriteLoader>`: `LoadPetGroupAsync(key, CancellationToken.None)`, `GetActions(key)["Idle"]`, `ReleasePetGroup(key)`; table `GameInfo.Instance.Pets` |
| timer | `FindObjectOfType<Il2Cpp.TimerManager>()`: `StopwatchSystem.Toggle()/IsPlaying`, `SessionTimerSystem.Start()/Pause()`. Stopwatch is not saved |
| ambience | `Singleton<Il2Cpp.AmbiencePlayer>`: `PlayAmbientSound(c)`, `StopAmbientSound(c)`, `Mute/UnmuteAmbientSound()`, `IsMute`; caches `Player.SoundSystem.GetAllAmbienceCaches()`; rain 20005 (non-DLC); DLC check `DLCManager.IsDLCSoundAccessible` |
| UI | overlay canvases are ScreenSpaceOverlay, 1728x1080 reference, scale ~1.48; font `ToastManager.multiplayText.font` (NotoSansKR SDF with CJK fallback); toast `ToastManager.ShowToast(text)` |
| memo (v2) | `Singleton<Il2Cpp.MemoManager>`: `_cacheDic` (key → `MemoCache`), `AddNewMemo(1, false)` creates + opens a memo synchronously (new key = the one not in `_cacheDic` before; bumps `createMemoCount`), `GetCache(k).GetMemoUserData().SetTitle(t)`, `MemoCache.EditContent(c)`, `_activatedMemos[k].SetData(cache)` redraws, `TryDeleteMemo(k)` deletes at once (no dialog). Saved in `UserContent_Alpha.es3`; `UserContentStore.RequestSave()` |
| todo (v2) | `Singleton<Il2Cpp.TodoListManager>`: `_cacheDic`, `AddNewTodoList(1, false)`, `TodolistCache.AddTodoItem(-1).EditContent(t)`, `RemoveTodoItem(item)`, `GetOrderedTodoItems()`, `_activatedTodoList[k].SetData(cache)`, `TryDeleteTodolist(k)` |
| pomodoro (v2) | `TimerManager.SessionTimerSystem`: `Start()`, `Pause()`, `CurrentStatus` (Ready/Session/Break), `CurrentTimeSeconds`. Not saved: a restart is back to Ready |
| music (v2) | `Singleton<MusicPlayer>` (`Il2Cppcozyhouse.musicplayer`): `Musics`, `PlayBgm(music, false)` (also switches the current album to the song's), `PlayNextTrack()/PlayPreviousTrack()` (start playback when paused), `PauseBgm()/ResumeBgm()`, `IsMute`, `CurrentMusic`. Ownership: `Music.Cache` null (local file) or `!IsDLC` or `DLCManager.IsDLCSoundAccessible(cache)`. 200 owned songs here; the ambience DLC (AmbiencePack, keys 205xx) is not owned |
| window | the game window covers the whole screen (2560x1600) transparently; the room UI is docked on the right |

## Gotchas

1. **`version.dll` proxy is ignored.** Windows has the compat layer `~ DISABLEDXMAXIMIZEDWINDOWEDMODE` on
   `MiniCozyRoom.exe` (HKCU AppCompatFlags\Layers). The shim engine loads System32 `VERSION.dll` before
   UnityPlayer's imports resolve, so the game-folder proxy never loads. Fix: rename the proxy to `winmm.dll`.
2. MelonLoader issue #1159: some Unity 6000.3 builds crash under MelonLoader. This one loads and runs
   (warning: `Class::Init signatures have been exhausted, using a substitute!`, harmless so far).
3. `ScreenCapture.CaptureScreenshot` throws `MissingMethodException` (ReadOnlySpan) through the interop; use
   `um win shot` from outside.
4. `ChangeActivityKey` alone changes nothing on screen, and bumps `activityCount`. See table above.
5. **Audible ambience earns exp** (315 → 575 in ~5 min during a test; reverted). Muted ambience earns none.
   `PlayAmbientSound` implicitly unmutes. `UnmuteAmbientSound` brings back whatever played at mute time, so stop
   sounds *after* unmuting, and mute *after* re-playing the originals.
6. **`ChangePetVisualOnlyAsync` is a preview**: its sprite group is released after `PetSpriteLoader`'s grace TTL, so
   clones go blank after a few seconds, and it may drop a reference on the real pet's group. The mod uses bare
   SpriteRenderers holding their own loader reference instead.
7. Pet keys 10000/20000/30000 are "no pet" entries (icon `Pet_Empty`), blank sprites. They are in the unlocked list.
8. Interop optional `CancellationToken ct = null` parameters NRE: pass `Il2CppSystem.Threading.CancellationToken.None`.
9. The save on disk can lag the room by ~30 s. A restore must keep its journal until the save file is newer
   than the restore, or a restart reloads Claude's activity and the next episode records it as "original".
10. PowerShell `Get-Content`/`Set-Content` without `-Encoding utf8` mangles UTF-8 source with Chinese text.
11. Claude Code refuses a foreground `sleep N` and backgrounds it, ending the turn: a good-looking "bug" in tests.
12. **`UnmuteAmbientSound` is asynchronous**: it re-plays the at-mute sounds a few frames later, so stopping them right
    after unmuting doesn't stick. v2's ambience `set` keeps enforcing its set for 3 s (`RoomActions.Tick`).
    (Gotcha 5's "unmute first, then stop" order is necessary but not sufficient.)
13. A new todo list comes with one empty row. Filling that row in via `EditContent` doesn't redraw the open window
    (still shows the placeholder); remove it with `RemoveTodoItem` and append the items instead.
14. `AddNewMemo` bumps `createMemoCount`; v2 writes the old value back (like `activityCount` in gotcha 4).
15. Playing music earns exp too (385 → 595 over a few minutes of tests, plus `bgmPlayedCount`/`bgmPlayedTime_Sec`).
16. Installed from the local folder marketplace, the plugin is read in place: `claude plugin list` shows "Read from"
    `claude-mod/` (the manifest docs: a plugin loaded in place from a local-path marketplace isn't pinned by
    `version`), so a new session or `/reload-plugins` picks up edits. (An early note here said it was a cache copy
    needing a version bump + `plugin update`.) Bump `version` anyway when shipping.
17. Plugin tool hooks have a 10 s budget and `$.clock.sleep` counts against it; `$.fs` has no delete, so an
    abandoned command is overwritten with `{"cancelled":true}` and the game also drops anything older than 8 s.
18. **`UI_TodoList.SetData(cache)` flushes every row's input field into the data before rebuilding**, so text
    changed in the data while the window is open is overwritten with the old text. Redraw an existing window with
    `RefreshList(keepScroll: true, suppressFlush: true)`; only a just-created list uses `SetData`.
19. `TodoItemCache.Complete()` bumps `completeAmountTodolist`. TaskSync snapshots every int stat of
    `PlayerUserData` by reflection and writes back whatever changed (`StatSnapshot`).
20. `TrayIcon` (namespace `Il2CppUtils`) caches the game window (`unityWindowHandle`), knows whether it is hidden in
    the tray (`isHiddenFromTaskbar`), and exposes `UpdateTooltip`; the original tooltip is in
    `notifyIconData.szTip` ("迷你舒适房间：Lo-Fi" in Chinese). `FlashWindowEx(FLASHW_TRAY|FLASHW_TIMER)` on that
    window flashes the taskbar button until `FLASHW_STOP`.
22. Language: `GameManager.CurrentLocale` gave nothing usable here; `LanguageManager.currentLanguage` holds names
    like `ChineseSimplified` / `English` / `Spanish`. The settings menu path is `GameManager.ChangeLanguage(int)`,
    whose index order is NOT `LanguageManager.supportedLanguages`' order (0 English, 1 ChineseSimplified, 4 Spanish).
    `LanguageManager.ChangeLanguage(string)` alone changes the name but not the UI.
21. Claude Code's task list comes two ways: TaskCreate/TaskUpdate (+ `classic.TaskCreated` / `TaskCompleted`
    events, verified firing in `claude -p` 2.1.299) and legacy TodoWrite (whole list each call). The plugin handles
    both; `PostToolUse` of TaskCreate is a backup for the id.
23. A Bash call that exits non-zero comes back as an error and fires `PostToolUseFailure`, not `PostToolUse`
    (checked 2026-10-23 in a `claude -p` transcript: `npm run test:bad` → `is_error: true`). So "PostToolUse of a
    test command" already means exit 0; the output check is for pipes (`npm test | tail`) that hide the exit code.
24. Bash's `tool_response` carries `gitOperation` (`commit: {sha, kind}`, `pr: {number, action: merged |
    auto-merge-enabled | ...}`), classified by Claude Code itself; the plugin trusts it before parsing stdout.
25. Context fill: `session.measure` (after each turn; `e.context.percent`, `changed` names `context`) and
    `$.session.usage()` (free without `breakdown`). `percent` is absent until the first response and after a
    compaction. Unity UI confetti with plain gravity leaves the screen in ~2 s: cap the fall speed.

## Route

MelonLoader + Il2CppInterop, C# `net6.0` mod (`game-mod/`). Claude Code side: function-hooks plugin `cozy-room`
(`claude-mod/`) hooking `classic.*` events, writing `%LOCALAPPDATA%\CozyClaude\sessions\<sid>.json` with a 20 s
heartbeat; status line via `$.ui.status` from `room.json`.

## Verification (2026-10-21)

- Headless `claude -p` runs with the plugin (`--plugin-dir`, then installed): SessionStart → rest (bed), prompt →
  desk, Stop → bed, SessionEnd → back to beanbag 10011; subagent count 1→0, tool failure → error, refused Write →
  waiting=1, all through real hooks. Screenshots taken with `um win shot` and inspected.
- Synthetic session files: `×2` bubble, 3 subagent pets visible after the loader's grace TTL, rain switch and
  recovery (with RainUnmutes temporarily on), co-op guard (simulated via dev `coop on`), crash/restart restore.
- `claude plugin test claude-mod`: 11 pass. `tsc` strict against the 2.1.299 declarations: clean.
  `claude plugin validate`: passes (only the public directory's LICENSE rule).
- Game with the plugin's files absent / game closed: Claude Code sessions finish normally.
- Dev console: create `<game>\UserData\CozyClaude\dev\` and write commands to `dev\cmd.txt` (`dump`, `state`,
  `find`, `srinfo`, `amb`, `coop on|off`, `act`, ...). Removed at the end, so it is off.

## v2 active operations (2026-10-21)

- Plugin 0.2.0 registers 5 tools at `session.start` (`$.tool.register`, deferred behind ToolSearch):
  `mcp__cozy-room__room_note|room_clear_claude_notes|room_timer|room_music|room_ambience`. Each `tool.call` hook
  checks `room.json` (offline / co-op / loading → immediate `{deny}`), writes `commands\<uuid>.json` with the token
  and `at`, polls `results\<uuid>.json` every 150 ms for up to 5 s. Failures come back as `{deny: reason}`.
- Game mod 0.3.0: `CommandBridge` (thread pool timer, 200 ms) validates token/age/id and queues; `Mod.RunCommands`
  executes on the main thread via `RoomActions` (re-checks the co-op guard, `Health.Command` per feature: Notes,
  Pomodoro, Music, Ambience — self-checked at room ready, shown in room.json health and the status line).
- Claude's notes: `[Claude] ` title prefix + `claude-notes.json` ledger; clear deletes only ledger entries whose
  current title still equals the recorded one.
- Verified in the real game (screenshots `um win shot`): memo, todo (2 items, no blank row), clear (user's memo 0
  "Blank" and todo 0 untouched, deletion persisted across restart), pomodoro start/pause (status line
  `🍅 专注 19:23`), play by name, next, pause, ambience set (incl. from muted), stop, list (DLC filtered),
  wrong token ignored, co-op refusal (dev `coop on`), simulated incompatibility (dev `break Music`: toast,
  `⚠ Claude 换歌不兼容`, tool refusal text). Two real `claude -p` runs: all five ops by plain-language request, then
  clear + pause + stop. Game closed → immediate "not running" error; faked fresh room.json with no game → 5 s
  timeout error, command withdrawn, never run on the next start.
- `claude plugin test claude-mod`: 16 pass. `tsc` strict against 2.1.299 declarations: clean. `claude plugin
  validate`: passes (LICENSE/icon directory notes only).
- Restored after testing: exp 595 → 385, bgmPlayedCount 252 → 248, bgmPlayedTime_Sec 34140 → 33960; music back to
  Lo-Fi Chill Sound Track - 17 paused; ambience fireplace + muted; Claude notes cleared. A fresh game load diffed
  identical to the pre-test `stats` dump (every int stat, notes, music, ambience, timer). Dev console removed.
- Backups for this round: `um backup` `minicozyroom-saves\20261021-170051.zip`; plain copy
  `C:\Code_Project\Open_source\mini-cozy-room-mod-save-backup\2026-10-21-v2\`.
- Dev console additions: `stats`, `stat <field> <value>`, `streset`, `todoitems <key>`, `break <Feature>`.

## Task list, activity line, hidden-window reminder (2026-10-22)

- Plugin 0.3.0: session files gain `project` (cwd folder name), `activity` (one line from the tool about to run:
  `describeTool`, e.g. 正在跑测试… / 在改 X / 思考中…) and `tasks` (`[{id, subject, done}]`, max 30).
- Game mod 0.4.0:
  - `TaskSync` mirrors each live session's tasks into a `[Claude] <project> 任务` todo list (ledger kind `tasks` with
    `Sid` and task id → item key), ticks/renames/removes items, deletes the list when the session ends. A list the
    player deleted or retitled is marked `Suppressed` and left alone.
  - The bubble shows the waiting reminder first, otherwise the activity line in a quieter style (translucent, no
    bob, smaller text); it follows the character every frame (`RoomDriver.FollowBubble`).
  - `Attention`: only while the window is minimized/hidden and Claude waits — taskbar flash + tray tooltip; optional
    timer sound (`Player.TimerSoundKey` through `AudioManager.PlayOneShot(..., Productivity)`) and window restore.
  - Config gains `ShowActivity`, `SyncTaskList`, `AttentionWhenHidden`, `AttentionSound`, `AttentionRestoreWindow`;
    `Config.Load` now writes new keys back into an existing `config.json`.
- Verified in the real game: fake sessions (activity bubble, list created/ticked/renamed live/unticked/removed,
  `completeAmountTodolist` kept at 20 on every tick), minimized window → tooltip "Claude 在等你确认" + flashing taskbar
  button (screen capture), restored to "迷你舒适房间：Lo-Fi" and no flash when the waiting ended; visible window →
  nothing. Real `claude -p` (haiku) with TaskCreate/TaskUpdate: list created 18:17:03, ticked 3×, removed 18:17:17
  at session end; activity lines 思考中… / 正在跑命令… seen.
- Not verified: `AttentionSound` and `AttentionRestoreWindow` (off by default); the "player took over the list"
  path (same title check as v2's clear, which v2 verified).
- Stats/notes diffed identical to the pre-test dump. Backups: `um backup` `20261022-180442.zip`; plain copy
  `mini-cozy-room-mod-save-backup\2026-10-22-features\`. Dev console additions: `tray`, `retitle <key> <title>`.
- `claude plugin test claude-mod`: 23 pass; `tsc` strict: clean.

## Language (2026-10-22)

- Game mod 0.4.1 / plugin 0.4.0. `Lang` picks zh for any Chinese game language, en otherwise (config `Language`
  auto|zh|en), re-read every 2 s. In-game texts (waiting reminder, tray tooltip, incompatibility toast, Claude's
  operation toasts, task list title) and room.json's timer text follow it; room.json carries `lang` and the plugin
  writes the status line in that language. The plugin now sends activity as codes (`test`, `edit:README.md`, ...);
  `Activity.Text` words them, and passes through finished text from older plugins. Tool replies to Claude stay
  Chinese.
- Verified in game: Chinese → bubble 在改 README.md, list "[Claude] demo 任务"; settings-path switch to English
  (`gmlang 0`) → full English UI, bubble "Claude is waiting for you" / "Running tests…", list "[Claude] docs-site
  tasks", room.json lang en; back to Chinese (`gmlang 1`), persisted across a restart. Plugin tests 26 pass.
- Dev console additions: `langs`, `setlang <name>`, `gmlang <index>`.

## Hint-line width and promo (2026-10-22)
- The hint-line tail is cut by the terminal when it's too long. The plugin now stores the summary as versions from
  longest to shortest (`statusLines`) and the PromptHint hook keeps the longest that fits (`fitLine`): it measures
  display width (CJK and emoji 2 cells, combining marks / variation selectors / ZWJ 0), subtracts the hint text,
  the engine's separator and a margin, and while Claude works another `WORKING_PILLS` (42) cells for the working
  indicators that aren't part of `props.hint`. The song shows only while music plays. Plugin 0.4.2.
- On newer models Claude Code leaves the task tools out by default. What turns them on (official docs, Task tool
  availability): `CLAUDE_CODE_ENABLE_TODO_TOOLS=1` (shell, or the `env` block of a settings file, applied live),
  `--allowedTools`, `--tools`. `permissions.allow` does NOT: checked 2026-10-23 from each session's init tool list
  (default: none; `permissions.allow` TaskCreate/TaskUpdate: none; `env` var via `--settings` and via
  `~/.claude/settings.json`: TaskCreate/TaskGet/TaskList/TaskUpdate). A plugin can't do it on its own by documented
  means: plugin `settings` only applies `agent` / `subagentStatusLine`, and `$.settings` is documented as read-only.
  Decision: the installer doesn't change Claude Code settings; task list sync works where the session has the tools,
  and the troubleshooting table tells users how to turn them on themselves.
  Note `--allowedTools` is variadic: put the prompt before it or it's swallowed as a tool name.
- Promo (`docs/media/hero.png`, `demo.gif`, `demo.mp4`): demo Windows Terminal (130 columns) in an empty
  `C:\cozy-demo` project, started with `CLAUDE*`, `CLAUDECODE`, `NO_COLOR`, `FORCE_COLOR`, `ANTHROPIC_*` stripped
  from the environment (otherwise no colour and a child-session warning). Terminal and game recorded as separate
  windows (`um win record --hwnd` / `--exe`, window-only, no desktop), permission prompts approved with Enter only
  after checking the demo window is foreground, composed side by side with ffmpeg (game cropped to the room panel
  and the todo window). Game switched to English for the shoot. Take 6 was recorded with plugin 0.4.1, so the
  working frames show the tail cut to "· C…"; 0.4.2 fixes that.

## Celebrations and the sleepy character (2026-10-23)

- Plugin 0.5.0: `celebrationFor` (state.ts) on `PostToolUse`: `gitOperation.pr.action == merged` or `gh pr merge` +
  "Merged pull request" (not auto-merge) → `merge`; `gitOperation.commit` or git's `[branch sha]` line → `commit`;
  a test command (describeTool `test`) whose output has a runner pass line and no failure count/marker → `tests`;
  GitHub MCP `merge_pull_request` → `merge`. Backgrounded/interrupted calls never count. Session files gain
  `celebrate`, `celebrateAt`, `context` (whole percent or null; reset on SessionStart incl. compact).
- Game mod 0.5.0: `Effects` (own overlay canvas, order 1490 under the bubble): confetti bursts (commit 40 warm,
  tests 32 green, merge 3×~40) with capped fall + flutter; "z"s beside the head while sleepy. `RoomDriver.UpdateFun`
  plays a celebration once if under 15 s old (tests at most every 90 s), bubble cheer for 3.5 s, pet
  `TouchPetReAction` wrapped in `StatSnapshot`. Sleepy from the most recently active session's `context` ≥
  `SleepyAtContextPercent` (80); resting bubble "上下文快满了，好困… N%"; same session dropping below → "睡醒了，
  精神满满！" for 4 s. Features `Celebrate`, `Sleepy` under Health. Bubble order: waiting > cheer > awake > activity
  > sleepy (resting).
- Verified in the real game: dev `party merge` (bubble + confetti), fake session files (`fake.py` in the session
  scratchpad; dev `only fake-` hides other sessions): resting at 88/91% → bed, sleepy bubble, z's; working at 91% →
  activity bubble + z's; context 88 → 40 → awake line; `celebrate=commit` through the bridge → confetti + 提交成功！;
  English (`gmlang 0`): "Context nearly full, so sleepy… 91%", "PR merged!". Real `claude -p` (haiku) in a throwaway
  repo: `npm run test:bad` (prints "1 passed", exits 1) → no party; `npm test` → `celebrating tests` 0.45 s after the
  result; commit → `celebrating commit`; session file `context: 4`. Not verified: a real session reaching 80%
  (fake files only), a real `gh pr merge` (unit tests only).
- Stats: activityCount and every other int stat unchanged by the mod; exp/bgm counters rose from the player's own
  music while the game ran and were written back. Fresh load diffed against the pre-test dump: same except
  `bgmPlayedCount` +1 from that launch itself, the playlist position, and the unsaved pomodoro display. Backup:
  `mini-cozy-room-mod-save-backup\2026-10-23-fun\`. Dev console additions: `party <kind>`, `only <prefix>|off`.
  Dev folder removed. Plugin tests 35 pass, `tsc` strict clean, `claude plugin validate` passes.

## Real merge, second promo (2026-10-23)

- **`gh pr merge` prints nothing when stdout isn't a terminal** (as under the Bash tool), and Claude Code attached no
  `gitOperation` to that result: a real merge in a demo session got `stdout: ""` and no party. Plugin 0.5.1 counts a
  successful (`PostToolUse`, so exit 0) `gh pr merge` as a merge unless it has `--auto` / `--disable-auto` / `--help`
  or the output reports a failure (`not mergeable`, `error`, `failed`, `could not`: a `|| true` that masked the exit
  code). Verified live: PR #3 of a throwaway private repo merged by a demo session → `celebrating merge` 3.7 s
  after the approval. Passing tests also verified live (`claude plugin test` in this session → `celebrating tests`).
- Promo v2 (`docs/media/hero.png`, `demo.gif`, `demo.mp4`; X cut `x-promo.mp4`, not committed): no task list. Merge
  take = demo Windows Terminal in `C:\cozy-demo` (prompt: view, `gh pr merge 3 --merge`, confirm with
  `gh pr view 3 --json state`; `--allowedTools 'Bash(gh pr view *)'` so only the merge asks), recorded as before.
  Sleepy clip staged: dev `only fake-` + a `fake-sleepy` session (working at 91% → resting at 91%), game window only.
  Game in English for the shoot; stats diffed identical afterwards; language restored; dev console removed.
- **Open:** after the demo session's Stop the room stayed in Working (bubble "Running a command…") while this
  development session was idle with a background command running; take 2 (same setup) did go to rest. Watch the
  session file around a turn end with a background task before trusting "idle = resting" in recordings; isolate
  takes with dev `only <sid prefix>` if needed. Not this session's file, as far as checked: watched across a later
  turn end (with a background command running), it went `working=False` at the Stop and stayed there.

## Log
- 2026-10-21: backups; MelonLoader as `winmm.dll`; probe; full mod; plugin; verification above; plugin installed
  at user scope from the local folder marketplace `cozy-room-local`. Nothing committed.
- 2026-10-21 (later): v2 active operations — plugin 0.2.0 tools + game mod 0.3.0 command channel; verified and
  restored as above. Nothing committed.
- 2026-10-22: MelonLoader console hidden / start screen off / offline generation (`UserData\Loader.cfg`); task
  list sync, activity line, hidden-window reminder (plugin 0.3.0, game mod 0.4.0).
- 2026-10-22 (later): status line moved to the prompt hint line's tail (desktop: short pinned form); mod texts
  follow the game's language (game mod 0.4.1, plugin 0.4.0).
- 2026-10-22 (later): width-aware hint line (plugin 0.4.2); promo hero/GIF/MP4 in `docs/media/`, shown in both
  READMEs.
- 2026-10-23: celebrations + sleepy character (plugin 0.5.0, game mod 0.5.0). Not released: INSTALL still points at
  the v0.4.2 DLL.
- 2026-10-23 (later): plugin 0.5.1 counts a silent successful `gh pr merge`; promo v2 (merge celebration +
  sleepy character, no task list).
- 2026-10-23 (later): released v0.5.1 (game mod 0.5.0 DLL, sha256 ab577729...a550, built with `DebugType=none`;
  plugin 0.5.1). INSTALL points at it. Release DLL loaded in game: all 17 features ok.
