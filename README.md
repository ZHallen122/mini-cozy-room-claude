# Mini Cozy Room × Claude Code

**English** | [中文](README.zh-CN.md)

![Claude Code fixing a bug in the terminal while the character works at the desk, a "Claude is waiting for you" bubble above it and Claude's task list ticked off in the room](docs/media/hero.png)

Makes the room in [Mini Cozy Room: Lo-Fi](https://store.steampowered.com/app/3511030/) follow what Claude Code is doing:

- When Claude starts working, the character sits down at the desk with the laptop; when Claude stops, the character lies down on the bed (or sofa).
- A speech bubble above the character shows what Claude is doing right now: "thinking", "running tests", "editing README.md".
- When Claude waits for you to approve something, the bubble turns into a clear "Claude is waiting for you" reminder and the pet reacts; it goes away as soon as you answer.
- If the game is minimized or hidden in the tray while Claude waits, the taskbar button flashes and the tray tooltip shows the reminder.
- When Claude breaks its work into tasks, a `[Claude] <project> tasks` todo list appears in the room and gets ticked off as Claude goes; it is removed when the session ends.
- Every subagent Claude sends out brings one extra pet along.
- When one of Claude's tools fails, the ambience switches to rain, and back once things work again.
- The stopwatch runs while Claude works, and the end of the hint line under the Claude Code prompt shows the song playing in the room, the timer and how long Claude worked today.
- When the session ends, the room goes back to how it was before.
- When you ask in the conversation, Claude can also add memos and todo lists, run the pomodoro, change the song or change the ambience.

![Demo: Claude Code gets a task, makes a task list, asks for approval, fixes the bug and finishes; the room follows each step](docs/media/demo.gif)

<sub>The demo runs at 1.5× speed. [Full-quality MP4](docs/media/demo.mp4).</sub>

**Install: paste the prompt from [INSTALL.md](INSTALL.md) into Claude Code and it installs everything itself.**

> A personal, unofficial mod, not affiliated with the game's developer. Windows and the Steam version of the game only.

> The mod's texts follow the game's language: Chinese (Simplified or Traditional) shows Chinese, every other language shows English. Switch the language in the game's settings and the room and the Claude Code hint line follow.

## Two parts

| Part | Folder | What it is |
| --- | --- | --- |
| Game mod | [`game-mod/`](game-mod/) | MelonLoader mod `CozyClaude.dll` that changes the room inside the game |
| Claude Code mod | [`claude-mod/`](claude-mod/) | Claude Code plugin `cozy-room` that tells the game what Claude is doing and gives Claude tools for the room |

The two only talk through a few small files in `%LOCALAPPDATA%\CozyClaude\`. No ports, no network.

## Usage

Nothing to do once it is installed: start a Claude Code session with the game open and the room follows along.

To have Claude do something in the room, ask in the conversation, for example:

- "I'm about to study, set the room up so I can focus"
- "Switch the ambience to rain and fireplace"
- "Play a piano piece", "next song", "pause the music"
- "Start the pomodoro"
- "Add a todo in the room: review chapter 3, do the exercises"
- "Remove the memos you added"

The tools Claude uses:

| Tool | What it does |
| --- | --- |
| `room_note` | Adds a memo or a todo list; the title gets a `[Claude] ` prefix |
| `room_clear_claude_notes` | Removes only the memos and todo lists Claude added, never yours |
| `room_timer` | Pomodoro: start / pause / status |
| `room_music` | List the songs you own / play by name / previous / next / pause / resume |
| `room_ambience` | List ambient sounds / play exactly these / add / stop |

The first time Claude uses one of them, Claude Code asks for your permission like for any other tool.

### Task list sync

Claude Code doesn't give Claude its task tools by default, so without them there's no task list to mirror. To turn them on, add them to `permissions.allow` in `~/.claude/settings.json`:

```json
{
  "permissions": {
    "allow": ["TaskCreate", "TaskUpdate"]
  }
}
```

Lists Claude keeps with the older `TodoWrite` tool are mirrored too.

## Guarantees

- **Claude Code is never affected**: whether the game is closed, the mod fails, or a game update breaks it, Claude Code keeps working. The plugin's hooks pass every event on first and do their work afterwards, swallowing any error. Claude's room tools wait at most 5 seconds and say right away when the game isn't running.
- **Breakage is visible**: every feature has its own health check. If one stops matching a new game version, only that feature is switched off, the game shows a one-time notice and the Claude Code status line shows `⚠ <feature> 不兼容` (incompatible). If the mod doesn't load at all, the status line says the room is offline.
- **Co-op is left alone**: in a co-op room or with friends visiting, the mod changes nothing in the room and Claude's requests are refused.
- **Paid content stays paid**: only activities, pets, songs and ambient sounds you already own are used; DLC you haven't bought never shows up.
- **Saves stay intact**: the bubble and the extra pets are never saved. The activity and ambience the mod changed are restored when the session ends, and on the next start after a crash or restart. Memos, todo lists and task lists Claude added can all be removed; your own memos, todos and decorations are never touched. Ticking task-list items doesn't count toward your "todos completed" statistic. A Claude list you retitle counts as yours: the mod stops syncing it and never deletes it.
- **Only your computer controls it**: the mod only accepts files in `%LOCALAPPDATA%\CozyClaude\` that carry this machine's random token, out of reach of web pages and other computers.

### About experience points

The game awards experience over time while music or ambience is **audible**. So the automatic features never unmute anything for you: with ambience muted, the rain on errors stays silent (set `RainUnmutes` to `true` to unmute for the rain). When you ask Claude to play music or change the ambience, that is like pressing play yourself, and experience is earned as usual.

## Configuration

`%LOCALAPPDATA%\CozyClaude\config.json` is created on the first game start (settings added by newer versions are filled in automatically). Restart the game after editing it.

| Key | Default | Meaning |
| --- | --- | --- |
| `WorkActivities` | `[10003, 10002]` | Activity while Claude works; the first one whose furniture is in the current room (desk with laptop, desk with drawing tablet) |
| `RestActivities` | `[10021, 10031, 10011, 10024]` | Activity while Claude is idle (lying in bed, reading on the sofa, reading on the beanbag, reading in bed) |
| `TimerMode` | `"stopwatch"` | Which timer follows Claude: `stopwatch`, `pomodoro`, or `off` |
| `RestoreOnSessionEnd` | `true` | Put the room back when the session ends |
| `RainOnError` | `true` | Switch to rain when a tool fails |
| `RainSoundKey` | `20005` | Which ambient sound is the rain |
| `RainUnmutes` | `false` | Unmute muted ambience for the rain |
| `PetNudgeWhileWaiting` | `true` | The pet reacts while Claude waits for you |
| `WaitingText` | `""` | Bubble text while Claude waits for you; empty means the default in the current language |
| `MaxSubagentPets` | `3` | Maximum number of extra pets for subagents |
| `ShowActivity` | `true` | Show what Claude is doing in the bubble |
| `SyncTaskList` | `true` | Mirror Claude's tasks into a todo list in the room |
| `AttentionWhenHidden` | `true` | While the game is minimized or in the tray and Claude waits: flash the taskbar and change the tray tooltip |
| `AttentionSound` | `false` | Also play the timer sound you picked in the game |
| `AttentionRestoreWindow` | `false` | Also bring the game window back |
| `Language` | `"auto"` | `auto` follows the game's language; `zh` or `en` force Chinese or English |

## Uninstall

1. Close all Claude Code sessions, start the game once so the room is restored (task lists are removed too), then close it. To remove the memos and todo lists Claude added, ask Claude to use `room_clear_claude_notes` first.
2. Claude Code side:

```bash
claude plugin uninstall cozy-room@cozy-room-local
```

```bash
claude plugin marketplace remove cozy-room-local
```

3. Game side: delete `Mods\CozyClaude.dll` in the game folder. To remove MelonLoader as well, also delete `winmm.dll`, `MelonLoader\`, `Mods\`, `Plugins\`, `UserData\` and `UserLibs\`.
4. Data: delete `%LOCALAPPDATA%\CozyClaude\`.

## Known limitations

- Tested only with game version 1.07.01 (Unity 6000.3.22f1) and Claude Code 2.1.299. After a game update MelonLoader may fail to start ([MelonLoader#1159](https://github.com/LavaGang/MelonLoader/issues/1159)); the status line then says the room is offline and Claude Code is unaffected.
- The mod's texts exist in Chinese and English only; games in other languages get English. The room tools' replies to Claude are in Chinese (Claude relays them in your language).
- With MelonLoader the game starts a few seconds slower, more on the first start after a long break; see the troubleshooting table in [INSTALL.md](INSTALL.md).
- The game saves about every 30 seconds, so mid-session the save may hold the activity Claude set; the mod restores it when the session ends or on the next start.
- The pomodoro state isn't saved (the game doesn't save it), so it is back to "ready" after a restart.
- The co-op guard was tested by simulation in single player, not in a real co-op room.
- Other programs running as the same Windows user could in principle read the local token; that is the limit of talking through local files.

## TODO

Ideas not built yet (pick one up if you like):

- [ ] **A room per project**: switch to a different room or decoration preset depending on the repo Claude works in, and back when the session ends (changes the save, so the original must be recorded and restored).
- [ ] **Celebrate milestones**: a successful git commit, a merged PR or all tests passing gives the character a celebration effect and a toast in the game.
- [ ] **Sleepy when the context fills up**: when Claude's context is nearly full and about to be compacted, the character yawns and shows the sleep effect.
- [ ] **Time of day**: coding late at night turns the room to night (the game's day cycle already follows the real clock, so first check whether to interfere at all).
- [ ] **Pomodoro back to Claude Code**: when the pomodoro reaches a break, Claude Code shows "time for a break" and the status line counts down the break.
- [ ] **Daily summary memo**: at the end of the day, a `[Claude]` memo with how many hours you worked with Claude, how many sessions, and which projects.
- [ ] **Character emotes**: a happy emote when a task succeeds (the game's emotes are built for co-op; whether they work in single player is unverified).
- [x] **Localization**: the mod's texts switch between Chinese and English with the game's language.
- [ ] **More languages**: the game also supports Korean, Japanese and 12 more; the mod shows English for those.
- [ ] **Verify the co-op guard in a real co-op room**.

Not planned: broadcasting Claude's state to co-op friends (would break "co-op is left alone"), or game rewards based on Claude usage (would change progression).

## Development

- Build the game mod: `dotnet build game-mod -c Release -p:DeployToGame=true` (needs MelonLoader installed and the game started once, because it references the assemblies MelonLoader generates).
- Test the Claude Code mod: `claude plugin test claude-mod`.
- After changing `claude-mod/`, bump `version` in `claude-mod/.claude-plugin/plugin.json`, then update the plugin as described under "Update" in [INSTALL.md](INSTALL.md).
- Game internals, pitfalls and verification notes are in [MODLOG.md](MODLOG.md).
