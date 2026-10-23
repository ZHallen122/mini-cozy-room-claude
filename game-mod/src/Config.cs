using System;
using System.IO;
using System.Text.Json;
using MelonLoader;

namespace CozyClaude
{
    // User-editable settings, %LOCALAPPDATA%\CozyClaude\config.json. Written with defaults on first run.
    internal sealed class Config
    {
        // Activity keys tried in order; the first whose furniture is in the current room wins.
        // 10003 = desk / laptop, 10002 = desk / drawing tablet.
        public int[] WorkActivities { get; set; } = { 10003, 10002 };
        // 10021 = lying in bed, 10031 = reading on the sofa, 10011 = reading on the beanbag.
        public int[] RestActivities { get; set; } = { 10021, 10031, 10011, 10024 };
        // "stopwatch": the stopwatch counts while Claude works (no game stats touched).
        // "pomodoro": the session timer starts/pauses with Claude. "off": leave the timer alone.
        public string TimerMode { get; set; } = "stopwatch";
        public bool RestoreOnSessionEnd { get; set; } = true;
        public bool RainOnError { get; set; } = true;
        public int RainSoundKey { get; set; } = 20005;
        // Muted ambience stays muted by default: audible ambience earns the game's playtime XP, and the mod must not
        // change progression. Set true to unmute for the rain anyway (mutes again afterwards).
        public bool RainUnmutes { get; set; } = false;
        public bool PetNudgeWhileWaiting { get; set; } = true;
        // Empty: the reminder in the game's language. Anything else is shown as written.
        public string WaitingText { get; set; } = "";
        public int MaxSubagentPets { get; set; } = 3;
        // A line above the character on what Claude is doing ("正在跑测试…", "在改 README.md").
        public bool ShowActivity { get; set; } = true;
        // Mirror Claude's own task list into a "[Claude] <project> 任务" todo list, removed when the session ends.
        public bool SyncTaskList { get; set; } = true;
        // While the game is minimized or hidden in the tray and Claude waits for approval: flash the taskbar button
        // and put the reminder in the tray tooltip. Optionally also play the player's timer sound, or bring the
        // window back.
        public bool AttentionWhenHidden { get; set; } = true;
        public bool AttentionSound { get; set; } = false;
        public bool AttentionRestoreWindow { get; set; } = false;
        // A little party over the character when Claude makes a commit, merges a pull request or gets a test run to
        // pass (tests at most every 90 s): confetti, a line in the bubble and the pet's reaction. No sound, nothing saved.
        public bool Celebrate { get; set; } = true;
        public bool CelebrateTests { get; set; } = true;
        // From this full (percent of Claude's context window) the character gets sleepy: "z"s drift up, and while
        // resting the bubble says so. It wakes up when the conversation is compacted. 0 turns it off.
        public int SleepyAtContextPercent { get; set; } = 80;
        // "auto": follow the game's language (Chinese, otherwise English). "zh" or "en" force one.
        public string Language { get; set; } = "auto";

        // The waiting reminder: the player's own text, or the default in the current language. Older versions wrote
        // the Chinese default into config.json, so that one counts as "default" too.
        public string Waiting() =>
            string.IsNullOrEmpty(WaitingText) || WaitingText == "Claude 在等你确认"
                ? Lang.T("Claude 在等你确认", "Claude is waiting for you")
                : WaitingText;

        public static Config Load()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                };
                var old = File.Exists(Paths.Config) ? File.ReadAllText(Paths.Config) : null;
                var cfg = old == null ? new Config() : JsonSerializer.Deserialize<Config>(old) ?? new Config();
                // write it back so settings added by a newer version show up in the file, with their defaults
                var text = JsonSerializer.Serialize(cfg, options);
                if (text != old)
                {
                    Directory.CreateDirectory(Paths.Root);
                    Paths.WriteAtomic(Paths.Config, text);
                }
                return cfg;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("config.json unreadable, using defaults: " + e.Message);
                return new Config();
            }
        }
    }
}
