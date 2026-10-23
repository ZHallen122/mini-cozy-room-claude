using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2Cppcozyhouse;
using Il2Cppcozyhouse.dlc;
using Il2CppTS;
using Il2CppTSMultiPlay;
using MelonLoader;
using UnityEngine;

namespace CozyClaude
{
    internal enum Phase { Unknown, Off, Working, Resting }

    // Turns the folded Claude Code state into room changes. Changes happen on transitions only, so the player
    // can still move the character by hand in between, and nothing is touched while in a co-op room.
    internal sealed class RoomDriver
    {
        private readonly Config _cfg;
        private readonly Health _health;
        private readonly Journal _journal;
        private readonly Bubble _bubble = new();
        private readonly PetSquad _pets = new();
        private readonly Effects _effects = new();
        private readonly TaskSync _tasks;
        private readonly Attention _attention;
        private Il2Cpp.TimerManager _timerManager;
        private bool _timerStartedByMod;
        private bool _rainOn;
        private bool _blocked;
        private float _nextNudge;
        private string _lastSignature;
        // celebrations: the newest one already played, and what the bubble says until when
        private long _celebratedAt;
        private string _partyKind;
        private float _partyUntil;
        private float _lastTestsParty = -999f;
        // the sleepy character: the session whose context is nearly full, and the "awake again" line after it
        private string _sleepySid;
        private int _sleepyPercent;
        private float _awakeUntil;

        // dev console only: behave as if in a co-op room
        internal static bool SimulateCoop;

        public Phase Applied { get; private set; } = Phase.Unknown;
        public bool Blocked => _blocked;

        public RoomDriver(Config cfg, Health health)
        {
            _cfg = cfg;
            _health = health;
            _journal = Journal.Load();
            _tasks = new TaskSync(cfg, health);
            _attention = new Attention(cfg, health);
            _rainOn = _journal.Rain != null; // rain left over from a crash gets switched off on the first tick
        }

        public static bool GameReady()
        {
            var gm = Singleton<GameManager>.Instance;
            var world = Singleton<World>.Instance;
            return gm != null && gm.IsLoaded && Player.Instance != null && world != null && world.characterScript != null;
        }

        public void Tick(Snapshot s)
        {
            if (!UpdateBlocked()) return;

            var signature = $"waiting={s.Waiting} error={s.Error} subagents={s.Subagents}";
            if (signature != _lastSignature) { MelonLogger.Msg(signature); _lastSignature = signature; }

            var target = s.AliveSessions == 0 ? Phase.Off : s.Working ? Phase.Working : Phase.Resting;
            if (target != Applied) Transition(target);

            ForgetRestoreOnceSaved();
            UpdateFun(s);
            UpdateBubble(s);
            _attention.Update(Applied == Phase.Off ? 0 : s.Waiting);
            _tasks.Tick(s);
            UpdateRain(s.Error && _cfg.RainOnError && s.AliveSessions > 0);
            _health.Run(Feature.Subagents, () => _pets.Sync(Applied == Phase.Off ? 0 : Mathf.Min(s.Subagents, _cfg.MaxSubagentPets)));
        }

