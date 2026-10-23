using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2Cppcozyhouse;
using Il2Cppcozyhouse.data;
using Il2Cppcozyhouse.dlc;
using Il2Cppcozyhouse.musicplayer;
using Il2CppTS;
using MelonLoader;

namespace CozyClaude
{
    // v2 "active operations": what Claude does in the room when the user asks for it in a Claude Code conversation.
    // Runs on the main thread. Nothing runs in a co-op room, nothing plays content the player doesn't own, and the
    // memos/todo lists Claude adds are titled with a marker and recorded in claude-notes.json, so "clear" removes
    // exactly those and never a note of the player's.
    internal sealed class RoomActions
    {
        public const string Marker = "[Claude] ";
        private const int MaxNotes = 20;

        private readonly RoomDriver _room;
        private readonly Health _health;

        public RoomActions(RoomDriver room, Health health)
        {
            _room = room;
            _health = health;
        }

        public CommandResult Execute(Command cmd, bool ready)
        {
            if (!ready) return CommandResult.Fail("房间还没加载好（游戏在标题画面或正在加载），稍后再试。");
            if (_room.CheckBlocked())
                return CommandResult.Fail("你现在在联机房间（或大厅）里，CozyClaude 联机时不改任何东西，这次没有执行。");
            switch (cmd.Op)
            {
                case "memo.add": return _health.Command(Feature.Notes, () => AddMemo(cmd));
                case "todo.add": return _health.Command(Feature.Notes, () => AddTodo(cmd));
                case "notes.clear": return _health.Command(Feature.Notes, ClearNotes);
                case "timer": return _health.Command(Feature.Pomodoro, () => Timer(cmd.Str("action", 16)));
                case "music": return _health.Command(Feature.Music, () => Music(cmd.Str("action", 16), cmd.Str("name", 120)));
                case "ambience":
                    return _health.Command(Feature.Ambience, () => Ambience(cmd.Str("action", 16), cmd.List("names", 12, 80)));
                default: return CommandResult.Fail("不认识的操作：" + cmd.Op);
            }
        }

        // Read-only probe for the room-ready self check.
        public void SelfCheck()
        {
            T Need<T>(T value, string what) where T : class => value ?? throw new InvalidOperationException(what + " not found");
            _health.Run(Feature.Notes, () =>
            {
                _ = Need(Singleton<Il2Cpp.MemoManager>.Instance, "MemoManager")._cacheDic.Count;
                _ = Need(Singleton<Il2Cpp.TodoListManager>.Instance, "TodoListManager")._cacheDic.Count;
            });
            _health.Run(Feature.Pomodoro, () => _ = Need(Timers(), "TimerManager").SessionTimerSystem.IsPlaying.Value);
            _health.Run(Feature.Music, () =>
            {
                _ = Need(Singleton<MusicPlayer>.Instance, "MusicPlayer").Musics.Count;
                Need(Singleton<DLCManager>.Instance, "DLCManager");
            });
            _health.Run(Feature.Ambience, () =>
            {
                _ = Need(Singleton<Il2Cpp.AmbiencePlayer>.Instance, "AmbiencePlayer").IsMute;
                _ = Player.Instance.SoundSystem.GetAllAmbienceCaches().Count;
            });
        }

        // ---------- memo / todo ----------

        private CommandResult AddMemo(Command cmd)
        {
            var title = cmd.Str("title", 40);
            var content = cmd.Str("content", 4000);
            if (string.IsNullOrEmpty(content)) return CommandResult.Fail("memo 内容是空的。");
            if (!Ledger.TryLoad(out var ledger, out var problem)) return CommandResult.Fail(problem);
            var added = ledger.Notes.Count(n => n.Kind != "tasks");
            if (added >= MaxNotes)
                return CommandResult.Fail($"Claude 已经加了 {added} 条 memo/todo（上限 {MaxNotes}），先让用户清掉一些。");
            var memos = Singleton<Il2Cpp.MemoManager>.Instance;
            var before = Keys(memos._cacheDic);
            // the "memos created" stat belongs to the player; a memo Claude adds doesn't count
            var stats = Player.Instance.UserData.playerUserData;
            var created = stats.createMemoCount;
            var ok = memos.AddNewMemo(1, false);
            stats.createMemoCount = created;
            if (!ok) return CommandResult.Fail("游戏拒绝新建 memo（可能已经到游戏的 memo 数量上限）。");
            var key = NewKey(before, Keys(memos._cacheDic));
            if (key == null) return CommandResult.Fail("新建了 memo 却找不到它，没有写入内容。");
            var cache = memos.GetCache(key.Value);
            var fullTitle = Marker + (string.IsNullOrEmpty(title) ? "memo" : title);
            cache.GetMemoUserData().SetTitle(fullTitle);
            cache.EditContent(content);
            if (memos._activatedMemos.ContainsKey(key.Value)) memos._activatedMemos[key.Value].SetData(cache);
            ledger.Notes.Add(new Ledger.Entry { Kind = "memo", Key = key.Value, Title = fullTitle, At = Now() });
            ledger.Save();
            UserContentStore.RequestSave();
            Toast(Lang.T($"Claude 加了一条 memo：{fullTitle}", $"Claude added a memo: {fullTitle}"));
            return CommandResult.Done($"已在房间里加了 memo「{fullTitle}」。", new { key = key.Value, title = fullTitle });
        }

