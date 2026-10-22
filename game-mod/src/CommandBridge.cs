using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MelonLoader;

namespace CozyClaude
{
    // One request from a Claude Code tool call (v2 "active operations").
    internal sealed class Command
    {
        public string Id;
        public string Op;
        public JsonElement Args;

        public string Str(string name, int max)
        {
            if (Args.ValueKind != JsonValueKind.Object || !Args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String)
                return null;
            var s = v.GetString().Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        public List<string> List(string name, int maxItems, int maxLength)
        {
            var list = new List<string>();
            if (Args.ValueKind != JsonValueKind.Object || !Args.TryGetProperty(name, out var v)) return list;
            var items = v.ValueKind == JsonValueKind.String ? new List<JsonElement> { v }
                : v.ValueKind == JsonValueKind.Array ? new List<JsonElement>(v.EnumerateArray()) : new List<JsonElement>();
            foreach (var item in items)
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var s = item.GetString().Trim();
                if (s.Length == 0) continue;
                list.Add(s.Length > maxLength ? s.Substring(0, maxLength) : s);
                if (list.Count == maxItems) break;
            }
            return list;
        }
    }

    internal sealed class CommandResult
    {
        public bool Ok;
        public string Message;
        public object Data;

        public static CommandResult Done(string message, object data = null) => new() { Ok = true, Message = message, Data = data };
        public static CommandResult Fail(string message, object data = null) => new() { Ok = false, Message = message, Data = data };
    }

    // Reads %LOCALAPPDATA%\CozyClaude\commands\<id>.json on a background thread and writes results\<id>.json back.
    // The cozy-room plugin's tools write a command and wait a few seconds for its result. A command must carry the
    // secret.key token and be fresh: one older than MaxAgeMs was already given up on by its tool and is dropped, so
    // a command written while the game was closed never runs later by surprise.
    internal sealed class CommandBridge : IDisposable
    {
        private const long MaxAgeMs = 8_000;
        private const long ResultTtlMs = 120_000;
        private const int MaxFileBytes = 32 * 1024;

        private readonly Timer _timer;
        private readonly ConcurrentQueue<Command> _pending = new();
        private readonly HashSet<string> _seen = new();
        private int _busy;
        private long _nextCleanup;

        public string Problem { get; private set; }

        public CommandBridge()
        {
            _timer = new Timer(_ => Poll(), null, 0, 200);
        }

        public void Dispose() => _timer.Dispose();

        public bool TryTake(out Command cmd) => _pending.TryDequeue(out cmd);

        public void Reply(Command cmd, CommandResult r)
        {
            var json = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["v"] = 1, ["id"] = cmd.Id, ["ok"] = r.Ok, ["message"] = r.Message, ["data"] = r.Data,
                ["at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            MelonLogger.Msg($"[command] {cmd.Op} {cmd.Id} -> {(r.Ok ? "ok" : "fail")}: {r.Message}");
            Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(Paths.Results);
                    Paths.WriteAtomic(Path.Combine(Paths.Results, cmd.Id + ".json"), json);
                }
                catch (Exception e) { MelonLogger.Warning("could not write command result: " + e.Message); }
            });
        }

        private void Poll()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                if (!Directory.Exists(Paths.Commands)) return;
                var secret = File.Exists(Paths.Secret) ? File.ReadAllText(Paths.Secret).Trim() : null;
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (var file in Directory.GetFiles(Paths.Commands, "*.json"))
                {
                    var cmd = Read(file, secret, now);
                    if (cmd == null) continue;
                    if (_seen.Add(cmd.Id)) _pending.Enqueue(cmd);
                }
                if (now >= _nextCleanup)
                {
                    _nextCleanup = now + 30_000;
                    CleanResults(now);
                    if (_seen.Count > 1000) _seen.Clear();
                }
                Problem = null;
            }
            catch (Exception e) { Problem = e.GetType().Name + ": " + e.Message; }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        // Returns the command, or null when the file is not (yet) a valid, fresh, signed command.
        // Every file it decides on is deleted; a half-written one is left for the next poll.
        private static Command Read(string file, string secret, long now)
        {
            CommandFile c;
            try
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length > MaxFileBytes) { TryDelete(file); return null; }
                    c = JsonSerializer.Deserialize<CommandFile>(fs);
                }
            }
            catch (IOException) { return null; }
            catch (JsonException)
            {
                // a writer in the middle of its write, or junk; junk gets old and is removed
                if (now - new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeMilliseconds() > MaxAgeMs) TryDelete(file);
                return null;
            }
            TryDelete(file);
            if (c == null || c.cancelled) return null;
            if (secret == null || c.tok != secret)
            {
                MelonLogger.Warning($"ignored command file without the right token: {Path.GetFileName(file)}");
                return null;
            }
            if (now - c.at > MaxAgeMs || c.at - now > MaxAgeMs) return null;
            if (string.IsNullOrEmpty(c.id) || c.id.Length > 64 || !IsSafeId(c.id) || string.IsNullOrEmpty(c.op)) return null;
            return new Command { Id = c.id, Op = c.op, Args = c.args.ValueKind == JsonValueKind.Undefined ? default : c.args.Clone() };
        }

        private static bool IsSafeId(string id)
        {
            foreach (var ch in id)
                if (!((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_')) return false;
            return true;
        }

        private static void CleanResults(long now)
        {
            if (!Directory.Exists(Paths.Results)) return;
            foreach (var file in Directory.GetFiles(Paths.Results, "*.json"))
                if (now - new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeMilliseconds() > ResultTtlMs) TryDelete(file);
        }

        private static void TryDelete(string file)
        {
            try { File.Delete(file); }
            catch (Exception e) { MelonLogger.Warning($"could not delete {file}: {e.Message}"); }
        }

        private sealed class CommandFile
        {
            public int v { get; set; }
            public string tok { get; set; }
            public string id { get; set; }
            public string op { get; set; }
            public JsonElement args { get; set; }
            public long at { get; set; }
            public bool cancelled { get; set; }
        }
    }
}
