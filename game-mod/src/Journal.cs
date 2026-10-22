using System;
using System.IO;
using System.Text.Json;
using MelonLoader;

namespace CozyClaude
{
    // What the room looked like before Claude touched it. Kept on disk so a crash or a game restart in the
    // middle of a session still restores the user's room later. Deleted once everything is restored.
    internal sealed class Journal
    {
        public int? OriginalActivity { get; set; }
        public int? LastSetActivity { get; set; }
        // unix ms of the last restore; set until a game save newer than it exists
        public long? RestoredAt { get; set; }
        public RainBackup Rain { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsEmpty => OriginalActivity == null && Rain == null;

        public sealed class RainBackup
        {
            public bool WasMuted { get; set; }
            public int[] Playing { get; set; } = Array.Empty<int>();
        }

        public static Journal Load()
        {
            try
            {
                if (File.Exists(Paths.Journal))
                    return JsonSerializer.Deserialize<Journal>(File.ReadAllText(Paths.Journal)) ?? new Journal();
            }
            catch (Exception e) { MelonLogger.Warning("journal.json unreadable, ignoring: " + e.Message); }
            return new Journal();
        }

        public void Save()
        {
            try
            {
                if (IsEmpty) { if (File.Exists(Paths.Journal)) File.Delete(Paths.Journal); return; }
                Directory.CreateDirectory(Paths.Root);
                Paths.WriteAtomic(Paths.Journal, JsonSerializer.Serialize(this));
            }
            catch (Exception e) { MelonLogger.Warning("could not write journal.json: " + e.Message); }
        }
    }
}