        private CommandResult AddTodo(Command cmd)
        {
            var title = cmd.Str("title", 40);
            var items = cmd.List("items", 30, 300);
            if (items.Count == 0) return CommandResult.Fail("todo 没有条目。");
            if (!Ledger.TryLoad(out var ledger, out var problem)) return CommandResult.Fail(problem);
            var added = ledger.Notes.Count(n => n.Kind != "tasks");
            if (added >= MaxNotes)
                return CommandResult.Fail($"Claude 已经加了 {added} 条 memo/todo（上限 {MaxNotes}），先让用户清掉一些。");
            var todos = Singleton<Il2Cpp.TodoListManager>.Instance;
            var before = Keys(todos._cacheDic);
            if (!todos.AddNewTodoList(1, false))
                return CommandResult.Fail("游戏拒绝新建 todo 清单（可能已经到游戏的清单数量上限）。");
            var key = NewKey(before, Keys(todos._cacheDic));
            if (key == null) return CommandResult.Fail("新建了 todo 清单却找不到它，没有写入条目。");
            var cache = todos.GetCache(key.Value);
            var fullTitle = Marker + (string.IsNullOrEmpty(title) ? "todo" : title);
            cache.GetTodolistUserData().SetTitle(fullTitle);
            // a new list comes with one empty row; drop it rather than leave it above Claude's items (filling it in
            // doesn't redraw: the open window keeps showing the row empty)
            foreach (var blank in Items(cache.GetOrderedTodoItems()).Where(i => !i.IsFolder && string.IsNullOrEmpty(i.Content)).ToList())
                cache.RemoveTodoItem(blank);
            foreach (var text in items) cache.AddTodoItem(-1).EditContent(text);
            if (todos._activatedTodoList.ContainsKey(key.Value)) todos._activatedTodoList[key.Value].SetData(cache);
            ledger.Notes.Add(new Ledger.Entry { Kind = "todo", Key = key.Value, Title = fullTitle, At = Now() });
            ledger.Save();
            UserContentStore.RequestSave();
            Toast(Lang.T($"Claude 加了一个 todo 清单：{fullTitle}（{items.Count} 项）", $"Claude added a todo list: {fullTitle} ({items.Count} items)"));
            return CommandResult.Done($"已在房间里加了 todo 清单「{fullTitle}」，{items.Count} 项。", new { key = key.Value, title = fullTitle, items = items.Count });
        }

        // Deletes only notes that are both in the ledger and still carry the marker title Claude gave them. A note
        // the player retitled (or deleted, or whose key was reused) is dropped from the ledger and left alone.
        private CommandResult ClearNotes()
        {
            if (!Ledger.TryLoad(out var ledger, out var problem)) return CommandResult.Fail(problem);
            var memos = Singleton<Il2Cpp.MemoManager>.Instance;
            var todos = Singleton<Il2Cpp.TodoListManager>.Instance;
            int removed = 0, kept = 0;
            foreach (var e in ledger.Notes)
            {
                if (e.Kind == "memo")
                {
                    var c = memos._cacheDic.ContainsKey(e.Key) ? memos._cacheDic[e.Key] : null;
                    if (c == null || c.Title != e.Title) { kept += c == null ? 0 : 1; continue; }
                    memos.TryDeleteMemo(e.Key);
                    if (memos._cacheDic.ContainsKey(e.Key)) throw new InvalidOperationException("TryDeleteMemo left the memo in place");
                }
                else
                {
                    var c = todos._cacheDic.ContainsKey(e.Key) ? todos._cacheDic[e.Key] : null;
                    if (c == null || c.Title != e.Title) { kept += c == null ? 0 : 1; continue; }
                    todos.TryDeleteTodolist(e.Key);
                    if (todos._cacheDic.ContainsKey(e.Key)) throw new InvalidOperationException("TryDeleteTodolist left the list in place");
                }
                removed++;
            }
            ledger.Notes.Clear();
            ledger.Save();
            if (removed > 0) UserContentStore.RequestSave();
            if (removed > 0) Toast(Lang.T($"清掉了 Claude 加的 {removed} 条 memo/todo", $"Removed {removed} memo/todo list(s) Claude added"));
            var note = kept > 0 ? $"另有 {kept} 条的标题已经不是 Claude 起的（被改过，或那个位置已是别的笔记），算作用户的，没有删。" : "";
            return CommandResult.Done(removed == 0 && kept == 0 ? "没有 Claude 加的 memo/todo 需要清除。" : $"清掉了 Claude 加的 {removed} 条 memo/todo。{note}",
                new { removed, kept });
        }

