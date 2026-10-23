namespace CozyClaude
{
    // The cozy-room plugin sends what Claude is doing as a language-neutral code ("test", "edit:README.md"); this turns
    // it into the bubble's line in the room's language. Anything that isn't a known code (an older plugin sent finished
    // Chinese lines) is shown as it came.
    internal static class Activity
    {
        public static string Text(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            var colon = code.IndexOf(':');
            var kind = colon < 0 ? code : code.Substring(0, colon);
            var arg = colon < 0 ? "" : code.Substring(colon + 1);
            switch (kind)
            {
                case "thinking": return Lang.T("思考中…", "Thinking…");
                case "test": return Lang.T("正在跑测试…", "Running tests…");
                case "commit": return Lang.T("正在提交代码…", "Committing…");
                case "sync": return Lang.T("正在同步代码…", "Syncing code…");
                case "install": return Lang.T("正在装依赖…", "Installing packages…");
                case "build": return Lang.T("正在编译…", "Building…");
                case "command": return Lang.T("正在跑命令…", "Running a command…");
                case "edit": return arg.Length > 0 ? Lang.T($"在改 {arg}", $"Editing {arg}") : Lang.T("在改代码…", "Editing code…");
                case "read": return arg.Length > 0 ? Lang.T($"在读 {arg}", $"Reading {arg}") : Lang.T("在读代码…", "Reading code…");
                case "search": return Lang.T("在找代码…", "Searching the code…");
                case "web": return Lang.T("在查资料…", "Looking things up…");
                case "agent": return Lang.T("派出了帮手…", "Sent out a helper…");
                case "plan": return Lang.T("在排计划…", "Planning…");
                case "room": return Lang.T("在布置房间…", "Arranging the room…");
                case "tool": return Lang.T($"在用 {arg}", $"Using {arg}");
                default: return code;
            }
        }
    }
}