        // Read-only probe of everything each feature touches, run once when the room is ready. After a game update
        // this names the broken feature right away instead of at its first use.
        public void SelfCheck()
        {
            T Need<T>(T value, string what) where T : class => value ?? throw new InvalidOperationException(what + " not found");
            _health.Run(Feature.Guard, () => Need(Singleton<MultiplayManager>.Instance, "MultiplayManager").IsInLobby());
            _health.Run(Feature.Work, () =>
            {
                var activities = Player.Instance.ActivitySystem.GetActivityInfoDic();
                if (!_cfg.WorkActivities.Concat(_cfg.RestActivities).Any(activities.ContainsKey))
                    throw new InvalidOperationException("none of the configured activity keys exist");
                Need(Singleton<World>.Instance.characterScript, "Character");
            });
            _health.Run(Feature.Restore, () => Player.Instance.GetCurrentActivityKey());
            _health.Run(Feature.Waiting, () => Need(Singleton<World>.Instance.petScript, "Pet"));
            if (_cfg.TimerMode != "off")
                _health.Run(Feature.Timer, () =>
                {
                    var tm = Need(Timers(), "TimerManager");
                    _ = tm.StopwatchSystem.IsPlaying.Value;
                    _ = tm.SessionTimerSystem.IsPlaying.Value;
                });
            _health.Run(Feature.Rain, () =>
            {
                _ = Need(Singleton<Il2Cpp.AmbiencePlayer>.Instance, "AmbiencePlayer").IsMute;
                Need(Player.Instance.SoundSystem.GetCache(_cfg.RainSoundKey), $"sound {_cfg.RainSoundKey}");
                Need(Singleton<DLCManager>.Instance, "DLCManager");
            });
            _health.Run(Feature.Subagents, () =>
            {
                Need(Singleton<PetSpriteLoader>.Instance, "PetSpriteLoader");
                _ = GameInfo.Instance.Pets.Count;
            });
            _health.Run(Feature.Status, () => Need(Singleton<Il2Cppcozyhouse.musicplayer.MusicPlayer>.Instance, "MusicPlayer"));
            _health.Run(Feature.Celebrate, () => Need(Singleton<ToastManager>.Instance, "ToastManager"));
            _health.Run(Feature.Sleepy, () => Need(Singleton<ToastManager>.Instance, "ToastManager"));
            _tasks.SelfCheck();
            _attention.SelfCheck();
        }

        // Every frame: keep the bubble over the character (the rest of the driver runs at 4 Hz).
        public void FollowBubble()
        {
            if (_bubble.Visible) _health.Run(Feature.Waiting, _bubble.Follow);
            if (_blocked) return;
            if (!_health.Run(Feature.Celebrate, _effects.AnimateConfetti)) _effects.ClearConfetti();
            if (!_health.Run(Feature.Sleepy, _effects.AnimateZees)) _effects.ClearZees();
        }

        // dev console: play a celebration now, as if a session had just sent it
        internal void DevParty(string kind) => _health.Run(Feature.Celebrate, () => Celebrate(kind));

        // Called when the game quits: put the room back so the save never keeps Claude's state.
        public void Shutdown()
        {
            if (!GameReady() || _blocked) return;
            PauseTimer();
            UpdateRain(false);
            _health.Run(Feature.Restore, RestoreActivity);
            _attention.Stop();
            _bubble.Destroy();
            _effects.Destroy();
            _pets.Clear();
        }

        // For Claude's requested operations: true while the room must not be touched (evaluated now, not at the last tick).
        public bool CheckBlocked() => !UpdateBlocked();

        // Claude started or paused the pomodoro because the user asked: it is the user's timer from now on, so the
        // working/resting linkage must not pause it.
        public void TimerTakenOver()
        {
            if (_cfg.TimerMode == "pomodoro") _timerStartedByMod = false;
        }

        // Claude changed the ambience because the user asked: the rain backup would undo that when the rain ends,
        // so forget it. The rain itself is stopped or kept by the request's own stop/play.
        public void AmbienceTakenOver()
        {
            if (_journal.Rain == null) return;
            _journal.Rain = null;
            _journal.Save();
        }

        // Returns false while the room must not be touched (co-op room, visitors, or unknown multiplayer state).
        private bool UpdateBlocked()
        {
            var blocked = true;
            if (!_health.Run(Feature.Guard, () =>
                {
                    var mp = Singleton<MultiplayManager>.Instance;
                    blocked = SimulateCoop || mp == null || mp.IsMultiplayMode || mp.IsInLobby() || mp.IsTransitioning;
                }))
                blocked = true; // the guard itself is broken: stay hands-off for good
            if (blocked && !_blocked)
            {
                _bubble.Hide();
                _pets.Clear();
                _effects.ClearConfetti();
                _effects.ClearZees();
                _attention.Stop();
                _health.Note(Feature.Guard, "联机房间中，暂停联动");
            }
            else if (!blocked && _blocked)
            {
                _health.Note(Feature.Guard, null);
                Applied = Phase.Unknown; // re-apply the current state once back in the own room
            }
            _blocked = blocked;
            return !blocked;
        }