        private static HashSet<int> Keys<TV>(Il2CppSystem.Collections.Generic.Dictionary<int, TV> dic)
        {
            var keys = new HashSet<int>();
            foreach (var kv in dic) keys.Add(kv.Key);
            return keys;
        }

        private static int? NewKey(HashSet<int> before, HashSet<int> after)
        {
            var added = after.Where(k => !before.Contains(k)).ToList();
            return added.Count == 1 ? added[0] : null;
        }

        // ---------- pomodoro ----------

        private Il2Cpp.TimerManager _timers;
        private Il2Cpp.TimerManager Timers() => _timers != null ? _timers : _timers = UnityEngine.Object.FindObjectOfType<Il2Cpp.TimerManager>();

        private CommandResult Timer(string action)
        {
            var tm = Timers();
            if (tm == null) return CommandResult.Fail("找不到游戏的计时器。");
            var st = tm.SessionTimerSystem;
            string Describe() => $"{StatusName(st.CurrentStatus.Value)} {RoomDriver.Clock(st.CurrentTimeSeconds.Value)}" +
                (st.IsPlaying.Value || st.CurrentStatus.Value == Il2Cpp.ETimerStatus.Ready ? "" : "（暂停）");
            switch (action)
            {
                case "start":
                    if (st.IsPlaying.Value) return CommandResult.Done("番茄钟已经在走了：" + Describe());
                    st.Start();
                    _room.TimerTakenOver();
                    Toast(Lang.T("Claude 开了番茄钟", "Claude started the pomodoro"));
                    return CommandResult.Done("番茄钟开始了：" + Describe());
                case "pause":
                    if (!st.IsPlaying.Value) return CommandResult.Done("番茄钟本来就没在走：" + Describe());
                    st.Pause();
                    _room.TimerTakenOver();
                    Toast(Lang.T("Claude 暂停了番茄钟", "Claude paused the pomodoro"));
                    return CommandResult.Done("番茄钟暂停了：" + Describe());
                case "status":
                    return CommandResult.Done("番茄钟：" + Describe());
                default:
                    return CommandResult.Fail("timer 的 action 只能是 start / pause / status。");
            }
        }

        private static string StatusName(Il2Cpp.ETimerStatus s) =>
            s == Il2Cpp.ETimerStatus.Session ? "专注" : s == Il2Cpp.ETimerStatus.Break ? "休息" : "待开始";

        // ---------- music ----------

        private static bool Owned(SoundCache c) =>
            c == null || !c.IsDLC || Singleton<DLCManager>.Instance.IsDLCSoundAccessible(c);

