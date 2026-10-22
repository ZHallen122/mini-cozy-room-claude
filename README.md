# Mini Cozy Room × Claude Code 联动 Mod

自用 mod：Claude Code 干活时，[Mini Cozy Room: Lo-Fi](https://store.steampowered.com/app/3511030/) 里的小人坐到桌前用笔记本；
Claude 等你批准操作时，小人头上冒出「Claude 在等你确认」的气泡、宠物做反应动作；Claude 停下就去床上躺着；
会话结束后房间回到原样。你在对话里让 Claude 做的时候，它还能在房间里加 memo/todo、开番茄钟、换歌、换环境音。

两部分：

| 部分 | 目录 | 是什么 |
| --- | --- | --- |
| 游戏侧 | `game-mod/` | MelonLoader mod（C#，`CozyClaude.dll`） |
| Claude Code 侧 | `claude-mod/` | Claude Code 插件 `cozy-room`（函数钩子模块，TypeScript） |

两边只通过 `%LOCALAPPDATA%\CozyClaude\` 里的小文件说话，没有端口、没有网络。

## 功能和现状

| 功能 | 状态 | 怎么做的 |
| --- | --- | --- |
| P0 工作状态同步 | ✅ 实机验证 | 开工 → 书桌/笔记本（10003，备选画板 10002）；停下 → 床（10021，备选沙发、懒人沙发）。只在状态切换时改，中途你手动换动作不会被抢回来 |
| P0 等待确认提醒 | ✅ 实机验证 | 小人头顶气泡（mod 自己的覆盖层，不进存档），宠物每 6 秒做一次反应动作；多个会话同时等待显示 `×2`；你批准/拒绝后的下一个事件就消失 |
| P1 番茄钟联动 | ✅ 实机验证 | 默认用秒表：Claude 干活时走，空闲时暂停（不碰游戏统计）。`TimerMode: "pomodoro"` 可改成番茄钟。今天 Claude 干了多久另外记在 `stats.json`，显示在状态栏 |
| P1 会话结束复原 | ✅ 实机验证 | 所有会话结束 → 小人回到会话前的动作、环境音复原、气泡和额外宠物清掉。崩溃/重启也能复原（见下面「存档安全」） |
| P2 报错氛围 | ✅ 实机验证 | 工具失败 → 环境音切成雨声（20005，非 DLC），下一个成功的工具或回合结束后切回原来的。**你的环境音静音时默认不响**（见下面「为什么」） |
| P2 房间状态反显 | ✅ 单元测试；状态栏需在交互会话里看 | Claude Code 状态栏：`🏠 · ♪ 歌名 · ⏱ 秒表 · 今日 Claude 1h05m`，有功能不兼容时追加 `⚠ xx不兼容`；游戏没开显示「房间离线」 |
| P2 多会话 / subagent | ✅ 实机验证 | 任一会话在干活小人就保持工作；每个运行中的 subagent 在宠物右边多出一只你已解锁的宠物（最多 3 只，可配） |
| v2 主动操作 | ✅ 实机验证 | 插件给 Claude 5 个工具（见下面「让 Claude 动手」），只在你在对话里提出时用；游戏没开、联机中、超时都会立刻返回明确的失败原因 |

## 让 Claude 动手（v2）

在任何 Claude Code 对话里直接说，例如「在房间里加个 todo：买牛奶、跑步」「开番茄钟」「放 Lo-Fi Sound Track - 5」「环境音换成雨声」「把你加的 memo 都清掉」。Claude 用的工具：

| 工具 | 做什么 |
| --- | --- |
| `room_note` | 加一条 memo（标题 + 内容）或一个 todo 清单（标题 + 条目）。标题自动加 `[Claude] ` 前缀 |
| `room_clear_claude_notes` | 清掉 Claude 加的 memo/todo，你的一概不碰 |
| `room_timer` | 番茄钟 `start` / `pause` / `status`（用你自己的专注/休息时长设置） |
| `room_music` | `list`（只列你拥有的歌）/ `play`（按歌名）/ `next` / `previous` / `pause` / `resume` |
| `room_ambience` | `list` / `set`（只放这些）/ `add` / `stop`（不给名字就全停）。名字是游戏里的中文名，如「雨声」「壁炉」 |

工具挂在 ToolSearch 后面（和 MCP 工具一样，不占每次对话的提示词）；Claude 需要时自己搜出来。第一次调用会像别的工具一样问你要不要允许。
每个操作成功时游戏里弹一条小提示（「Claude 换歌：…」），状态栏的计时器后面会多出 `🍅 专注 19:23`。

**注意经验值**：歌和**有声**的环境音会让游戏按时长给经验、计入听歌统计（见「为什么静音时不放雨声」）。你让 Claude 放歌、或者在环境音静音时让它换环境音（换环境音会取消静音，回复里会写明），等于你自己按了播放，这部分经验照常算——这是你主动要求的操作，mod 不去改回。mod 自己的自动联动（P0–P2）仍然从不取消静音。

## 约束是怎么满足的

- **Claude Code 永远不受影响**：Claude 的工具最多等游戏 5 秒（钩子的时间预算是 10 秒），游戏没开/联机中直接返回原因不等；超时或你中途打断都会把命令作废；工具钩子出错时只返回一条失败信息。插件的每个钩子先把事件原样交还（`next(e)`），文件读写在之后异步进行、全部吞掉错误；每个钩子还挂了 `.catch` 失败时直接放行。游戏没开时插件照样只写一个小 JSON（已实测：游戏关闭时会话正常结束、无报错）。
- **坏了要看得出来**：游戏侧每个功能单独包在健康检查里。房间加载时先对每个功能做一次只读自检；之后任何功能抛异常（游戏更新后最常见的是 `MissingMethodException`）就只停用这一个功能，并且：游戏里弹一次提示「CozyClaude：xx 与当前游戏版本不兼容，已停用」、MelonLoader 日志写明原因、Claude Code 状态栏显示 `⚠ xx不兼容`。mod 整个没加载时状态栏显示「房间离线」。
- **不碰联机**：每次动手前查 `MultiplayManager`（联机模式 / 在大厅里 / 正在切换），任一为真就什么都不改，气泡和额外宠物也收起；联机管理器本身读不到时同样按「联机中」处理（宁可不动）。回到自己房间后按当前状态重新同步。Claude 的主动操作在执行那一刻再查一次，联机中直接拒绝并告诉 Claude「你在联机房间里」。
- **不碰付费内容**：只用 `ActivitySystem` 里本来就有、且当前房间有对应家具的动作；雨声若是 DLC 且你没买就不放；额外宠物只从你已解锁的宠物里挑（排除每类的「无宠物」占位）。Claude 换歌、换环境音只在你拥有的内容里挑（`DLCManager.IsDLCSoundAccessible`），`list` 也只列这些；点名要没买的 DLC 会被拒绝。「下一首/上一首」走游戏自己的播放顺序，和你按播放器按钮一样。
- **不弄坏存档**：气泡和额外宠物是纯场景对象，不进存档。改动作时把游戏的「换动作次数」统计改回原值。会改到存档里的只有「当前动作」和环境音开关，原值都记在 `journal.json`，复原后要等游戏真的把复原结果存盘才删日志；中途崩溃、重启都会在下次启动时复原。你自己的 memo、todo、装修一概不碰：Claude 只新建 memo/todo，标题带 `[Claude] ` 前缀，并记进 `claude-notes.json`；清除时只删「在记录里 **且** 标题仍是 Claude 起的那个」的笔记，你改过标题的就算你的。Claude 新建 memo 时把游戏的「创建 memo 次数」统计改回原值。
- **只有我能控制**：没有监听端口，网页够不着；会话文件和命令文件都在你的用户目录里，每个文件都要带 `secret.key` 里的随机令牌，不带的一律忽略（日志里记一笔）。命令超过 8 秒没被执行就作废，所以游戏没开时写下的命令不会在之后突然执行。（同一 Windows 用户下的其他程序理论上能读到令牌——本地文件方案的固有边界。）

### 为什么静音时不放雨声

实测发现：环境音**有声**时游戏会按时长给经验（我测试时开声几分钟，经验从 315 涨到 575，已改回）。你平时环境音是静音的，如果 mod 为了雨声替你取消静音，就等于替你刷经验，违反「不改数值」。所以默认 `RainUnmutes: false`：静音时只在日志/健康状态里注明「环境音已静音，雨声不会响」。想要雨声就把它改成 `true`（雨停后会重新静音）。

## 安装

> 下面这些在这台机器上**已经装好了**，这里记的是重装/在别处装的步骤。

### 游戏侧

1. 下载 [MelonLoader v0.7.3](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3) 的 `MelonLoader.x64.zip`，解压到游戏目录。
2. **把 `version.dll` 改名为 `winmm.dll`**（见下面「坑」第 1 条，不改名 MelonLoader 不会加载）。
3. 启动一次游戏，等 MelonLoader 生成 `MelonLoader\Il2CppAssemblies`（约 1 分钟），关掉。
4. 编译并部署：

```bash
dotnet build game-mod -c Release -p:DeployToGame=true
```

（需要 .NET SDK 6+；引用的是游戏目录里 MelonLoader 生成的程序集，路径不同就加 `-p:GameDir="..."`。）

### Claude Code 侧

```bash
claude plugin marketplace add "C:/Code_Project/Open_source/mini-cozy-room-mod/claude-mod"
```

```bash
claude plugin install cozy-room@cozy-room-local --scope user
```

安装时 Claude Code 把 `claude-mod/` 复制一份到插件缓存（按 `plugin.json` 的版本号分目录），之后不会自动跟着源码变。改了代码要先把 `plugin.json` 的 `version` 加一，再：

```bash
claude plugin marketplace update cozy-room-local
```

```bash
claude plugin update cozy-room@cozy-room-local
```

已经开着的 Claude Code 会话要重开才会加载新版本（v2 的工具在会话开始时注册）。

## 配置

`%LOCALAPPDATA%\CozyClaude\config.json`（首次运行自动生成，改完重启游戏生效）：

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `WorkActivities` | `[10003, 10002]` | 工作动作，按顺序取当前房间可用的第一个 |
| `RestActivities` | `[10021, 10031, 10011, 10024]` | 休息动作 |
| `TimerMode` | `"stopwatch"` | `stopwatch` / `pomodoro` / `off` |
| `RestoreOnSessionEnd` | `true` | 会话结束是否复原（开放问题 4） |
| `RainOnError` / `RainSoundKey` / `RainUnmutes` | `true` / `20005` / `false` | 报错雨声 |
| `PetNudgeWhileWaiting` | `true` | 等待时宠物做反应动作 |
| `WaitingText` | `"Claude 在等你确认"` | 气泡文字 |
| `MaxSubagentPets` | `3` | 额外宠物上限 |

## 开放问题：这版的默认答案

1. **番茄钟是否一直跟着 Claude？** 默认用秒表跟着 Claude 走（不影响番茄钟完成次数等统计）；mod 只暂停它自己启动的计时，你自己开着的计时不会被暂停。
2. **多会话每个会话一只宠物？** 只看「有没有会话在干活」；额外宠物按 subagent 数量出场。
3. **等待提醒要不要声音？** 这版没有声音，只有气泡 + 宠物动作。
4. **会话结束复原还是停在最后状态？** 默认复原，`RestoreOnSessionEnd: false` 可以停在最后状态。

## 文件

`%LOCALAPPDATA%\CozyClaude\`：

- `sessions\<会话id>.json` — 插件写：每个会话的状态，每个事件和每 20 秒心跳重写一次。90 秒没心跳算会话已死（Claude Code 被强杀也能识别）。
- `secret.key` — 插件首次运行生成的随机令牌。
- `room.json` — 游戏 mod 每 2 秒写：歌、计时、各功能健康状态，给状态栏用。
- `commands\<id>.json` / `results\<id>.json` — v2：Claude 的一次操作和游戏的回复。命令读到就删；回复 2 分钟后删。
- `claude-notes.json` — v2：Claude 加过、还没清掉的 memo/todo（类型、编号、标题）。
- `config.json`、`journal.json`（复原日志）、`stats.json`（每天 Claude 工作秒数）。

## 卸载

- Claude 侧：`claude plugin uninstall cozy-room@cozy-room-local`，再 `claude plugin marketplace remove cozy-room-local`。
- 游戏侧：先在没有 Claude 会话时开一次游戏让房间复原，再删游戏目录里的 `Mods\CozyClaude.dll`；要去掉 MelonLoader 就删 `winmm.dll`、`MelonLoader\`、`Mods\`、`Plugins\`、`UserData\`、`UserLibs\`。
- 数据：删 `%LOCALAPPDATA%\CozyClaude\`。卸载前想清掉 Claude 加的 memo/todo，先让 Claude 用 `room_clear_claude_notes`（或在游戏里手动删）。

## 坑（详见 MODLOG.md）

1. Windows 给 `MiniCozyRoom.exe` 挂了「禁用全屏优化」兼容层，兼容层会先从 System32 加载 `VERSION.dll`，MelonLoader 默认的 `version.dll` 代理因此被跳过。改名 `winmm.dll` 解决。
2. `Player.ChangeActivityKey` 只改数值，要接着调 `Character.UpdateActivity()`、`Pet.UpdateActivity()`、`World.UpdateActivityEffect()` 小人才会动；它还会给「换动作次数」统计 +1。
3. 有声环境音会按时长给经验；`PlayAmbientSound` 会顺带取消静音；取消静音会恢复「静音那一刻在放的声音」。
4. 宠物「预览」接口 `ChangePetVisualOnlyAsync` 的精灵会被 `PetSpriteLoader` 在宽限期后释放；每类宠物的 `x0000` 是空白的「无宠物」占位。

## 已知限制

- v2 的工具在交互式会话里第一次用时会弹权限确认；无头 `claude -p` 需要 `--allowedTools "mcp__cozy-room__room_note,…"`。
- 番茄钟状态不进存档（游戏本身如此），重启游戏后回到「待开始」。

- 状态栏是插件用 `$.ui.status` 显示的一行，只在交互式会话里看得到；这次的端到端测试用的是无头 `claude -p`，状态栏本身没有在交互界面里亲眼确认。
- 联机保护用开发开关模拟了「在大厅里」验证，没有真的开联机房间测试（不想动你的 Steam 好友/大厅）。
- 游戏自己约 30 秒才存一次盘，所以会话中途存档里可能是 Claude 设的动作；`journal.json` 保证之后复原。
- MelonLoader 在部分 Unity 6000.3 游戏上不稳定（[MelonLoader#1159](https://github.com/LavaGang/MelonLoader/issues/1159)），本游戏 1.07.01 实测正常。游戏更新后若 MelonLoader 起不来，状态栏会显示「房间离线」。