        private void Transition(Phase target)
        {
            MelonLogger.Msg($"phase {Applied} -> {target} (activity {Player.Instance.GetCurrentActivityKey()})");
            switch (target)
            {
                case Phase.Working:
                    _health.Run(Feature.Work, () => SetActivityFrom(_cfg.WorkActivities, "书桌"));
                    StartTimer();
                    break;
                case Phase.Resting:
                    _health.Run(Feature.Work, () => SetActivityFrom(_cfg.RestActivities, "床/沙发/懒人沙发"));
                    PauseTimer();
                    break;
                case Phase.Off:
                    PauseTimer();
                    if (_cfg.RestoreOnSessionEnd)
                    {
                        _health.Run(Feature.Restore, RestoreActivity);
                        UpdateRain(false);
                    }
                    else
                    {
                        _journal.OriginalActivity = null;
                        _journal.LastSetActivity = null;
                        _journal.Save();
                    }
                    _bubble.Hide();
                    _pets.Clear();
                    break;
            }
            Applied = target;
        }

        private void SetActivityFrom(int[] candidates, string furniture)
        {
            var key = PickActivity(candidates);
            if (key == null)
            {
                _health.Note(Feature.Work, $"当前房间没有可用的{furniture}");
                return;
            }
            _health.Note(Feature.Work, null);
            var player = Player.Instance;
            var current = player.GetCurrentActivityKey();
            if (_journal.RestoredAt != null)
            {
                // A finished episode whose restore may not be saved yet. Showing Claude's last activity means a
                // stale save, so the recorded original still holds; anything else is the player's own state.
                if (current != _journal.LastSetActivity) _journal.OriginalActivity = current;
                _journal.RestoredAt = null;
            }
            if (_journal.OriginalActivity == null) _journal.OriginalActivity = current;
            ApplyActivity(key.Value);
            _journal.LastSetActivity = key.Value;
            _journal.Save();
        }

        private void RestoreActivity()
        {
            if (_journal.OriginalActivity == null) return;
            var current = Player.Instance.GetCurrentActivityKey();
            if (_journal.RestoredAt != null && current != _journal.LastSetActivity)
            {
                // an earlier restore already reached the save (or the player chose something since): done
                _journal.OriginalActivity = null;
                _journal.LastSetActivity = null;
                _journal.RestoredAt = null;
                _journal.Save();
                return;
            }
            // If the player picked something by hand since, keep their choice.
            if (_journal.LastSetActivity == null || current == _journal.LastSetActivity)
                ApplyActivity(_journal.OriginalActivity.Value);
            // The game saves on its own schedule, so the save on disk may still hold Claude's activity. Keep the
            // entry until a save newer than this restore exists (ForgetRestoreOnceSaved); a restart before that
            // finds Claude's activity again and restores once more.
            _journal.RestoredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _journal.Save();
        }

        private float _nextSaveCheck;
        private string _saveFile;

        private void ForgetRestoreOnceSaved()
        {
            if (_journal.RestoredAt == null || Time.unscaledTime < _nextSaveCheck) return;
            _nextSaveCheck = Time.unscaledTime + 3f;
            if (_saveFile == null || !File.Exists(_saveFile))
                _saveFile = Directory.GetFiles(Application.persistentDataPath, "SaveFile_Alpha.es3", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (_saveFile == null) return;
            var savedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(_saveFile)).ToUnixTimeMilliseconds();
            if (savedAt <= _journal.RestoredAt + 500) return;
            _journal.OriginalActivity = null;
            _journal.LastSetActivity = null;
            _journal.RestoredAt = null;
            _journal.Save();
            MelonLogger.Msg("restored room is in the save; journal cleared");
        }