        private CommandResult Music(string action, string name)
        {
            var player = Singleton<MusicPlayer>.Instance;
            if (player == null) return CommandResult.Fail("找不到游戏的音乐播放器。");
            string Now() => player.CurrentMusic == null ? "（无）" : $"{player.CurrentMusic.MusicName}（{player.CurrentMusic.AlbumName}）";
            var muteNote = player.IsMute ? "注意：游戏里的音乐是静音的，Claude 不会替你取消静音。" : "";
            switch (action)
            {
                case "list":
                {
                    var owned = Items(player.Musics).Where(m => m != null && Owned(m.Cache)).ToList();
                    var albums = owned.GroupBy(m => m.AlbumName ?? "")
                        .ToDictionary(g => g.Key, g => g.Select(m => m.MusicName).ToArray());
                    return CommandResult.Done($"共 {owned.Count} 首可放的歌（只列你拥有的）。正在放：{Now()}{(player.IsPlaying ? "" : "，暂停中")}。{muteNote}",
                        new { playing = player.IsPlaying, current = player.CurrentMusic?.MusicName, albums });
                }
                case "next":
                case "previous":
                {
                    var before = Now();
                    if (action == "next") player.PlayNextTrack(); else player.PlayPreviousTrack();
                    Toast(Lang.T("Claude 换了一首歌", "Claude changed the song"));
                    return CommandResult.Done($"已切到{(action == "next" ? "下" : "上")}一首（按游戏自己的播放顺序）：{before} → {Now()}。{muteNote}");
                }
                case "pause":
                    if (!player.IsPlaying) return CommandResult.Done("音乐本来就是暂停的。");
                    player.PauseBgm();
                    Toast(Lang.T("Claude 暂停了音乐", "Claude paused the music"));
                    return CommandResult.Done("音乐已暂停：" + Now());
                case "resume":
                    if (player.IsPlaying) return CommandResult.Done("音乐已经在放：" + Now());
                    player.ResumeBgm();
                    Toast(Lang.T("Claude 继续播放音乐", "Claude resumed the music"));
                    return CommandResult.Done($"继续播放：{Now()}。{muteNote}");
                case "play":
                {
                    if (string.IsNullOrEmpty(name)) return CommandResult.Fail("play 需要歌名（name）；先用 action=list 看有哪些。");
                    var all = Items(player.Musics).Where(m => m != null).ToList();
                    var match = Match(all, m => m.MusicName, m => m.MusicKey, name);
                    if (match == null) return CommandResult.Fail($"没有叫「{name}」的歌；用 action=list 看可放的歌名。");
                    if (!Owned(match.Cache)) return CommandResult.Fail($"「{match.MusicName}」属于你没买的 DLC，不放。");
                    player.PlayBgm(match, false);
                    Toast(Lang.T($"Claude 换歌：{match.MusicName}", $"Claude is playing: {match.MusicName}"));
                    return CommandResult.Done($"开始播放「{match.MusicName}」（{match.AlbumName}）。{muteNote}", new { key = match.MusicKey });
                }
                default:
                    return CommandResult.Fail("music 的 action 只能是 list / play / next / previous / pause / resume。");
            }
        }

        // ---------- ambience ----------

        private CommandResult Ambience(string action, List<string> names)
        {
            var amb = Singleton<Il2Cpp.AmbiencePlayer>.Instance;
            var sounds = Player.Instance.SoundSystem;
            var all = Items(sounds.GetAllAmbienceCaches()).Where(c => c != null).ToList();
            string Playing() => string.Join("、", all.Where(c => c.IsPlaying).Select(c => c.SoundName)) is var s && s.Length > 0 ? s : "（无）";
            switch (action)
            {
                case "list":
                    return CommandResult.Done($"可用的环境音（只列你拥有的）。正在放：{Playing()}{(amb.IsMute ? "（静音中）" : "")}。",
                        new { muted = amb.IsMute, sounds = all.Where(c => Owned(c)).Select(c => new { name = c.SoundName, playing = c.IsPlaying }).ToArray() });
                case "stop":
                {
                    var targets = names.Count == 0 ? all.Where(c => c.IsPlaying).ToList() : Resolve(all, names, out _);
                    _room.AmbienceTakenOver();
                    foreach (var c in targets) if (c.IsPlaying) amb.StopAmbientSound(c);
                    Toast(Lang.T("Claude 关掉了环境音", "Claude stopped the ambience"));
                    return CommandResult.Done($"已停止：{string.Join("、", targets.Select(c => c.SoundName))}。现在在放：{Playing()}。");
                }
                case "set":
                case "add":
                {
                    if (names.Count == 0) return CommandResult.Fail($"{action} 需要环境音名字（names）；先用 action=list 看有哪些。");
                    var wanted = Resolve(all, names, out var unknown);
                    if (unknown.Count > 0) return CommandResult.Fail($"没有这些环境音：{string.Join("、", unknown)}；用 action=list 看可用的名字。");
                    var locked = wanted.Where(c => !Owned(c)).ToList();
                    if (locked.Count > 0) return CommandResult.Fail($"这些环境音属于你没买的 DLC，不放：{string.Join("、", locked.Select(c => c.SoundName))}。");
                    _room.AmbienceTakenOver();
                    var wasMuted = amb.IsMute;
                    // Unmute first: unmuting brings back whatever played at mute time (MODLOG gotcha 5), so the stops
                    // below must come after it. Audible ambience earns the game's playtime exp; the user asked for it.
                    if (wasMuted) amb.UnmuteAmbientSound();
                    // ...and it does so a few frames later, so a "set" keeps stopping the others for a moment
                    if (action == "set") { _ambienceWanted = new HashSet<int>(wanted.Select(c => c.SoundKey)); _ambienceUntil = UnityEngine.Time.unscaledTime + 3f; }
                    else _ambienceUntil = 0;
                    if (action == "set")
                        foreach (var c in all)
                            if (c.IsPlaying && !wanted.Contains(c)) amb.StopAmbientSound(c);
                    foreach (var c in wanted)
                        if (!c.IsPlaying) amb.PlayAmbientSound(c);
                    Toast(Lang.T($"Claude 换环境音：{string.Join("、", wanted.Select(c => c.SoundName))}", $"Claude changed the ambience: {string.Join(", ", wanted.Select(c => c.SoundName))}"));
                    var note = wasMuted ? "环境音原来是静音的，已取消静音（有声的环境音会让游戏按时长给经验）。" : "";
                    return CommandResult.Done($"现在在放：{Playing()}。{note}");
                }
                default:
                    return CommandResult.Fail("ambience 的 action 只能是 list / set / add / stop。");
            }
        }

