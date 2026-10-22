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
        public string WaitingText { get; set; } = "Claude 在等你确认";
        public int MaxSubagentPets { get; set; } = 3;

        public static Config Load()
        {
            try
            {
                if (File.Exists(Paths.Config))
                    return JsonSerializer.Deserialize<Config>(File.ReadAllText(Paths.Config)) ?? new Config();
                var fresh = new Config();
                Directory.CreateDirectory(Paths.Root);
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                };
                Paths.WriteAtomic(Paths.Config, JsonSerializer.Serialize(fresh, options));
                return fresh;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("config.json unreadable, using defaults: " + e.Message);
                return new Config();
            }
        }
    }
}
