# Mini Cozy Room × Claude Code

**English** | [中文](README.zh-CN.md)

**[Install](INSTALL.md)** · [Features](#features) · [Usage](#usage) · [Configuration](#configuration) · [Update](INSTALL.md#update) · [Uninstall](INSTALL.md#uninstall) · [Troubleshooting](INSTALL.md#troubleshooting)

A mod that links [Mini Cozy Room: Lo-Fi](https://store.steampowered.com/app/3511030/) to [Claude Code](https://claude.com/claude-code). While Claude works, the room reflects what it's doing, and Claude can operate the room when you ask.

![Claude Code fixing a bug in the terminal while the character works at the desk, a "Claude is waiting for you" bubble above it and Claude's task list ticked off in the room](docs/media/hero.png)

## Features

- **Follows Claude's state.** The character works at the desk while Claude works and rests when it stops. A bubble shows the current step: "Thinking", "Running tests", "Editing README.md".
- **Approval reminders.** When Claude needs your OK, the bubble says so and the pet reacts. If the game is minimized, the taskbar button flashes.
- **Task list.** When Claude uses Claude Code's task tools, its tasks show up as a todo list in the room and get ticked off as it finishes them. Newer models don't have the tools by default; see [Troubleshooting](INSTALL.md#troubleshooting).
- **Room signals.** Each subagent brings an extra pet, failing tools switch the ambience to rain, and the stopwatch runs while Claude works.
- **Status in Claude Code.** The line under the prompt shows the song playing, the timer and today's working time.
- **Room tools for Claude.** Ask Claude to add memos and todos, run the pomodoro, or change the music or ambience.
- **Leaves no trace.** When the session ends, the room goes back to how it was.

![Demo: Claude Code gets a task, makes a task list, asks for approval, fixes the bug and finishes; the room follows each step](docs/media/demo.gif)

<sub>Demo at 1.5× speed · [full-quality MP4](docs/media/demo.mp4)</sub>

## Install

**Requirements:** Windows 10/11, the Steam version of the game, Claude Code 2.1.299+ and Git. The game mod ships as a prebuilt DLL in [Releases](https://github.com/ZHallen122/mini-cozy-room-claude/releases/latest); the .NET SDK is only needed to build from source.

Paste the prompt from [INSTALL.md](INSTALL.md) into a new Claude Code session. The agent backs up your saves, installs MelonLoader and the mod, and installs the plugin. It asks before every change and leaves your Claude Code settings alone. The same page covers manual install, updating and uninstalling.

## Usage

Once it's installed, there's nothing to do: open the game, start a Claude Code session, and the room follows along.

To have Claude change the room, ask in the conversation:

> "I'm about to study, set the room up so I can focus." · "Play a piano piece." · "Start the pomodoro." · "Add a todo: review chapter 3."

| Tool | What it does |
| --- | --- |
| `room_note` | Adds a memo or todo list, titled with a `[Claude]` prefix |
| `room_clear_claude_notes` | Removes only the memos and lists Claude added |
| `room_timer` | Pomodoro: start, pause, status |
| `room_music` | Lists your songs; plays by name, previous, next, pause, resume |
| `room_ambience` | Lists ambient sounds; sets, adds or stops them |

The mod's texts follow the game's language: Chinese for Simplified or Traditional Chinese, English otherwise.

## How it works

| Part | Folder | Role |
| --- | --- | --- |
| Game mod | [`game-mod/`](game-mod/) | MelonLoader mod `CozyClaude.dll`; drives the room inside the game |
| Claude Code plugin | [`claude-mod/`](claude-mod/) | Plugin `cozy-room`; reports Claude's state and provides the room tools |

The two parts talk only through small files in `%LOCALAPPDATA%\CozyClaude\`. No ports and no network.

## Design principles

- **Claude Code is never affected.** Hooks pass every event on first and swallow their own errors, and room tools time out after 5 seconds.
- **Failures are visible.** Each feature has its own health check. A feature a game update breaks is switched off on its own and flagged on the line under the Claude Code prompt.
- **Saves stay intact.** Everything the mod changes is restored when the session ends or on the next start. Your own memos, todos and stats are never touched.
- **Co-op and paid content are off-limits.** The mod does nothing in co-op rooms and uses only content you own.
- **Experience isn't inflated.** Automatic features never unmute audio, so the game's audible-time experience isn't affected.
- **Local only.** The mod accepts only files signed with this machine's random token.

## Configuration

The game mod reads `%LOCALAPPDATA%\CozyClaude\config.json`, which is created on first start. Restart the game after editing it.

<details>
<summary>All settings</summary>

| Key | Default | Meaning |
| --- | --- | --- |
| `WorkActivities` | `[10003, 10002]` | Activity while Claude works: the first whose furniture is in the room (desk with laptop, desk with drawing tablet) |
| `RestActivities` | `[10021, 10031, 10011, 10024]` | Activity while Claude is idle (bed, sofa, beanbag, reading in bed) |
| `TimerMode` | `"stopwatch"` | Timer that follows Claude: `stopwatch`, `pomodoro` or `off` |
| `RestoreOnSessionEnd` | `true` | Restore the room when the session ends |
| `RainOnError` | `true` | Switch to rain when a tool fails |
| `RainSoundKey` | `20005` | Ambient sound used as rain |
| `RainUnmutes` | `false` | Unmute muted ambience for the rain |
| `PetNudgeWhileWaiting` | `true` | Pet reacts while Claude waits for approval |
| `WaitingText` | `""` | Custom waiting-bubble text; empty uses the default |
| `MaxSubagentPets` | `3` | Maximum extra pets for subagents |
| `ShowActivity` | `true` | Show Claude's current step in the bubble |
| `SyncTaskList` | `true` | Mirror Claude's tasks into a room todo list |
| `AttentionWhenHidden` | `true` | Flash the taskbar and update the tray tooltip when the game is hidden |
| `AttentionSound` | `false` | Also play the game's timer sound |
| `AttentionRestoreWindow` | `false` | Also restore the game window |
| `Language` | `"auto"` | `auto` follows the game; `zh` or `en` forces a language |

</details>

## Known limitations

- Tested with game version 1.07.01 (Unity 6000.3.22f1) and Claude Code 2.1.299. A game update can stop MelonLoader from loading ([MelonLoader#1159](https://github.com/LavaGang/MelonLoader/issues/1159)). The line under the prompt then says "Room offline", and Claude Code keeps working.
- MelonLoader adds about 3 seconds to game startup, and more after a long break.
- Only Chinese and English are available. The room tools reply to Claude in Chinese, and Claude relays the reply in your language.
- The pomodoro state isn't saved by the game, so it resets after a restart.
- The co-op guard has only been tested by simulation.

## Roadmap

- [ ] A room or decoration preset per project
- [ ] Celebration effects for commits, merged PRs and passing tests
- [ ] A sleepy character when Claude's context is nearly full
- [ ] Pomodoro breaks shown in Claude Code
- [ ] Daily summary memo
- [ ] Character emotes on success
- [ ] More languages
- [ ] Verify the co-op guard in a real co-op room

Out of scope: sharing Claude's state with co-op friends, and game rewards for Claude usage.

## Development

```bash
dotnet build game-mod -c Release -p:DeployToGame=true
```

```bash
claude plugin test claude-mod
```

The build references assemblies MelonLoader generates, so install MelonLoader and start the game once first. After changing `claude-mod/`, bump `version` in `claude-mod/.claude-plugin/plugin.json`. Game internals, pitfalls and test notes are in [MODLOG.md](MODLOG.md).

---

<sub>A personal, unofficial mod, not affiliated with the game's developer. Windows and the Steam version only.</sub>