        private HashSet<int> _ambienceWanted;
        private float _ambienceUntil;

        // Every frame while the room is ready: finishes a "set" whose unmute brought old sounds back late.
        public void Tick()
        {
            if (_ambienceUntil == 0) return;
            if (UnityEngine.Time.unscaledTime > _ambienceUntil) { _ambienceUntil = 0; return; }
            _health.Run(Feature.Ambience, () =>
            {
                var amb = Singleton<Il2Cpp.AmbiencePlayer>.Instance;
                foreach (var c in Player.Instance.SoundSystem.GetAllAmbienceCaches())
                {
                    if (c.IsPlaying && !_ambienceWanted.Contains(c.SoundKey)) amb.StopAmbientSound(c);
                    else if (!c.IsPlaying && _ambienceWanted.Contains(c.SoundKey)) amb.PlayAmbientSound(c);
                }
            });
        }

        private static List<SoundCache> Resolve(List<SoundCache> all, List<string> names, out List<string> unknown)
        {
            var found = new List<SoundCache>();
            unknown = new List<string>();
            foreach (var n in names)
            {
                var c = Match(all, x => x.SoundName, x => x.SoundKey, n);
                if (c == null) unknown.Add(n);
                else if (!found.Contains(c)) found.Add(c);
            }
            return found;
        }

        // Exact name (case and spaces ignored), then the sound key, then a unique substring match.
        private static T Match<T>(List<T> all, Func<T, string> name, Func<T, int> key, string query) where T : class
        {
            static string Norm(string s) => new string((s ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToLowerInvariant();
            var q = Norm(query);
            if (q.Length == 0) return null;
            var exact = all.FirstOrDefault(x => Norm(name(x)) == q);
            if (exact != null) return exact;
            if (int.TryParse(q, out var k)) { var byKey = all.FirstOrDefault(x => key(x) == k); if (byKey != null) return byKey; }
            var partial = all.Where(x => Norm(name(x)).Contains(q)).ToList();
            return partial.Count == 1 ? partial[0] : null;
        }

        // ---------- helpers ----------

        private static List<T> Items<T>(Il2CppSystem.Collections.Generic.List<T> list)
        {
            var items = new List<T>();
            if (list != null) foreach (var x in list) items.Add(x);
            return items;
        }

        private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static void Toast(string text)
        {
            try { Singleton<ToastManager>.Instance?.ShowToast(text); }
            catch (Exception e) { MelonLogger.Warning("could not show toast: " + e.Message); }
        }

        // claude-notes.json: the memos and todo lists Claude added, so they can be cleared without touching the player's.
        internal sealed class Ledger
        {
            public List<Entry> Notes { get; set; } = new();

            public sealed class Entry
            {
                // "memo", "todo", or "tasks" (a session's mirrored task list, kept by TaskSync)
                public string Kind { get; set; }
                public int Key { get; set; }
                public string Title { get; set; }
                public long At { get; set; }
                // tasks only: the session, task id -> todo item key, and whether the player took the list over
                public string Sid { get; set; }
                public Dictionary<string, int> Items { get; set; }
                public bool Suppressed { get; set; }
            }

            // An unreadable ledger stops the operation rather than forget which notes are Claude's.
            public static bool TryLoad(out Ledger ledger, out string problem)
            {
                ledger = new Ledger();
                problem = null;
                try
                {
                    if (File.Exists(Paths.ClaudeNotes))
                        ledger = JsonSerializer.Deserialize<Ledger>(File.ReadAllText(Paths.ClaudeNotes)) ?? new Ledger();
                    return true;
                }
                catch (Exception e)
                {
                    problem = $"读不了 {Paths.ClaudeNotes}（{e.Message}），为了不误删你的笔记，这次没有执行。";
                    return false;
                }
            }

            public void Save()
            {
                Directory.CreateDirectory(Paths.Root);
                Paths.WriteAtomic(Paths.ClaudeNotes, JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }));
            }
        }
    }
}
