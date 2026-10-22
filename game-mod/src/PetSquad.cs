using System.Collections.Generic;
using Il2Cppcozyhouse;
using Il2CppTS;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CozyClaude
{
    // One extra pet per running subagent, picked from pets the player has already unlocked. Each extra is a bare
    // sprite object that plays that pet's idle frames: no game component, nothing saved. The mod holds its own
    // reference on the pet's sprite group in the game's PetSpriteLoader and gives it back when the extra leaves.
    internal sealed class PetSquad
    {
        private static readonly Vector3[] Offsets =
        {
            // to the right of the real pet, so none hides behind the character
            new(1.2f, 0f, 0f), new(2.4f, 0f, 0f), new(1.8f, -0.7f, 0f), new(3.0f, -0.7f, 0f),
        };
        private const float FrameSeconds = 0.18f;

        private sealed class Extra
        {
            public GameObject Go;
            public SpriteRenderer Renderer;
            public int PetKey;
            public List<Sprite> Frames;
            public int Frame;
            public float NextFrame;
        }

        // dev console override: every extra shows this pet
        internal static int ForceKey;

        private readonly List<Extra> _extras = new();
        private int _roomKey = -1;

        public void Sync(int wanted)
        {
            var world = Singleton<World>.Instance;
            var source = world.petScript;
            var roomKey = world.CurrentRoom != null && world.CurrentRoom.Cache != null ? world.CurrentRoom.Cache.Key : -1;
            if (roomKey != _roomKey) { Clear(); _roomKey = roomKey; }
            if (source == null || !source.gameObject.activeInHierarchy) wanted = 0;
            wanted = Mathf.Min(wanted, Offsets.Length);
            while (_extras.Count > wanted) Remove(_extras[^1]);
            if (_extras.Count < wanted)
            {
                var keys = ForceKey != 0 ? new List<int> { ForceKey } : UnlockedPetKeys(source.currentPetKey);
                while (_extras.Count < wanted) _extras.Add(Create(source, keys[_extras.Count % keys.Count]));
            }
            var now = Time.unscaledTime;
            for (var i = 0; i < _extras.Count; i++)
            {
                var x = _extras[i];
                // follow the real pet, which moves whenever the character changes furniture
                x.Go.transform.position = source.transform.position + Offsets[i];
                if (x.Frames == null) x.Frames = IdleFrames(x.PetKey);
                if (x.Frames == null || x.Frames.Count == 0 || now < x.NextFrame) continue;
                x.NextFrame = now + FrameSeconds;
                x.Frame = (x.Frame + 1) % x.Frames.Count;
                x.Renderer.sprite = x.Frames[x.Frame];
            }
        }

        public void Clear()
        {
            while (_extras.Count > 0) Remove(_extras[^1]);
        }

        private static Extra Create(Il2Cppcozyhouse.world.Pet source, int petKey)
        {
            // the interop default for the token is null, which IL2CPP rejects; pass None explicitly
            Singleton<PetSpriteLoader>.Instance.LoadPetGroupAsync(petKey, Il2CppSystem.Threading.CancellationToken.None);
            var go = new GameObject("CozyClaude_SubagentPet");
            go.transform.SetParent(source.transform.parent, false);
            go.transform.localScale = source.transform.localScale;
            go.layer = source.gameObject.layer; // the room camera only draws the layers it culls for
            var sr = go.AddComponent<SpriteRenderer>();
            var original = source.petSpriteRenderer;
            sr.sortingLayerID = original.sortingLayerID;
            sr.sortingOrder = original.sortingOrder;
            sr.sharedMaterial = original.sharedMaterial;
            sr.flipX = original.flipX;
            return new Extra { Go = go, Renderer = sr, PetKey = petKey };
        }

        private void Remove(Extra x)
        {
            _extras.Remove(x);
            if (x.Go != null) Object.Destroy(x.Go);
            var loader = Singleton<PetSpriteLoader>.Instance;
            if (loader != null) loader.ReleasePetGroup(x.PetKey);
        }

        private static List<Sprite> IdleFrames(int petKey)
        {
            var actions = Singleton<PetSpriteLoader>.Instance.GetActions(petKey);
            if (actions == null || actions.Count == 0) return null; // still loading
            Il2CppSystem.Collections.Generic.List<Sprite> pick = null;
            if (actions.ContainsKey("Idle")) pick = actions["Idle"];
            else foreach (var kv in actions) { pick = kv.Value; break; }
            if (pick == null) return null;
            var frames = new List<Sprite>();
            foreach (var s in pick) if (s != null) frames.Add(s);
            return frames;
        }

        private static List<int> UnlockedPetKeys(int exclude)
        {
            // Each pet category has a "no pet" entry (10000, 20000, 30000; icon Pet_Empty) that draws nothing.
            var pets = GameInfo.Instance.Pets;
            var keys = new List<int>();
            foreach (var k in Player.Instance.UserData.playerUserData.unlockedPetKeyList)
                if (k != exclude && !keys.Contains(k) && pets.ContainsKey(k) && Player.Instance.CheckPetUnlocked(k)
                    && !(pets[k].IconPath ?? "").EndsWith("Pet_Empty"))
                    keys.Add(k);
            // no other unlocked pet: the extras look like the room's own pet
            if (keys.Count == 0) keys.Add(exclude);
            return keys;
        }
    }
}
