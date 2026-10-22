using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using MelonLoader;

namespace CozyClaude
{
    // What the room should show, folded from every live Claude Code session.
    internal sealed class Snapshot
    {
        public static readonly Snapshot Empty = new();
        public int AliveSessions;
        public bool Working;
        public int Waiting;
        public bool Error;
        public int Subagents;
        public string Problem; // why sessions can't be read at all, if they can't
        // what the most recently active working session is doing, for the bubble
        public string Activity;
        // every live session, with its task list (empty when Claude made none)
        public List<LiveSession> Sessions = new();
    }

    internal sealed class LiveSession
    {
        public string Sid;
        public string Project;
        public List<SessionTask> Tasks = new();
    }

    internal sealed class SessionTask
    {
        public string id { get; set; }
        public string subject { get; set; }
        public bool done { get; set; }
    }

    // Reads %LOCALAPPDATA%\CozyClaude\sessions\*.json on a background thread, so file IO never touches the
    // game's main thread. The cozy-room Claude Code plugin rewrites its session's file on every hook event
    // and every 20 s; a file that stops changing belongs to a Claude Code that exited or crashed.
    internal sealed class SessionBridge : IDisposable
    {
        private const long HeartbeatTimeoutMs = 90_000;
        private const long WorkingStaleMs = 30 * 60_000;   // a turn with no event for 30 min is treated as stuck
        private const long ErrorStaleMs = 10 * 60_000;
        private const long DeleteAfterMs = 60 * 60_000;

        private readonly Timer _timer;
        private volatile Snapshot _latest = Snapshot.Empty;
        private int _busy;

        public Snapshot Latest => _latest;

        public SessionBridge()
        {
            _timer = new Timer(_ => Poll(), null, 0, 500);
        }

        public void Dispose() => _timer.Dispose();

        private void Poll()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try { _latest = Build(); }
            catch (Exception e) { _latest = new Snapshot { Problem = e.GetType().Name + ": " + e.Message }; }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        private static Snapshot Build()
        {
            if (!File.Exists(Paths.Secret) || !Directory.Exists(Paths.Sessions))
                return new Snapshot { Problem = "no Claude Code session with the cozy-room plugin has run yet" };
            var secret = File.ReadAllText(Paths.Secret).Trim();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var snap = new Snapshot();
            long activityAt = 0;
            foreach (var file in Directory.GetFiles(Paths.Sessions, "*.json"))
            {
                var s = Read(file);
                // Only files carrying this machine's secret count; anything else is ignored.
                if (s == null || s.tok != secret) continue;
                var silent = now - s.updatedAt;
                if (s.ended || silent > HeartbeatTimeoutMs)
                {
                    if (silent > DeleteAfterMs) TryDelete(file);
                    continue;
                }
                snap.AliveSessions++;
                var quiet = now - s.eventAt;
                if (s.working && quiet < WorkingStaleMs) snap.Working = true;
                if (s.waiting > 0) snap.Waiting++;
                if (s.error && now - s.errorAt < ErrorStaleMs) snap.Error = true;
                if (quiet < WorkingStaleMs) snap.Subagents += s.agents;
                if (s.working && quiet < WorkingStaleMs && !string.IsNullOrEmpty(s.activity) && s.eventAt > activityAt)
                {
                    snap.Activity = s.activity;
                    activityAt = s.eventAt;
                }
                if (!string.IsNullOrEmpty(s.sid))
                    snap.Sessions.Add(new LiveSession { Sid = s.sid, Project = s.project ?? "", Tasks = s.tasks ?? new List<SessionTask>() });
            }
            return snap;
        }

        private static SessionFile Read(string file)
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length > 64 * 1024) return null;
                return JsonSerializer.Deserialize<SessionFile>(fs);
            }
            catch (IOException) { return null; }      // being rewritten right now; next poll gets it
            catch (JsonException) { return null; }
        }

        private static void TryDelete(string file)
        {
            try { File.Delete(file); }
            catch (Exception e) { MelonLogger.Warning($"could not delete old session file {file}: {e.Message}"); }
        }

        private sealed class SessionFile
        {
            public int v { get; set; }
            public string tok { get; set; }
            public string sid { get; set; }
            public long updatedAt { get; set; }
            public long eventAt { get; set; }
            public bool working { get; set; }
            public int waiting { get; set; }
            public bool error { get; set; }
            public long errorAt { get; set; }
            public int agents { get; set; }
            public bool ended { get; set; }
            public string project { get; set; }
            public string activity { get; set; }
            public List<SessionTask> tasks { get; set; }
        }
    }
}
