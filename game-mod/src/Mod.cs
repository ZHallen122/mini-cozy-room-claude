using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(CozyClaude.Mod), "CozyClaude", "0.4.0", "ZHallen122")]
[assembly: MelonGame("TesseractStudio", "MiniCozyRoom")]

namespace CozyClaude
{
    public sealed class Mod : MelonMod
    {
        private Config _cfg;
        private Health _health;
        private SessionBridge _bridge;
        private RoomDriver _room;
        private StatusFile _status;
        private DevConsole _dev;
        private CommandBridge _commands;
        private RoomActions _actions;
        private float _nextApply;
        private bool _wasReady;

        public override void OnInitializeMelon()
        {
            Directory.CreateDirectory(Paths.Root);
            _cfg = Config.Load();
            _health = new Health();
            _room = new RoomDriver(_cfg, _health);
            _status = new StatusFile();
            _bridge = new SessionBridge();
            _actions = new RoomActions(_room, _health);
            _commands = new CommandBridge();
            _dev = new DevConsole(Path.Combine(MelonEnvironment.UserDataDirectory, "CozyClaude"));
            LoggerInstance.Msg($"CozyClaude loaded. data dir: {Paths.Root}; dev console: {_dev.Enabled}");
        }

        public override void OnUpdate()
        {
            try
            {
                var snap = _bridge.Latest;
                var ready = RoomDriver.GameReady();
                if (ready && !_wasReady)
                {
                    LoggerInstance.Msg("room ready, linking with Claude Code");
                    _room.SelfCheck();
                    _actions.SelfCheck();
                }
                _wasReady = ready;
                if (ready)
                {
                    // the bubble follows the character every frame; everything else runs at 4 Hz
                    if (UnityEngine.Time.unscaledTime >= _nextApply)
                    {
                        _nextApply = UnityEngine.Time.unscaledTime + 0.25f;
                        _room.Tick(snap);
                    }
                    _room.FollowBubble();
                    _actions.Tick();
                    _health.FlushToasts();
                }
                RunCommands(ready);
                _status.Tick(snap, _room, _health, ready);
                if (_dev.Enabled) _dev.Tick(this);
            }
            catch (Exception e)
            {
                // never let an exception escape into the game loop
                LoggerInstance.Error("tick failed: " + e);
            }
        }

        // Claude's requested operations (v2). At most a few per frame; each one is answered, even when it can't run.
        private void RunCommands(bool ready)
        {
            for (var i = 0; i < 4 && _commands.TryTake(out var cmd); i++)
            {
                CommandResult result;
                try { result = _actions.Execute(cmd, ready); }
                catch (Exception e) { result = CommandResult.Fail("执行出错：" + e.Message); LoggerInstance.Error("command failed: " + e); }
                _commands.Reply(cmd, result);
                if (ready) _health.FlushToasts();
            }
        }

        public override void OnApplicationQuit()
        {
            try
            {
                _room.Shutdown();
                _status.SaveNow();
                _bridge.Dispose();
                _commands.Dispose();
            }
            catch (Exception e) { LoggerInstance.Error("shutdown failed: " + e); }
        }

        internal string DescribeState()
        {
            var s = _bridge.Latest;
            return $"sessions={s.AliveSessions} working={s.Working} waiting={s.Waiting} error={s.Error} subagents={s.Subagents} problem={s.Problem}\n" +
                   $"activity={s.Activity} tasks={string.Join(" | ", System.Linq.Enumerable.Select(s.Sessions, x => $"{x.Project}:{x.Tasks.Count}"))}\n" +
                   $"phase={_room.Applied} blocked={_room.Blocked} today={_status.TodaySeconds:0}s\n" +
                   string.Join("\n", System.Linq.Enumerable.Select(_health.Report(), kv => $"  {kv.Key}: {kv.Value}"));
        }

