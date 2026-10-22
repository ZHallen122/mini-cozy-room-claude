using System;
using System.Collections.Generic;
using Il2Cppcozyhouse;
using Il2CppTS;
using MelonLoader;

namespace CozyClaude
{
    internal enum Feature { Guard, Work, Waiting, Timer, Restore, Rain, Status, Subagents, Notes, Pomodoro, Music, Ambience, Tasks, Activity, Attention }

    // Per-feature status. A feature that throws (typically MissingMethodException / TypeLoadException after a
    // game update) is switched off, named in the log, in a one-time in-game toast and in the Claude Code status line.
    internal sealed class Health
    {
        public static readonly Dictionary<Feature, string> Names = new()
        {
            [Feature.Guard] = "联机保护",
            [Feature.Work] = "工作状态同步",
            [Feature.Waiting] = "等待确认提醒",
            [Feature.Timer] = "计时器联动",
            [Feature.Restore] = "会话结束复原",
            [Feature.Rain] = "报错雨声",
            [Feature.Status] = "房间状态反显",
            [Feature.Subagents] = "subagent 宠物",
            [Feature.Notes] = "Claude 加 memo/todo",
            [Feature.Pomodoro] = "Claude 开番茄钟",
            [Feature.Music] = "Claude 换歌",
            [Feature.Ambience] = "Claude 换环境音",
            [Feature.Tasks] = "任务清单同步",
            [Feature.Activity] = "头顶动态",
            [Feature.Attention] = "缩小时提醒",
        };

        private readonly Dictionary<Feature, string> _broken = new();
        private readonly Dictionary<Feature, string> _notes = new();
        private readonly Queue<string> _pendingToasts = new();

        public bool IsBroken(Feature f) => _broken.ContainsKey(f);

        // Runs a feature action; on failure the feature is disabled for the rest of the game session.
        public bool Run(Feature f, Action action)
        {
            if (_broken.ContainsKey(f)) return false;
            try { action(); return true; }
            catch (Exception e)
            {
                MarkBroken(f, e);
                return false;
            }
        }

        // Runs one of Claude's requested operations. A failure disables the feature like Run does, and the reply
        // says so, so the model can tell the user instead of retrying.
        public CommandResult Command(Feature f, Func<CommandResult> action)
        {
            if (_broken.TryGetValue(f, out var why))
                return CommandResult.Fail($"「{Names[f]}」与当前游戏版本不兼容，已停用（{why}）。");
            try { return action(); }
            catch (Exception e)
            {
                MarkBroken(f, e);
                return CommandResult.Fail($"「{Names[f]}」出错，判定为与当前游戏版本不兼容，已停用：{e.GetType().Name}: {e.Message}");
            }
        }

        public void MarkBroken(Feature f, Exception e)
        {
            var reason = $"{e.GetType().Name}: {e.Message}";
            _broken[f] = reason;
            MelonLogger.Error($"[{Names[f]}] incompatible, disabled: {e}");
            _pendingToasts.Enqueue($"CozyClaude：{Names[f]} 与当前游戏版本不兼容，已停用");
        }

        // Expected, non-fatal conditions, e.g. "no desk in this room". Cleared by passing null.
        public void Note(Feature f, string note)
        {
            if (note == null) _notes.Remove(f);
            else if (!_notes.TryGetValue(f, out var old) || old != note)
            {
                _notes[f] = note;
                MelonLogger.Msg($"[{Names[f]}] {note}");
            }
        }

        public Dictionary<string, string> Report()
        {
            var r = new Dictionary<string, string>();
            foreach (var f in Names.Keys)
                r[f.ToString()] = _broken.TryGetValue(f, out var b) ? "incompatible: " + b
                    : _notes.TryGetValue(f, out var n) ? "note: " + n : "ok";
            return r;
        }

        public void FlushToasts()
        {
            if (_pendingToasts.Count == 0) return;
            try
            {
                var toast = Singleton<ToastManager>.Instance;
                if (toast == null) return;
                toast.ShowToast(string.Join("\n", _pendingToasts));
                _pendingToasts.Clear();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("could not show toast: " + e.Message);
                _pendingToasts.Clear();
            }
        }
    }
}
