using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Il2CppTS;
using MelonLoader;
using UnityEngine;

namespace CozyClaude
{
    // Writes %LOCALAPPDATA%\CozyClaude\room.json every 2 s for the Claude Code status line, and keeps the
    // "Claude worked today" total in stats.json. Values are gathered on the main thread, written off it.
    internal sealed class StatusFile
    {
        private float _nextWrite;
        private float _nextStatsSave;
        private string _day;
        private double _todaySeconds;
        private int _writing;

        public double TodaySeconds => _todaySeconds;

        public StatusFile()
        {
            _day = DateTime.Now.ToString("yyyy-MM-dd");
            try
            {
                if (File.Exists(Paths.Stats))
                {
                    var s = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(Paths.Stats));
                    if (s != null && s.TryGetValue(_day, out var v)) _todaySeconds = v;
                }
            }
            catch (Exception e) { MelonLogger.Warning("stats.json unreadable: " + e.Message); }
        }

        public void Tick(Snapshot snap, RoomDriver room, Health health, bool ready)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            if (today != _day) { _day = today; _todaySeconds = 0; }
            if (snap.Working) _todaySeconds += Time.unscaledDeltaTime;

            var now = Time.unscaledTime;
            if (now >= _nextStatsSave)
            {
                _nextStatsSave = now + 30f;
                var day = _day; var secs = _todaySeconds;
                Task.Run(() => SaveStats(day, secs));
            }
            if (now < _nextWrite) return;
            _nextWrite = now + 2f;

            var status = new Dictionary<string, object>
            {
                ["v"] = 1,
                ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["ready"] = ready,
                ["blocked"] = room.Blocked,
                ["phase"] = room.Applied.ToString(),
                ["sessions"] = snap.AliveSessions,
                ["waiting"] = snap.Waiting,
                ["subagents"] = snap.Subagents,
                ["bridgeProblem"] = snap.Problem,
                ["claudeTodaySeconds"] = (long)_todaySeconds,
                // the room's language, so the Claude Code side writes its status line in the same one
                ["lang"] = Lang.Current,
            };
            if (ready)
                health.Run(Feature.Status, () =>
                {
                    var music = Singleton<Il2Cppcozyhouse.musicplayer.MusicPlayer>.Instance;
                    status["musicPlaying"] = music != null && music.IsPlaying;
                    status["music"] = music != null && music.CurrentMusic != null ? music.CurrentMusic.MusicName : null;
                    status["timer"] = room.TimerText();
                });
            status["health"] = health.Report();

            if (System.Threading.Interlocked.Exchange(ref _writing, 1) == 1) return;
            var json = JsonSerializer.Serialize(status);
            Task.Run(() =>
            {
                try { Directory.CreateDirectory(Paths.Root); Paths.WriteAtomic(Paths.RoomStatus, json); }
                catch (Exception e) { MelonLogger.Warning("could not write room.json: " + e.Message); }
                finally { System.Threading.Interlocked.Exchange(ref _writing, 0); }
            });
        }

        public void SaveNow() => SaveStats(_day, _todaySeconds);

        private static void SaveStats(string day, double secs)
        {
            try
            {
                var all = File.Exists(Paths.Stats)
                    ? JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(Paths.Stats)) ?? new()
                    : new Dictionary<string, double>();
                all[day] = Math.Round(secs);
                Directory.CreateDirectory(Paths.Root);
                Paths.WriteAtomic(Paths.Stats, JsonSerializer.Serialize(all));
            }
            catch (Exception e) { MelonLogger.Warning("could not write stats.json: " + e.Message); }
        }
    }
}
