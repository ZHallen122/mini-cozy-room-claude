using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Il2Cppcozyhouse;
using Il2CppTS;
using Il2CppUtils;
using MelonLoader;

namespace CozyClaude
{
    // When Claude waits for approval and the room can't be seen (game minimized, or hidden in the tray), reach the
    // player another way: flash the taskbar button, put the reminder in the tray icon's tooltip, and optionally play
    // the player's own timer sound or bring the window back. Everything is undone when the waiting ends.
    internal sealed class Attention
    {
        private readonly Config _cfg;
        private readonly Health _health;
        private bool _active;
        private string _originalTooltip;
        private IntPtr _hwnd;

        public Attention(Config cfg, Health health)
        {
            _cfg = cfg;
            _health = health;
        }

        public void SelfCheck()
        {
            if (!_cfg.AttentionWhenHidden) return;
            _health.Run(Feature.Attention, () =>
            {
                _ = TrayIcon.IsInitialized();
                _ = TrayIcon.isHiddenFromTaskbar;
                if (Window() == IntPtr.Zero) _health.Note(Feature.Attention, "找不到游戏窗口，任务栏不会闪");
            });
        }

        public void Update(int waiting)
        {
            if (!_cfg.AttentionWhenHidden)
            {
                if (_active) Stop();
                return;
            }
            _health.Run(Feature.Attention, () =>
            {
                var hidden = Hidden();
                if (waiting > 0 && hidden && !_active) Start();
                else if (_active && (waiting == 0 || !hidden)) Stop();
            });
        }

        // Called on quit and whenever the room must not be touched.
        public void Stop()
        {
            if (!_active) return;
            _active = false;
            try
            {
                var hwnd = Window();
                if (hwnd != IntPtr.Zero) Flash(hwnd, FLASHW_STOP);
                if (_originalTooltip != null && TrayIcon.IsInitialized()) TrayIcon.UpdateTooltip(_originalTooltip);
            }
            catch (Exception e) { MelonLogger.Warning("could not clear the reminder: " + e.Message); }
        }

        private void Start()
        {
            _active = true;
            MelonLogger.Msg("game hidden while Claude waits: reminding outside the room");
            var hwnd = Window();
            if (hwnd != IntPtr.Zero && !TrayIcon.isHiddenFromTaskbar) Flash(hwnd, FLASHW_TRAY | FLASHW_TIMER);
            if (TrayIcon.IsInitialized())
            {
                _originalTooltip ??= CurrentTooltip();
                TrayIcon.UpdateTooltip(_cfg.Waiting());
            }
            if (_cfg.AttentionSound) PlayTimerSound();
            if (_cfg.AttentionRestoreWindow)
            {
                if (TrayIcon.isHiddenFromTaskbar) TrayIcon.RestoreFromTray();
                else if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SW_RESTORE);
            }
        }

        private bool Hidden()
        {
            if (TrayIcon.isHiddenFromTaskbar) return true;
            var hwnd = Window();
            return hwnd != IntPtr.Zero && (IsIconic(hwnd) || !IsWindowVisible(hwnd));
        }

        private static string CurrentTooltip()
        {
            try
            {
                var tip = TrayIcon.notifyIconData.szTip;
                if (!string.IsNullOrEmpty(tip)) return tip;
            }
            catch (Exception) { }
            return TrayIcon.appDisplayName ?? "Mini Cozy Room";
        }

        private static void PlayTimerSound()
        {
            try
            {
                var cache = Player.Instance.SoundSystem.GetCache(Player.Instance.TimerSoundKey);
                var clip = cache?.LoadAudioClip();
                if (clip != null) Singleton<AudioManager>.Instance.PlayOneShot(clip, 1f, ESoundType.Productivity);
            }
            catch (Exception e) { MelonLogger.Warning("could not play the reminder sound: " + e.Message); }
        }

        // The game's own window: the tray code caches it; otherwise the first visible Unity window of this process.
        private IntPtr Window()
        {
            if (_hwnd != IntPtr.Zero && IsWindow(_hwnd)) return _hwnd;
            try
            {
                if (TrayIcon.windowHandleCached && TrayIcon.unityWindowHandle != IntPtr.Zero) return _hwnd = TrayIcon.unityWindowHandle;
            }
            catch (Exception) { }
            var pid = (uint)Process.GetCurrentProcess().Id;
            var found = IntPtr.Zero;
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out var owner);
                if (owner != pid) return true;
                var cls = new StringBuilder(64);
                GetClassName(h, cls, cls.Capacity);
                if (cls.ToString() != "UnityWndClass") return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return _hwnd = found;
        }

        private static void Flash(IntPtr hwnd, uint flags)
        {
            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = hwnd, dwFlags = flags, uCount = 0, dwTimeout = 0,
            };
            FlashWindowEx(ref info);
        }

        private const uint FLASHW_STOP = 0, FLASHW_TRAY = 2, FLASHW_TIMER = 4;
        private const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO info);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    }
}
