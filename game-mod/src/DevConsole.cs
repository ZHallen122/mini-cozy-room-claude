using System;
using System.IO;
using System.Linq;
using System.Text;
using Il2Cppcozyhouse;
using Il2CppTS;
using Il2CppTSMultiPlay;
using MelonLoader;
using UnityEngine;

namespace CozyClaude
{
    // Dev-only command file, active only when UserData/CozyClaude/dev/ exists.
    // Write one command per line to dev/cmd.txt; results go to dev/out.txt and the MelonLoader log.
    internal sealed class DevConsole
    {
        private readonly string _dir;
        private readonly string _cmdPath;
        private readonly string _outPath;
        private DateTime _lastHandled = DateTime.MinValue;
        private float _nextPoll;
        private readonly StringBuilder _out = new();

        public DevConsole(string userDataDir)
        {
            _dir = Path.Combine(userDataDir, "dev");
            _cmdPath = Path.Combine(_dir, "cmd.txt");
            _outPath = Path.Combine(_dir, "out.txt");
        }

        public bool Enabled => Directory.Exists(_dir);

        public void Tick(Mod mod)
        {
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.5f;
            if (!File.Exists(_cmdPath)) return;
            var stamp = File.GetLastWriteTimeUtc(_cmdPath);
            if (stamp == _lastHandled) return;
            _lastHandled = stamp;
            string[] lines;
            try { lines = File.ReadAllLines(_cmdPath); File.Delete(_cmdPath); }
            catch (IOException) { return; }
            _out.Clear();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                Emit("> " + line);
                try { Run(mod, line); }
                catch (Exception e) { Emit("ERR " + e); }
            }
            File.WriteAllText(_outPath, _out.ToString());
        }

        private void Emit(string s)
        {
            _out.AppendLine(s);
            MelonLogger.Msg("[dev] " + s);
        }

        private void Run(Mod mod, string line)
        {
            var parts = line.Split(' ', 2);
            var arg = parts.Length > 1 ? parts[1] : "";
            switch (parts[0])
            {
                case "dump": Dump(); break;
                case "act": Player.Instance.ChangeActivityKey(int.Parse(arg)); Emit("activity now " + Player.Instance.GetCurrentActivityKey()); break;
                case "pettouch": Singleton<World>.Instance.petScript.TouchPetReAction(); break;
                case "petto":
                {
                    var room = Singleton<World>.Instance.CurrentRoom;
                    var deco = room.GetDecoInfo(arg);
                    Emit(deco == null ? "no deco " + arg : "moving pet to " + arg);
                    if (deco != null) Singleton<World>.Instance.petScript.UpdatePositionAndDirection(deco);
                    break;
                }
                case "petupdate": Singleton<World>.Instance.petScript.UpdateActivity(); break;
                case "charupdate": Singleton<World>.Instance.characterScript.UpdateActivity(); break;
                case "fireact": Player.Instance.OnActivityChanged?.Invoke(); break;
                case "effect": Singleton<World>.Instance.UpdateActivityEffect(); break;
                case "ui":
                {
                    Emit($"screen {Screen.width}x{Screen.height} cam={Camera.main?.name} ortho={Camera.main?.orthographic} size={Camera.main?.orthographicSize}");
                    var c = Singleton<World>.Instance.characterScript.transform.position;
                    Emit($"char world={c} screen={Camera.main?.WorldToScreenPoint(c)}");
                    foreach (var cv in UnityEngine.Object.FindObjectsOfType<Canvas>())
                        if (cv.isRootCanvas)
                            Emit($"  canvas {cv.name} mode={cv.renderMode} order={cv.sortingOrder} scale={cv.scaleFactor} cam={cv.worldCamera?.name} rect={cv.GetComponent<RectTransform>().rect}");
                    var tmp = Singleton<ToastManager>.Instance.multiplayText;
                    Emit($"toast font={tmp?.font?.name} size={tmp?.fontSize}");
                    break;
                }
                case "toast": Singleton<ToastManager>.Instance.ShowToast(arg); break;
                case "shot": ScreenCapture.CaptureScreenshot(Path.Combine(_dir, (arg.Length > 0 ? arg : "shot") + ".png")); break;
                case "state": Emit(mod.DescribeState()); break;
                default: mod.DevCommand(parts[0], arg, Emit); break;
            }
        }

        private void Dump()
        {
            var player = Player.Instance;
            Emit($"gameMode={Singleton<GameManager>.Instance.CurrentGameMode} loaded={Singleton<GameManager>.Instance.IsLoaded}");
            var mp = Singleton<MultiplayManager>.Instance;
            Emit(mp == null ? "multiplay: null" : $"multiplay: mode={mp.IsMultiplayMode} inLobby={mp.IsInLobby()} transitioning={mp.IsTransitioning}");
            Emit("activity current=" + player.GetCurrentActivityKey());
            foreach (var kv in player.ActivitySystem.GetActivityInfoDic())
            {
                var a = kv.Value;
                Emit($"  act {a.ActivityKey} deco={a.DecorableType} type={a.ActivityType} name={a.Name} frames={a.SpriteLength}");
            }
            var world = Singleton<World>.Instance;
            var room = world.CurrentRoom;
            Emit($"room={room?.name} cacheKey={room?.Cache?.Key}");
            if (room != null)
                foreach (var d in room.currentDecos)
                    if (d != null)
                        Emit($"  deco {d.GetDecoType()} cat={d.GetDecoCategory()} skin={d.GetCurrentSkinKey()} interact={room.IsDecoInteractable(d.GetDecoType())} active={d.gameObject.activeInHierarchy}");
            var pet = world.petScript;
            Emit($"pet key={pet?.currentPetKey} pos={pet?.transform.position} char pos={world.characterScript?.transform.position}");
            foreach (var s in player.SoundSystem.GetAllAmbienceCaches())
                Emit($"  amb {s.SoundKey} {s.SoundName} dlc={s.IsDLC}/{s.DLCType} playing={s.IsPlaying} vol={s.Volume}");
            var amb = Singleton<Il2Cpp.AmbiencePlayer>.Instance;
            Emit($"ambience preset={amb.CurPresetIndex} mute={amb.IsMute} anyPlaying={amb.IsAmbientSoundPlaying()}");
            var music = Singleton<Il2Cppcozyhouse.musicplayer.MusicPlayer>.Instance;
            Emit($"music playing={music.IsPlaying} key={music.CurrentMusicKey} name={music.CurrentMusic?.MusicName}");
            var tm = UnityEngine.Object.FindObjectOfType<Il2Cpp.TimerManager>();
            if (tm == null) Emit("timer manager: null");
            else
            {
                Emit($"timer tab={tm.TabType}");
                var st = tm.SessionTimerSystem;
                Emit($"  session playing={st.IsPlaying.Value} status={st.CurrentStatus.Value} cur={st.CurrentTimeSeconds.Value} sessMin={st.SessionMinutes.Value}");
                var sw = tm.StopwatchSystem;
                Emit($"  stopwatch playing={sw.IsPlaying.Value} total={sw.TotalSecond.Value}");
                var t = tm.TimerSystem;
                Emit($"  timer playing={t.IsPlaying.Value} total={t.TotalSeconds.Value}");
            }
            var ud = player.UserData;
            Emit($"stats activityCount={ud.playerUserData.activityCount} level={ud.playerUserData.level} exp={ud.playerUserData.exp} completeTimer={ud.playerUserData.completeAmountTimer}");
        }
    }
}