        private static int? PickActivity(int[] candidates)
        {
            var activities = Player.Instance.ActivitySystem.GetActivityInfoDic();
            var room = Singleton<World>.Instance.CurrentRoom;
            if (room == null) return null;
            foreach (var key in candidates)
            {
                if (!activities.ContainsKey(key)) continue;
                var type = activities[key].DecorableType;
                if (type == "None" || room.IsDecoInteractable(type)) return key;
            }
            return null;
        }

        private static void ApplyActivity(int key)
        {
            var player = Player.Instance;
            var before = player.GetCurrentActivityKey();
            MelonLogger.Msg($"activity {before} -> {key}");
            if (before == key) return;
            // ChangeActivityKey also bumps the "activities changed" stat; keep the player's stats untouched.
            var stats = player.UserData.playerUserData;
            var count = stats.activityCount;
            player.ChangeActivityKey(key);
            stats.activityCount = count;
            var world = Singleton<World>.Instance;
            world.characterScript.UpdateActivity();
            if (world.petScript != null) world.petScript.UpdateActivity();
            world.UpdateActivityEffect();
        }

        // Celebrations and the sleepy character. Both only decorate: no activity change, no sound, nothing saved.
        private void UpdateFun(Snapshot s)
        {
            if (Applied == Phase.Off)
            {
                _effects.ClearConfetti();
                _effects.ClearZees();
                _partyUntil = 0f;
                _awakeUntil = 0f;
                _sleepySid = null;
                return;
            }
            if (s.CelebrationAt > _celebratedAt)
            {
                _celebratedAt = s.CelebrationAt;
                // only a fresh one: a game started (or a co-op room left) later doesn't replay old news. Passing tests
                // come often, so at most one party for them every 90 s.
                var fresh = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - s.CelebrationAt < 15_000;
                var tests = s.Celebration == "tests";
                if (_cfg.Celebrate && fresh && !(tests && (!_cfg.CelebrateTests || Time.unscaledTime - _lastTestsParty < 90f)))
                    _health.Run(Feature.Celebrate, () => Celebrate(s.Celebration));
            }
            if (_health.IsBroken(Feature.Sleepy)) return;
            var threshold = _cfg.SleepyAtContextPercent;
            var sleepy = threshold > 0 && s.Context >= threshold;
            if (!sleepy && _sleepySid != null)
            {
                // the same conversation got compacted (or cleared): wide awake again. A session that ended or another
                // one that took over just ends the nap quietly.
                var was = s.Sessions.FirstOrDefault(x => x.Sid == _sleepySid);
                if (was != null && !(was.Context >= threshold)) _awakeUntil = Time.unscaledTime + 4f;
                _sleepySid = null;
            }
            if (sleepy)
            {
                if (_sleepySid == null) MelonLogger.Msg($"sleepy: context {s.Context}%");
                _sleepySid = s.ContextSid;
                _sleepyPercent = s.Context ?? 0;
            }
            _effects.SetSleepy(sleepy);
        }

        private void Celebrate(string kind)
        {
            MelonLogger.Msg("celebrating " + kind);
            _effects.Celebrate(kind);
            _partyKind = kind;
            _partyUntil = Time.unscaledTime + 3.5f;
            if (kind == "tests") _lastTestsParty = Time.unscaledTime;
            var pet = Singleton<World>.Instance.petScript;
            if (pet != null && pet.gameObject.activeInHierarchy)
            {
                // the pet's reaction may count as a touch in the player's stats: keep those the player's
                var stats = StatSnapshot.Take();
                pet.TouchPetReAction();
                stats.Restore();
            }
        }

        private static string PartyText(string kind) => kind switch
        {
            "merge" => Lang.T("PR 合并啦！", "PR merged!"),
            "tests" => Lang.T("测试全过了！", "All tests pass!"),
            _ => Lang.T("提交成功！", "Committed!"),
        };

