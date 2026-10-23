using System;
using Il2Cppcozyhouse;
using Il2CppTS;
using MelonLoader;
using UnityEngine;

namespace CozyClaude
{
    // The language of everything the mod shows: the game's own language (Chinese stays Chinese, every other language
    // gets English), unless config.json's Language forces "zh" or "en". The Claude Code plugin follows the same choice
    // through room.json, so the room and the terminal always agree.
    internal static class Lang
    {
        private static Config _cfg;
        private static string _current = "zh";
        private static float _nextCheck;
        private static bool _warned;
        private static bool _logged;

        // dev console: what the game reports
        internal static string Describe() => $"mod={_current} config={_cfg?.Language} game={GameLanguageCode()}";

        public static string Current => _current;
        public static bool Zh => _current == "zh";

        public static void Init(Config cfg)
        {
            _cfg = cfg;
            if (cfg.Language == "zh" || cfg.Language == "en") _current = cfg.Language;
        }

        // Picks a text by the current language.
        public static string T(string zh, string en) => Zh ? zh : en;

        // Re-read the game's language now and then: the player can change it while the game runs.
        public static void Tick()
        {
            if (_cfg == null || (_cfg.Language != "auto" && _cfg.Language != null && _cfg.Language != "")) return;
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 2f;
            var code = GameLanguageCode();
            if (code == null) return;
            var lang = code.StartsWith("zh", StringComparison.OrdinalIgnoreCase) || code.StartsWith("chinese", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
            if (lang != _current || !_logged) MelonLogger.Msg($"language: {lang} (game: {code})");
            _logged = true;
            _current = lang;
        }

        private static string GameLanguageCode()
        {
            try
            {
                var locale = Singleton<GameManager>.Instance?.CurrentLocale;
                if (locale != null)
                {
                    var code = locale.Identifier.Code;
                    if (!string.IsNullOrEmpty(code)) return code;
                }
            }
            catch (Exception e) { Warn(e); }
            try
            {
                var current = Singleton<LanguageManager>.Instance?.currentLanguage;
                if (!string.IsNullOrEmpty(current)) return current;
            }
            catch (Exception e) { Warn(e); }
            return null;
        }

        private static void Warn(Exception e)
        {
            if (_warned) return;
            _warned = true;
            MelonLogger.Warning("could not read the game's language, keeping " + _current + ": " + e.Message);
        }
    }
}
