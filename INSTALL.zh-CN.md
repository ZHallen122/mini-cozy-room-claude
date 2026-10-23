# 安装

[English](INSTALL.md) | **中文**

## 你需要准备

- Windows 10/11，Steam 版 [Mini Cozy Room: Lo-Fi](https://store.steampowered.com/app/3511030/)（至少启动过一次）。
- [Claude Code](https://claude.com/claude-code) 2.1.299 或更新版本。
- [Git](https://git-scm.com/download/win)。
- 可选：[.NET SDK](https://dotnet.microsoft.com/download) 6 或更新版本，只有不用 [Releases](https://github.com/ZHallen122/mini-cozy-room-claude/releases/latest) 里编译好的 DLL、要自己从源码编译时才需要。

## 让 AI 帮你装（推荐）

在 Claude Code 里开一个新会话，把下面整段复制进去发送。Agent 会先检查环境、备份存档，改动游戏目录前会先问你。

````text
请帮我在这台 Windows 电脑上安装「Mini Cozy Room × Claude Code」联动 mod，仓库是 https://github.com/ZHallen122/mini-cozy-room-claude 。按下面的步骤做，每一步做完都要确认结果，不要跳步。

规则：
- 改动游戏目录、关闭正在运行的游戏之前，先告诉我要做什么，等我同意。
- 不要修改、删除或覆盖存档，只做备份。
- 关游戏只用关闭主窗口的方式（PowerShell 的 CloseMainWindow），不要按进程名强杀。
- 同一个问题失败 3 次就停下来，告诉我卡在哪、试过什么。
- 不要 git commit 或 push 任何东西。

1. 检查环境：Windows、git、claude（`claude --version`，需要 2.1.299 或更新）。缺什么就告诉我去装，不要替我装系统软件。.NET SDK 只有第 7 步需要从源码编译时才用到。

2. 找到游戏目录：默认是 `C:\Program Files (x86)\Steam\steamapps\common\Mini Cozy Room`；不在那里就读 Steam 的 `steamapps\libraryfolders.vdf` 找到 app 3511030 所在的库。确认目录里有 `MiniCozyRoom.exe` 和 `GameAssembly.dll`，并读出 `Version.txt`。

3. 备份存档：把 `%USERPROFILE%\AppData\LocalLow\TesseractStudio\MiniCozyRoom\` 整个复制到 `%USERPROFILE%\MiniCozyRoom-save-backup\<今天日期>\`，复制完核对文件数一致，把备份路径告诉我。

4. 安装 MelonLoader v0.7.3：
   - 如果游戏目录里已经有 `MelonLoader\` 文件夹：看 `MelonLoader\Latest.log` 开头几行的版本号，是 v0.7.3 并且游戏目录里有 `winmm.dll` 就跳过这一步；是别的版本或加载文件不是 winmm.dll，先问我。
   - 从官方地址下载 https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.x64.zip 到一个临时文件夹，核对 SHA256 必须等于 5B2B2F3D1CD42B59EC886C5BDC2663EDAE87A0097A4F4A8F58C0965A99DDA416 ，不一致就停下。
   - 解压到游戏目录，然后把游戏目录里的 `version.dll` 改名为 `winmm.dll`（这一步必须做：Windows 的兼容性设置会让游戏跳过 version.dll，用 winmm.dll 才能稳定加载）。如果游戏目录里原本就有 winmm.dll，先停下问我。

5. 生成 MelonLoader 的程序集：通过 Steam 启动游戏，例如 PowerShell 里 `Start-Process "steam://rungameid/3511030"`（不要直接运行 exe），等到 `<游戏目录>\MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll` 出现，而且 `<游戏目录>\MelonLoader\Latest.log` 里出现 `Support Module Loaded`（第一次要 1～2 分钟）。然后关闭游戏（征得我同意后用 CloseMainWindow），等进程退出。如果游戏闪退或黑屏退出，把 Latest.log 最后 30 行给我看，并删除 `winmm.dll` 恢复原状。
   然后编辑这次生成的 `<游戏目录>\UserData\Loader.cfg`，只改这三项：`hide_console = true`（不弹 MelonLoader 控制台窗口）、`disable_start_screen = true`（不显示 MelonLoader 启动画面）、`force_offline_generation = true`（启动时不联网查询 MelonLoader 服务器，这个游戏查了也没有数据）。

6. 获取源码：把仓库克隆到用户目录下的 `mini-cozy-room-claude` 文件夹（下面叫「克隆目录」；已经存在就在里面 `git pull`）。克隆失败就把错误告诉我。

7. 安装游戏 mod（游戏必须是关闭的）：
   - 如果第 2 步读到的 `Version.txt` 是 `1.07.01`：下载 https://github.com/ZHallen122/mini-cozy-room-claude/releases/download/v0.5.1/CozyClaude.dll 到一个临时文件夹，核对 SHA256 必须等于 ab57772974b76c8fc106c679c033d7bad7dccce7dd9d98f02ed13a002345a550 ，不一致就停下。然后复制到 `<游戏目录>\Mods\CozyClaude.dll`。
   - 版本不是 1.07.01，或者第 9 步发现 mod 加载失败：改为从源码编译。这需要 .NET SDK 6 或更新（`dotnet --list-sdks`；没装就告诉我去装）。在克隆目录里运行 `dotnet build game-mod -c Release -p:DeployToGame=true -p:GameDir="<游戏目录>"`，要求 0 个错误。
   - 不管哪种方式，最后都要确认 `<游戏目录>\Mods\CozyClaude.dll` 存在。

8. 安装 Claude Code 插件（路径用克隆目录的绝对路径）：
   `claude plugin marketplace add "<克隆目录>\claude-mod"`
   `claude plugin install cozy-room@cozy-room-local --scope user`
   然后用 `claude plugin list` 确认 cozy-room 是 enabled。

9. 验证：
   - 再次通过 Steam 启动游戏，等 20 秒，确认 `MelonLoader\Latest.log` 里有 `CozyClaude loaded` 和 `room ready, linking with Claude Code`，并且没有 `[ERROR]`。
   - 确认 `%LOCALAPPDATA%\CozyClaude\room.json` 存在，且里面的 `updatedAt`（毫秒时间戳）是最近几秒。
   - 运行 `claude -p "Reply with the single word ok."`，然后确认 `%LOCALAPPDATA%\CozyClaude\sessions\` 里多了一个 .json 文件，并且 Latest.log 里出现了 `phase ... -> Working` 和 `-> Off` 这样的行。
   - 游戏保持开着，不用关。

10. 最后用几句话告诉我：存档备份在哪、装了什么、改了哪些文件夹、每项验证的结果，以及「请重新开一个 Claude Code 会话，输入框下面的房间信息和房间工具才会出现」。
````

## 手动安装

1. **备份存档**：复制 `%USERPROFILE%\AppData\LocalLow\TesseractStudio\MiniCozyRoom\` 到别处。
2. **装 MelonLoader v0.7.3**：下载 [MelonLoader.x64.zip](https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.x64.zip)，解压到游戏目录，**把 `version.dll` 改名为 `winmm.dll`**。
3. **启动一次游戏**：从 Steam 启动，等 1～2 分钟，直到 `MelonLoader\Il2CppAssemblies\` 生成，然后关掉游戏。
   再打开游戏目录里的 `UserData\Loader.cfg`，把 `hide_console`、`disable_start_screen`、`force_offline_generation` 三项改成 `true`：不弹控制台窗口、不显示启动画面、启动时不联网查询。
4. **安装游戏 mod**：从[最新 Release](https://github.com/ZHallen122/mini-cozy-room-claude/releases/latest)下载 `CozyClaude.dll`，放进游戏目录的 `Mods\` 文件夹（游戏要先关掉）。Release 说明里写了 SHA256 和测试过的游戏版本。
   想自己从源码编译（需要 .NET SDK 6+）：克隆本仓库，在仓库目录里运行（游戏目录不是默认位置时加 `-p:GameDir="..."`）：

```bash
dotnet build game-mod -c Release -p:DeployToGame=true
```

5. **装 Claude Code 插件**：克隆本仓库，然后运行（把路径换成你克隆的位置）：

```bash
claude plugin marketplace add "C:/path/to/mini-cozy-room-claude/claude-mod"
```

```bash
claude plugin install cozy-room@cozy-room-local --scope user
```

6. 启动游戏，新开一个 Claude Code 会话，输入框下面那行末尾出现 `🏠 …` 就装好了（桌面版里显示为一条置顶提示）。

## 更新

关掉游戏，用[最新 Release](https://github.com/ZHallen122/mini-cozy-room-claude/releases/latest)里的 `CozyClaude.dll` 替换 `Mods\CozyClaude.dll`（或者从源码重新编译）。然后在克隆目录里拉最新代码：

```bash
git pull
```

再更新插件，然后重开 Claude Code 会话：

```bash
claude plugin marketplace update cozy-room-local
```

```bash
claude plugin update cozy-room@cozy-room-local
```

（维护者改了 `claude-mod/` 时要把 `claude-mod/.claude-plugin/plugin.json` 的 `version` 加一，否则 `plugin update` 不会拿到新代码。发布用的 DLL 要加 `-p:DebugType=none` 编译，避免里面带上本机路径。）

## 卸载

1. 关掉所有 Claude Code 会话，开一次游戏让房间复原（Claude 的任务清单也会删掉），再关掉游戏。想清掉 Claude 加的 memo 和待办，先让 Claude 用 `room_clear_claude_notes`。
2. 卸载 Claude Code 插件：

```bash
claude plugin uninstall cozy-room@cozy-room-local
```

```bash
claude plugin marketplace remove cozy-room-local
```

3. 删掉游戏目录里的 `Mods\CozyClaude.dll`。要连 MelonLoader 一起去掉，再删 `winmm.dll`、`MelonLoader\`、`Mods\`、`Plugins\`、`UserData\`、`UserLibs\`。
4. 删掉 `%LOCALAPPDATA%\CozyClaude\`。

## 排错

| 现象 | 原因和办法 |
| --- | --- |
| 游戏目录里没有生成 `MelonLoader\Latest.log` | MelonLoader 没被加载。检查是不是还叫 `version.dll`，改名为 `winmm.dll` |
| 装了 MelonLoader 后游戏黑屏退出 | 游戏更新到了 MelonLoader 还不支持的 Unity 版本。删掉 `winmm.dll` 即可恢复原样，等 MelonLoader 更新 |
| 编译报找不到 `Il2CppAssemblies` 里的 dll | 装完 MelonLoader 后还没启动过游戏，或 `GameDir` 路径不对 |
| 输入框下面那行显示 `🏠 房间离线`（英文是 `Room offline`） | 游戏没开，或 mod 没加载。看 `MelonLoader\Latest.log` 有没有 `CozyClaude loaded` |
| 输入框下面那行显示 `注意：xx不兼容`（英文是 `Note: … incompatible`） | 游戏更新后这个功能对不上了，mod 已自动停用它，其他功能照常。日志里有原因 |
| 输入框下面没有 `🏠`、Claude 没有房间工具 | 插件装完后要新开 Claude Code 会话；用 `claude plugin list` 确认 cozy-room 是 enabled |
| Claude 说「你在联机房间里」 | 你在联机房间里或有朋友来访，mod 按设计不动房间 |
| 报错时没有雨声 | 环境音静音时雨声也不出声，这样游戏按「有声音的时长」给的经验不受影响。想听雨声就在 `config.json` 里把 `RainUnmutes` 设成 `true` |
| 没看到 Claude 的任务清单 | 这个清单同步的是 Claude Code 自己的任务工具，只有 Claude 有这些工具、并且把工作拆成任务时才会出现。较新的模型上 Claude Code 默认不提供任务工具；想要的话可以自己设置环境变量 `CLAUDE_CODE_ENABLE_TODO_TOOLS=1`（[官方说明](https://code.claude.com/docs/en/tools-reference#task-tool-availability)）。安装流程不替你改这项，因为它会影响所有 Claude Code 会话。也检查 `config.json` 里 `SyncTaskList` 是不是 `true` |
| 每次启动弹出一个黑色的 MelonLoader 窗口 | `UserData\Loader.cfg` 里设 `hide_console = true`。排错时想看实时日志可以改回 `false`，日志也一直写在 `MelonLoader\Latest.log` |
| 启动比原来慢 | MelonLoader 每次启动要先起 .NET 运行时、加载程序集。实测连续启动时多约 3 秒（不装 3.1～3.4 秒，装了 6.1～6.8 秒，到游戏初始化完成）；隔很久第一次开（文件不在系统缓存里）会慢到 20 秒左右，再开就快了 |
