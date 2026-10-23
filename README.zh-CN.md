# Mini Cozy Room × Claude Code

[English](README.md) | **中文**

让 [Mini Cozy Room: Lo-Fi](https://store.steampowered.com/app/3511030/) 里的房间跟着 Claude Code 一起动：

- Claude 开始干活，小人坐到桌前用笔记本；Claude 停下，小人去床上（或沙发）躺着。
- 小人头上的气泡实时显示 Claude 在干嘛：「思考中…」「正在跑测试…」「在改 README.md」。
- Claude 等你批准操作时，气泡换成醒目的「Claude 在等你确认」，宠物做反应动作；你一确认气泡就消失。
- 游戏最小化或缩到托盘时，Claude 等你确认会让任务栏按钮闪烁、托盘提示变成「Claude 在等你确认」。
- Claude 把工作拆成任务时，房间里出现一个 `[Claude] <项目名> 任务` 待办清单，做完一项勾一项，会话结束自动删掉。
- Claude 派出 subagent 时，每个 subagent 多出一只宠物陪着。
- Claude 的工具报错时，环境音切成雨声，恢复后切回。
- 秒表跟着 Claude 走，Claude Code 输入框下面那行提示的末尾显示房间里正在放的歌、计时和「今日 Claude 干了多久」。
- 会话结束后，房间回到会话开始前的样子。
- 你在对话里开口，Claude 还能在房间里加 memo/todo、开番茄钟、换歌、换环境音。

**安装：把 [INSTALL.zh-CN.md](INSTALL.zh-CN.md) 里的那段 prompt 复制给 Claude Code，它会自己装好。**

> 这是个人自用 mod，非官方，与游戏开发商无关。只支持 Windows + Steam 版游戏。

## 由两部分组成

| 部分 | 目录 | 是什么 |
| --- | --- | --- |
| 游戏 mod | [`game-mod/`](game-mod/) | MelonLoader mod `CozyClaude.dll`，在游戏里改房间 |
| Claude Code mod | [`claude-mod/`](claude-mod/) | Claude Code 插件 `cozy-room`，把 Claude 的状态告诉游戏，并给 Claude 操作房间的工具 |

两边只通过 `%LOCALAPPDATA%\CozyClaude\` 里的几个小文件传话，不开端口、不联网。

## 使用

装好以后什么都不用做：开一个 Claude Code 会话，开着游戏，房间就会跟着动。

想让 Claude 动手，直接在对话里说，例如：

- 「我要学习了，帮我调成适合专心的状态」
- 「环境音换成雨声加壁炉」
- 「放一首钢琴曲」「下一首」「暂停音乐」
- 「开番茄钟」
- 「在房间里加个 todo：复习第三章、做习题」
- 「把你加的 memo 都清掉」

Claude 用的工具：

| 工具 | 做什么 |
| --- | --- |
| `room_note` | 加一条 memo 或一个 todo 清单，标题自动带 `[Claude] ` 前缀 |
| `room_clear_claude_notes` | 只清掉 Claude 加的 memo/todo，你自己的不碰 |
| `room_timer` | 番茄钟：开始 / 暂停 / 查看 |
| `room_music` | 列出你拥有的歌 / 按名字播放 / 上一首 / 下一首 / 暂停 / 继续 |
| `room_ambience` | 列出环境音 / 只放这些 / 加上 / 停掉 |

第一次用某个工具时，Claude Code 会像对其他工具一样问你是否允许。

## 设计上的保证

- **不影响 Claude Code**：游戏没开、mod 出错、游戏更新后不兼容，Claude Code 都照常工作。插件的钩子先放行事件再做事，出错一律吞掉；Claude 的房间工具最多等 5 秒，游戏没开会立刻说明原因。
- **坏了看得出来**：每个功能单独做健康检查。某个功能和新版游戏不兼容时，只停用这一个，游戏里弹一次提示，Claude Code 状态栏显示 `⚠ xx不兼容`；mod 整个没加载时，状态栏显示「房间离线」。
- **不碰联机**：在联机房间里或有朋友来访时，mod 不对房间做任何改动，Claude 的操作也会被拒绝。
- **不碰付费内容**：只用你已经拥有的动作、宠物、歌和环境音，没买的 DLC 内容不会出现。
- **不弄坏存档**：气泡和额外宠物不进存档；改过的动作和环境音会在会话结束后复原，中途崩溃或重启也会在下次启动时复原；Claude 加的 memo/todo 和任务清单都能清掉，你自己的 memo、todo 和装修不受影响。任务清单打勾不计入你的「已完成 todo」统计。你改过标题的 Claude 清单算作你的，mod 不再同步也不会删掉它。
- **只有你的电脑能控制**：只认 `%LOCALAPPDATA%\CozyClaude\` 里带本机随机令牌的文件，网页和别的电脑碰不到。

### 关于经验值

游戏会在**有声**的音乐和环境音播放时按时长给经验。所以 mod 的自动联动从不替你取消静音：环境音静音时，报错雨声不会响（`RainUnmutes` 设成 `true` 才会临时取消静音）。你主动让 Claude 放歌或换环境音，就和你自己按播放一样，经验照常算。

## 配置

`%LOCALAPPDATA%\CozyClaude\config.json`，第一次运行游戏时自动生成（新版本加的设置也会自动补进去），改完重启游戏生效。

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `WorkActivities` | `[10003, 10002]` | Claude 干活时的动作，按顺序取当前房间有家具的第一个（桌前笔记本、桌前画板） |
| `RestActivities` | `[10021, 10031, 10011, 10024]` | Claude 停下时的动作（床上躺、沙发看书、懒人沙发看书、床上看书） |
| `TimerMode` | `"stopwatch"` | 自动联动用哪个计时器：`stopwatch` 秒表 / `pomodoro` 番茄钟 / `off` 不联动 |
| `RestoreOnSessionEnd` | `true` | 会话结束后是否把房间复原 |
| `RainOnError` | `true` | 报错时切雨声 |
| `RainSoundKey` | `20005` | 用哪个环境音当雨声 |
| `RainUnmutes` | `false` | 环境音静音时，是否为雨声临时取消静音 |
| `PetNudgeWhileWaiting` | `true` | 等待确认时宠物做反应动作 |
| `WaitingText` | `"Claude 在等你确认"` | 等待确认时的气泡文字 |
| `MaxSubagentPets` | `3` | subagent 宠物最多几只 |
| `ShowActivity` | `true` | 气泡显示 Claude 正在做什么 |
| `SyncTaskList` | `true` | 把 Claude 的任务同步成房间里的待办清单 |
| `AttentionWhenHidden` | `true` | 游戏最小化或在托盘时，等确认会闪任务栏、改托盘提示 |
| `AttentionSound` | `false` | 同时用你在游戏里选的计时器提示音响一声 |
| `AttentionRestoreWindow` | `false` | 同时把游戏窗口弹出来 |

## 卸载

1. 关掉所有 Claude Code 会话，开一次游戏让房间复原（任务清单也会删掉），再关掉游戏。想清掉 Claude 加的 memo/todo，先让 Claude 用 `room_clear_claude_notes`。
2. Claude Code 侧：

```bash
claude plugin uninstall cozy-room@cozy-room-local
```

```bash
claude plugin marketplace remove cozy-room-local
```

3. 游戏侧：删掉游戏目录里的 `Mods\CozyClaude.dll`。要连 MelonLoader 一起去掉，再删 `winmm.dll`、`MelonLoader\`、`Mods\`、`Plugins\`、`UserData\`、`UserLibs\`。
4. 数据：删掉 `%LOCALAPPDATA%\CozyClaude\`。

## 已知限制

- 只在游戏 1.07.01（Unity 6000.3.22f1）和 Claude Code 2.1.299 上测过。游戏更新后 MelonLoader 可能起不来（[MelonLoader#1159](https://github.com/LavaGang/MelonLoader/issues/1159)），这时状态栏显示「房间离线」，Claude Code 不受影响。
- 游戏里的文字（气泡、提示、任务清单标题）和头顶动态目前只有中文。
- 装了 MelonLoader 后游戏启动会慢几秒，隔很久第一次打开会更慢，见 [INSTALL.zh-CN.md](INSTALL.zh-CN.md) 的排错表。
- 游戏大约 30 秒才存一次盘，会话进行中存档里可能是 Claude 设的动作；mod 会在会话结束或下次启动时复原。
- 番茄钟状态不进存档（游戏本身如此），重启游戏后回到「待开始」。
- 联机保护是在单人环境下模拟测试的，没有在真实联机房间里测过。
- 同一个 Windows 用户下运行的其他程序理论上能读到本机令牌，这是本地文件通信的边界。

## TODO

想做、还没做的点子（欢迎挑一个来做）：

- [ ] **按项目换房间**：在不同的 repo 里干活时自动切到不同的房间或装修预设，会话结束切回来（会改存档，要记录原状并复原）。
- [ ] **重要时刻放特效**：git commit 成功、PR 合并、测试全过时，小人头上出庆祝特效，游戏里弹一条提示。
- [ ] **上下文快满时小人犯困**：Claude 的上下文快用完、要压缩对话时，小人打哈欠、冒睡觉特效。
- [ ] **天色跟着时间走**：深夜还在写代码时，房间切成夜晚的样子（游戏的昼夜系统本来跟着真实时间，要先确认该不该插手）。
- [ ] **番茄钟反向联动**：番茄钟到了休息时间，Claude Code 弹一条「该休息了」，状态栏显示休息倒计时。
- [ ] **每日小结 memo**：每天结束时留一条 `[Claude]` memo：今天和 Claude 干了几个小时、几个会话、主要改了哪些项目。
- [ ] **小人表情**：任务成功时冒个开心的表情（游戏的表情功能是给联机用的，单人能不能用还没验证）。
- [ ] **多语言**：游戏里的文字和头顶动态跟随游戏语言，现在只有中文。
- [ ] **在真实联机房间里验证联机保护**。

不打算做的：把 Claude 的状态广播给联机好友（违反「不碰联机」），按 Claude 的用量给游戏奖励（违反「不改数值」）。

## 开发

- 编译游戏 mod：`dotnet build game-mod -c Release -p:DeployToGame=true`（需要先装好 MelonLoader 并启动过一次游戏，引用的是它生成的程序集）。
- 测试 Claude Code mod：`claude plugin test claude-mod`。
- 改了 `claude-mod/` 后，把 `claude-mod/.claude-plugin/plugin.json` 的 `version` 加一，再按 [INSTALL.zh-CN.md](INSTALL.zh-CN.md) 的「更新」步骤更新插件。
- 游戏内部 API、踩过的坑和验证记录在 [MODLOG.md](MODLOG.md)（英文）。
