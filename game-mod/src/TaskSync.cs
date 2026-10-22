using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cppcozyhouse;
using Il2Cppcozyhouse.data;
using Il2CppTS;
using MelonLoader;
using UnityEngine;

namespace CozyClaude
{
    // Mirrors each live session's own task list (TaskCreate/TaskUpdate or TodoWrite) into a todo list in the room,
    // titled "[Claude] <project> 任务": new tasks appear, finished ones get ticked, deleted ones go. When the session
    // ends the list is removed again. The lists are recorded in claude-notes.json like Claude's other notes; a list
    // the player deleted or retitled is theirs from then on and is neither synced nor removed.
    internal sealed class TaskSync
    {
        private readonly Config _cfg;
        private readonly Health _health;
        private readonly Dictionary<string, string> _synced = new(); // sid -> signature of the tasks last written
        private float _next;

        public TaskSync(Config cfg, Health health)
        {
            _cfg = cfg;
            _health = health;
        }

        public void SelfCheck()
        {
            _health.Run(Feature.Tasks, () =>
            {
                var todos = Singleton<Il2Cpp.TodoListManager>.Instance ?? throw new InvalidOperationException("TodoListManager not found");
                _ = todos._cacheDic.Count;
            });
        }

        // Called from RoomDriver.Tick, only while the room may be touched.
        public void Tick(Snapshot s)
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            _health.Run(Feature.Tasks, () => Sync(s));
        }

        private void Sync(Snapshot s)
        {
            // An unreadable ledger means not knowing which lists are Claude's: do nothing rather than guess.
            if (!RoomActions.Ledger.TryLoad(out var ledger, out _)) return;
            var todos = Singleton<Il2Cpp.TodoListManager>.Instance;
            var wanted = _cfg.SyncTaskList
                ? s.Sessions.Where(x => x.Tasks.Count > 0).GroupBy(x => x.Sid).ToDictionary(g => g.Key, g => g.First())
                : new Dictionary<string, LiveSession>();
            var changed = false;

            // lists whose session ended (or emptied its tasks) go
            foreach (var e in ledger.Notes.Where(n => n.Kind == "tasks").ToList())
            {
                if (wanted.ContainsKey(e.Sid)) continue;
                if (!e.Suppressed) Delete(todos, e);
                ledger.Notes.Remove(e);
                _synced.Remove(e.Sid ?? "");
                changed = true;
            }

            foreach (var session in wanted.Values)
            {
                var entry = ledger.Notes.FirstOrDefault(n => n.Kind == "tasks" && n.Sid == session.Sid);
                if (entry != null && entry.Suppressed) continue;
                var signature = Signature(session);
                var cache = entry == null ? null : Cache(todos, entry.Key);
                if (entry != null && (cache == null || cache.Title != entry.Title))
                {
                    MelonLogger.Msg($"task list '{entry.Title}' was deleted or renamed by the player; no longer synced");
                    entry.Suppressed = true;
                    changed = true;
                    continue;
                }
                if (cache != null && _synced.TryGetValue(session.Sid, out var last) && last == signature) continue;
                var created = false;
                if (entry == null)
                {
                    entry = Create(todos, session, out cache);
                    if (entry == null) continue;
                    ledger.Notes.Add(entry);
                    created = true;
                }
                SyncItems(cache, entry, session.Tasks);
                if (todos._activatedTodoList.ContainsKey(entry.Key))
                {
                    var window = todos._activatedTodoList[entry.Key];
                    // SetData flushes every row's input field into the data first, which would write the old text
                    // back over a renamed task; an existing window is redrawn without that flush.
                    if (created) window.SetData(cache);
                    else window.RefreshList(true, true);
                    if (FixTexts(cache, entry, session.Tasks))
                    {
                        window.RefreshList(true, true);
                        MelonLogger.Warning("the todo window overwrote a task's text; set it again");
                    }
                }
                _synced[session.Sid] = signature;
                changed = true;
            }

            if (!changed) return;
            ledger.Save();
            UserContentStore.RequestSave();
        }