        // The bubble shows the "waiting for you" reminder first, then a celebration, then (while Claude works) what it
        // is doing; resting with a nearly full context, how sleepy it is.
        private void UpdateBubble(Snapshot s)
        {
            if (Applied == Phase.Off)
            {
                _bubble.Hide();
                return;
            }
            if (s.Waiting > 0 && !_health.IsBroken(Feature.Waiting))
            {
                _health.Run(Feature.Waiting, () =>
                {
                    _bubble.Show(s.Waiting > 1 ? $"{_cfg.Waiting()} ×{s.Waiting}" : _cfg.Waiting());
                    if (_cfg.PetNudgeWhileWaiting && Time.unscaledTime >= _nextNudge)
                    {
                        _nextNudge = Time.unscaledTime + 6f;
                        var pet = Singleton<World>.Instance.petScript;
                        if (pet != null && pet.gameObject.activeInHierarchy) pet.TouchPetReAction();
                    }
                });
                if (!_health.IsBroken(Feature.Waiting)) return;
            }
            if (Time.unscaledTime < _partyUntil && !_health.IsBroken(Feature.Celebrate))
            {
                _health.Run(Feature.Celebrate, () => _bubble.Show(PartyText(_partyKind)));
                if (!_health.IsBroken(Feature.Celebrate)) return;
            }
            if (Time.unscaledTime < _awakeUntil && !_health.IsBroken(Feature.Sleepy))
            {
                _health.Run(Feature.Sleepy, () => _bubble.Show(Lang.T("睡醒了，精神满满！", "Fresh context, wide awake!"), emphasized: false));
                if (!_health.IsBroken(Feature.Sleepy)) return;
            }
            if (_cfg.ShowActivity && Applied == Phase.Working && !string.IsNullOrEmpty(s.Activity) && !_health.IsBroken(Feature.Activity))
            {
                _health.Run(Feature.Activity, () => _bubble.Show(Activity.Text(s.Activity), emphasized: false));
                if (!_health.IsBroken(Feature.Activity)) return;
            }
            if (_sleepySid != null && Applied == Phase.Resting && !_health.IsBroken(Feature.Sleepy))
            {
                _health.Run(Feature.Sleepy, () => _bubble.Show(
                    Lang.T($"上下文快满了，好困… {_sleepyPercent}%", $"Context nearly full, so sleepy… {_sleepyPercent}%"), emphasized: false));
                if (!_health.IsBroken(Feature.Sleepy)) return;
            }
            _bubble.Hide();
        }

        private Il2Cpp.TimerManager Timers()
        {
            if (_timerManager == null) _timerManager = UnityEngine.Object.FindObjectOfType<Il2Cpp.TimerManager>();
            return _timerManager;
        }

        private void StartTimer()
        {
            if (_cfg.TimerMode == "off") return;
            _health.Run(Feature.Timer, () =>
            {
                var tm = Timers();
                if (tm == null) { _health.Note(Feature.Timer, "找不到计时器"); return; }
                if (_cfg.TimerMode == "pomodoro")
                {
                    var st = tm.SessionTimerSystem;
                    if (!st.IsPlaying.Value) { st.Start(); _timerStartedByMod = true; }
                }
                else
                {
                    var sw = tm.StopwatchSystem;
                    if (!sw.IsPlaying.Value) { sw.Toggle(); _timerStartedByMod = true; }
                }
            });
        }

        private void PauseTimer()
        {
            if (!_timerStartedByMod) return;
            _timerStartedByMod = false;
            _health.Run(Feature.Timer, () =>
            {
                var tm = Timers();
                if (tm == null) return;
                if (_cfg.TimerMode == "pomodoro")
                {
                    if (tm.SessionTimerSystem.IsPlaying.Value) tm.SessionTimerSystem.Pause();
                }
                else if (tm.StopwatchSystem.IsPlaying.Value) tm.StopwatchSystem.Toggle();
            });
        }

        private void UpdateRain(bool want)
        {
            if (want == _rainOn) return;
            _health.Run(Feature.Rain, () =>
            {
                if (want) StartRain(); else StopRain();
            });
            if (_health.IsBroken(Feature.Rain)) _rainOn = want; // stop retrying every frame
        }