        internal void DevCommand(string cmd, string arg, Action<string> emit)
        {
            switch (cmd)
            {
                // maintenance helpers used while developing; they only touch what the dev session changed
                case "swreset": UnityEngine.Object.FindObjectOfType<Il2Cpp.TimerManager>().StopwatchSystem.Reset(); break;
                case "actcount": Il2Cppcozyhouse.Player.Instance.UserData.playerUserData.activityCount = int.Parse(arg); break;
                case "ambmute": Il2CppTS.Singleton<Il2Cpp.AmbiencePlayer>.Instance.MuteAmbientSound(); break;
                case "find":
                    foreach (var tr in UnityEngine.Object.FindObjectsOfType<UnityEngine.Transform>(true))
                        if (tr.name.Contains(arg))
                        {
                            emit($"{tr.name} active={tr.gameObject.activeInHierarchy} pos={tr.position} scale={tr.lossyScale} layer={tr.gameObject.layer} parent={tr.parent?.name}");
                            foreach (var sr in tr.GetComponentsInChildren<UnityEngine.SpriteRenderer>(true))
                                emit($"   sr {sr.name} enabled={sr.enabled} sprite={sr.sprite?.name} color={sr.color} layer={sr.sortingLayerName}/{sr.sortingOrder}");
                            var pet = tr.GetComponent<Il2Cppcozyhouse.world.Pet>();
                            if (pet != null) emit($"   pet key={pet.currentPetKey} actions={pet.actions?.Count}");
                        }
                    break;
                case "srinfo":
                    foreach (var sr in UnityEngine.Object.FindObjectsOfType<UnityEngine.SpriteRenderer>())
                        if (sr.name == "Pet" || sr.name.StartsWith("CozyClaude"))
                            emit($"{sr.name} sprite={sr.sprite?.name} tex={sr.sprite?.texture?.name} ppu={sr.sprite?.pixelsPerUnit} bounds={sr.bounds} visible={sr.isVisible} shader={sr.sharedMaterial?.shader?.name} mask={sr.maskInteraction} sortLayer={sr.sortingLayerID} order={sr.sortingOrder} rendLayer={sr.renderingLayerMask}");
                    break;
                case "comps":
                    foreach (var tr in UnityEngine.Object.FindObjectsOfType<UnityEngine.Transform>())
                        if (tr.name == arg)
                            foreach (var c in tr.GetComponents<UnityEngine.Component>())
                                emit($"{tr.name}: {c.GetIl2CppType().FullName}");
                    break;
                case "petinfo":
                    foreach (var kv in Il2Cppcozyhouse.GameInfo.Instance.Pets)
                        emit($"pet {kv.Key} cat={kv.Value.PetCategory} unlockLevel={kv.Value.UnlockLevel} icon={kv.Value.IconPath}");
                    break;
                case "amb":
                {
                    var amb = Il2CppTS.Singleton<Il2Cpp.AmbiencePlayer>.Instance;
                    var parts = arg.Split(' ');
                    var c = Il2Cppcozyhouse.Player.Instance.SoundSystem.GetCache(int.Parse(parts[0]));
                    if (parts.Length > 1)
                        switch (parts[1])
                        {
                            case "stop": amb.StopAmbientSound(c); break;
                            case "play": amb.PlayAmbientSound(c); break;
                            case "flagoff": c.SetAmbiencePlay(false); break;
                            case "flagon": c.SetAmbiencePlay(true); break;
                            case "presetoff": amb.CurPreset.presetDic[c.SoundKey].isPlaying = false; break;
                        }
                    var inPreset = amb.CurPreset.presetDic.ContainsKey(c.SoundKey) ? amb.CurPreset.presetDic[c.SoundKey].isPlaying.ToString() : "-";
                    emit($"{c.SoundKey} IsPlaying={c.IsPlaying} user={c._userData?.isPlaying} audible={amb.IsAmbientSoundPlaying(c)} preset={inPreset} mute={amb.IsMute}");
                    break;
                }
                case "stats":
                {
                    // every int stat of the player, plus the notes and sound state the v2 operations touch
                    var ud = Il2Cppcozyhouse.Player.Instance.UserData.playerUserData;
                    foreach (var p in ud.GetType().GetProperties())
                        if (p.PropertyType == typeof(int)) emit($"stat {p.Name}={p.GetValue(ud)}");
                    var memos = Il2CppTS.Singleton<Il2Cpp.MemoManager>.Instance;
                    foreach (var kv in memos._cacheDic) emit($"memo {kv.Key} title={kv.Value.Title} open={kv.Value.IsOpen}");
                    emit($"memo active={memos.IsActivate.Value}");
                    var todos = Il2CppTS.Singleton<Il2Cpp.TodoListManager>.Instance;
                    foreach (var kv in todos._cacheDic) emit($"todo {kv.Key} title={kv.Value.Title} open={kv.Value.IsOpen} items={kv.Value.GetTotalCount()}");
                    var music = Il2CppTS.Singleton<Il2Cppcozyhouse.musicplayer.MusicPlayer>.Instance;
                    emit($"music key={music.CurrentMusicKey} name={music.CurrentMusic?.MusicName} album={music.CurrentAlbum?.Name} playing={music.IsPlaying} mute={music.IsMute}");
                    var amb = Il2CppTS.Singleton<Il2Cpp.AmbiencePlayer>.Instance;
                    foreach (var c in Il2Cppcozyhouse.Player.Instance.SoundSystem.GetAllAmbienceCaches())
                        if (c.IsPlaying) emit($"amb playing {c.SoundKey} {c.SoundName}");
                    emit($"amb mute={amb.IsMute} preset={amb.CurPresetIndex}");
                    var st = UnityEngine.Object.FindObjectOfType<Il2Cpp.TimerManager>().SessionTimerSystem;
                    emit($"session status={st.CurrentStatus.Value} playing={st.IsPlaying.Value} cur={st.CurrentTimeSeconds.Value} min={st.SessionMinutes.Value} clear={st.ClearSessionCount}");
                    break;
                }
                case "stat":
                {
                    var parts = arg.Split(' ');
                    var ud = Il2Cppcozyhouse.Player.Instance.UserData.playerUserData;
                    ud.GetType().GetProperty(parts[0]).SetValue(ud, int.Parse(parts[1]));
                    emit($"stat {parts[0]}={ud.GetType().GetProperty(parts[0]).GetValue(ud)}");
                    break;
                }
                // simulate a game update breaking a feature: break Music
                case "break": _health.MarkBroken(Enum.Parse<Feature>(arg), new MissingMethodException("simulated by the dev console")); break;
                case "todoitems":
                {
                    var cache = Il2CppTS.Singleton<Il2Cpp.TodoListManager>.Instance.GetCache(int.Parse(arg));
                    foreach (var it in cache.GetOrderedTodoItems()) emit($"item {it.Key} folder={it.IsFolder} done={it.IsComplete} content='{it.Content}'");
                    break;
                }
                case "streset": UnityEngine.Object.FindObjectOfType<Il2Cpp.TimerManager>().SessionTimerSystem.Reset(); break;
                case "coop": RoomDriver.SimulateCoop = arg == "on"; break;
                case "tray":
                    emit($"tray init={Il2CppUtils.TrayIcon.IsInitialized()} hiddenFromTaskbar={Il2CppUtils.TrayIcon.isHiddenFromTaskbar} tip='{Il2CppUtils.TrayIcon.notifyIconData.szTip}' hwnd={Il2CppUtils.TrayIcon.unityWindowHandle}");
                    break;
                case "retitle":
                {
                    var parts = arg.Split(' ', 2);
                    Il2CppTS.Singleton<Il2Cpp.TodoListManager>.Instance.GetCache(int.Parse(parts[0])).GetTodolistUserData().SetTitle(parts[1]);
                    break;
                }
                case "petkey": PetSquad.ForceKey = int.Parse(arg); break;
                case "exp": Il2Cppcozyhouse.Player.Instance.UserData.playerUserData.exp = int.Parse(arg); break;
                default: emit("unknown command " + cmd); break;
            }
        }
    }
}
