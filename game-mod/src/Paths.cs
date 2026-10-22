using System;
using System.IO;

namespace CozyClaude
{
    // Everything the Claude Code hooks and the mod share lives under %LOCALAPPDATA%\CozyClaude.
    // It is a per-user folder: web pages cannot write there and other Windows users cannot read it.
    internal static class Paths
    {
        public static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CozyClaude");

        public static readonly string Sessions = Path.Combine(Root, "sessions");
        public static readonly string Secret = Path.Combine(Root, "secret.key");
        public static readonly string Config = Path.Combine(Root, "config.json");
        public static readonly string Journal = Path.Combine(Root, "journal.json");
        public static readonly string Stats = Path.Combine(Root, "stats.json");
        public static readonly string RoomStatus = Path.Combine(Root, "room.json");
        public static readonly string Commands = Path.Combine(Root, "commands");
        public static readonly string Results = Path.Combine(Root, "results");
        public static readonly string ClaudeNotes = Path.Combine(Root, "claude-notes.json");

        public static void WriteAtomic(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, true);
        }
    }
}