        private void StartRain()
        {
            var amb = Singleton<Il2Cpp.AmbiencePlayer>.Instance;
            var sounds = Player.Instance.SoundSystem;
            var rain = sounds.GetCache(_cfg.RainSoundKey);
            if (rain == null || (rain.IsDLC && !Singleton<DLCManager>.Instance.IsDLCSoundAccessible(rain)))
            {
                _health.Note(Feature.Rain, "雨声不可用（不存在或属于未购买的 DLC）");
                _rainOn = true; // don't retry until the error clears
                return;
            }
            if (amb.IsMute && !_cfg.RainUnmutes)
            {
                _health.Note(Feature.Rain, "环境音已静音，雨声不会响（config.json 里 RainUnmutes=true 可临时取消静音）");
                _rainOn = true;
                return;
            }
            _health.Note(Feature.Rain, null);
            var playing = new List<int>();
            foreach (var c in sounds.GetAllAmbienceCaches())
                if (c.IsPlaying) playing.Add(c.SoundKey);
            _journal.Rain = new Journal.RainBackup { WasMuted = amb.IsMute, Playing = playing.ToArray() };
            _journal.Save();
            // Unmute first: unmuting brings back whatever played when the ambience was muted, so stopping before
            // it would be undone.
            if (amb.IsMute && _cfg.RainUnmutes) amb.UnmuteAmbientSound();
            foreach (var key in playing)
                if (key != rain.SoundKey) amb.StopAmbientSound(sounds.GetCache(key));
            if (!rain.IsPlaying) amb.PlayAmbientSound(rain);
            _rainOn = true;
        }

        private void StopRain()
        {
            _rainOn = false;
            var backup = _journal.Rain;
            if (backup == null) return;
            var amb = Singleton<Il2Cpp.AmbiencePlayer>.Instance;
            var sounds = Player.Instance.SoundSystem;
            var rain = sounds.GetCache(_cfg.RainSoundKey);
            if (rain != null && rain.IsPlaying && !backup.Playing.Contains(rain.SoundKey)) amb.StopAmbientSound(rain);
            foreach (var key in backup.Playing)
            {
                var c = sounds.GetCache(key);
                if (c != null && !c.IsPlaying) amb.PlayAmbientSound(c);
            }
            // mute last: the game remembers what played at mute time and brings exactly that back on unmute
            if (backup.WasMuted && !amb.IsMute) amb.MuteAmbientSound();
            _journal.Rain = null;
            _journal.Save();
        }

        public string TimerText()
        {
            var tm = Timers();
            if (tm == null) return null;
            if (_cfg.TimerMode == "pomodoro")
            {
                var st = tm.SessionTimerSystem;
                return $"🍅 {PomodoroPhase(st.CurrentStatus.Value)} {Clock(st.CurrentTimeSeconds.Value)}{(st.IsPlaying.Value ? "" : Paused)}";
            }
            var sw = tm.StopwatchSystem;
            var text = $"{Clock(sw.TotalSecond.Value)}{(sw.IsPlaying.Value ? "" : Paused)}";
            // a pomodoro the user (or Claude, when asked) started shows too
            var pomo = tm.SessionTimerSystem;
            if (pomo.CurrentStatus.Value != Il2Cpp.ETimerStatus.Ready)
                text += $" · 🍅 {PomodoroPhase(pomo.CurrentStatus.Value)} {Clock(pomo.CurrentTimeSeconds.Value)}{(pomo.IsPlaying.Value ? "" : Paused)}";
            return text;
        }

        // Shown in the Claude Code status line through room.json, in the room's language.
        private static string Paused => Lang.T(" (暂停)", " (paused)");

        private static string PomodoroPhase(Il2Cpp.ETimerStatus s) =>
            s == Il2Cpp.ETimerStatus.Break ? Lang.T("休息", "Break") : s == Il2Cpp.ETimerStatus.Ready ? Lang.T("待开始", "Ready") : Lang.T("专注", "Focus");

        internal static string Clock(float seconds)
        {
            var t = System.TimeSpan.FromSeconds(Mathf.Max(0f, seconds));
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
        }
    }
}