        private RoomActions.Ledger.Entry Create(Il2Cpp.TodoListManager todos, LiveSession session, out TodolistCache cache)
        {
            cache = null;
            var before = Keys(todos);
            if (!todos.AddNewTodoList(1, false))
            {
                _health.Note(Feature.Tasks, "游戏拒绝新建 todo 清单（可能到了清单数量上限）");
                return null;
            }
            var added = Keys(todos).Where(k => !before.Contains(k)).ToList();
            if (added.Count != 1) return null;
            cache = todos.GetCache(added[0]);
            var title = RoomActions.Marker + (string.IsNullOrEmpty(session.Project) ? "Claude 的任务" : $"{session.Project} 任务");
            cache.GetTodolistUserData().SetTitle(title);
            // a new list comes with one empty row
            foreach (var blank in Items(cache).Where(i => !i.IsFolder && string.IsNullOrEmpty(i.Content)).ToList())
                cache.RemoveTodoItem(blank);
            _health.Note(Feature.Tasks, null);
            MelonLogger.Msg($"task list created: {title}");
            return new RoomActions.Ledger.Entry
            {
                Kind = "tasks", Key = added[0], Title = title, Sid = session.Sid, Items = new Dictionary<string, int>(),
                At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
        }

        private static void SyncItems(TodolistCache cache, RoomActions.Ledger.Entry entry, List<SessionTask> tasks)
        {
            entry.Items ??= new Dictionary<string, int>();
            // Ticking an item may count toward the player's todo statistics or rewards; those are the player's.
            var stats = StatSnapshot.Take();
            try
            {
                foreach (var kv in entry.Items.ToList())
                {
                    if (tasks.Any(t => t.id == kv.Key)) continue;
                    var gone = cache.GetTodoItemByKey(kv.Value);
                    if (gone != null) cache.RemoveTodoItem(gone);
                    entry.Items.Remove(kv.Key);
                }
                var items = new List<(SessionTask task, TodoItemCache item)>();
                foreach (var task in tasks)
                {
                    TodoItemCache item = null;
                    if (entry.Items.TryGetValue(task.id, out var key)) item = cache.GetTodoItemByKey(key);
                    if (item == null)
                    {
                        item = cache.AddTodoItem(-1);
                        item.EditContent(task.subject);
                        entry.Items[task.id] = item.Key;
                    }
                    items.Add((task, item));
                }
                // Ticking makes an open window redraw its rows from their input fields, which writes the old text
                // back; so tick first and set the text afterwards.
                foreach (var (task, item) in items)
                {
                    if (task.done && !item.IsComplete) item.Complete();
                    else if (!task.done && item.IsComplete) item.Uncomplete();
                }
                foreach (var (task, item) in items)
                    if (item.Content != task.subject) item.GetUserData().Content = task.subject;
            }
            finally { stats.Restore(); }
        }

        // After the window redraws: put back any text the redraw overwrote. Returns true when something was fixed.
        private static bool FixTexts(TodolistCache cache, RoomActions.Ledger.Entry entry, List<SessionTask> tasks)
        {
            var fixedAny = false;
            foreach (var task in tasks)
            {
                if (!entry.Items.TryGetValue(task.id, out var key)) continue;
                var item = cache.GetTodoItemByKey(key);
                if (item == null || item.Content == task.subject) continue;
                item.GetUserData().Content = task.subject;
                fixedAny = true;
            }
            return fixedAny;
        }

        private static void Delete(Il2Cpp.TodoListManager todos, RoomActions.Ledger.Entry e)
        {
            var cache = Cache(todos, e.Key);
            // only a list still titled the way Claude titled it; anything else belongs to the player now
            if (cache == null || cache.Title != e.Title) return;
            todos.TryDeleteTodolist(e.Key);
            MelonLogger.Msg($"task list removed: {e.Title}");
        }

        private static string Signature(LiveSession s) =>
            string.Join("\n", s.Tasks.Select(t => $"{t.id}\t{t.subject}\t{t.done}"));

        private static TodolistCache Cache(Il2Cpp.TodoListManager todos, int key) =>
            todos._cacheDic.ContainsKey(key) ? todos._cacheDic[key] : null;

        private static HashSet<int> Keys(Il2Cpp.TodoListManager todos)
        {
            var keys = new HashSet<int>();
            foreach (var kv in todos._cacheDic) keys.Add(kv.Key);
            return keys;
        }

        private static List<TodoItemCache> Items(TodolistCache cache)
        {
            var items = new List<TodoItemCache>();
            foreach (var i in cache.GetOrderedTodoItems()) items.Add(i);
            return items;
        }
    }

    // Every int statistic of the player (todos completed, exp, points, ...), so an action the player didn't take can
    // be undone in their numbers.
    internal sealed class StatSnapshot
    {
        private readonly PlayerUserData _data;
        private readonly Dictionary<System.Reflection.PropertyInfo, int> _values = new();

        private StatSnapshot(PlayerUserData data) => _data = data;

        public static StatSnapshot Take()
        {
            var data = Player.Instance.UserData.playerUserData;
            var snap = new StatSnapshot(data);
            foreach (var p in data.GetType().GetProperties())
                if (p.PropertyType == typeof(int) && p.CanRead && p.CanWrite)
                    snap._values[p] = (int)p.GetValue(data);
            return snap;
        }

        public void Restore()
        {
            foreach (var kv in _values)
            {
                var now = (int)kv.Key.GetValue(_data);
                if (now == kv.Value) continue;
                kv.Key.SetValue(_data, kv.Value);
                MelonLogger.Msg($"kept player stat {kv.Key.Name} at {kv.Value} (would have been {now})");
            }
        }
    }
}
