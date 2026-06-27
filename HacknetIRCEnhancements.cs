using BepInEx.Hacknet;
using BepInEx;
using HarmonyLib;
using Hacknet.Daemons.Helpers;
using Microsoft.Xna.Framework.Input;

namespace HacknetIRCEnhancements
{
    [BepInPlugin(ModGUID, ModName, ModVer)]
    public class HacknetIRCEnhancements : HacknetPlugin
    {
        public const string ModGUID = "com.LDTchara.IRCEnhancements";
        public const string ModName = "HacknetIRCEnhancements";
        public const string ModVer = "1.0.0";

        private static bool IsTAXCoreLoaded =>
            HacknetChainloader.Instance?.Plugins?.ContainsKey("com.TAXCore.MainMenuText") == true;

        public override bool Load()
        {
            if (IsTAXCoreLoaded)
            {
                Log.LogInfo("TAXCore detected — IRCEnhancements skipping");
                return true;
            }
            HarmonyInstance.PatchAll();
            Log.LogInfo("IRCEnhancements loaded");
            return true;
        }
    }

    [HarmonyPatch]
    internal static class IRCPagingPatches
    {
        private static readonly Dictionary<IRCSystem, int> _scrollOffsets = new();
        private static readonly Dictionary<IRCSystem, DateTime> _lastKeyTime = new();
        private static readonly Dictionary<IRCSystem, bool> _canWriteSnapshot = new();
        private const int KEY_REPEAT_DELAY_MS = 150;
        private static bool _keyHeld = false;

        private static int GetOffset(IRCSystem irc)
        {
            if (!_scrollOffsets.TryGetValue(irc, out var offset))
                _scrollOffsets[irc] = 0;
            return _scrollOffsets[irc];
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(IRCSystem), "Draw")]
        private static void OnDraw(IRCSystem __instance, bool CanWrite)
        {
            if (!__instance.isCurrentlyBeingViewed) return;

            var state = Keyboard.GetState();
            bool anyKey = state.IsKeyDown(Keys.Up) || state.IsKeyDown(Keys.Down) ||
                          state.IsKeyDown(Keys.PageUp) || state.IsKeyDown(Keys.PageDown) ||
                          state.IsKeyDown(Keys.Home) || state.IsKeyDown(Keys.End);

            if (!anyKey)
            {
                _keyHeld = false;
                return;
            }

            // Throttle repeat
            if (_keyHeld) return;
            var now = DateTime.UtcNow;
            if (_lastKeyTime.TryGetValue(__instance, out var last) &&
                (now - last).TotalMilliseconds < KEY_REPEAT_DELAY_MS)
                return;
            _keyHeld = true;
            _lastKeyTime[__instance] = now;

            var logs = __instance.GetLogsFromFile();
            if (logs == null || logs.Count == 0) return;

            int offset = GetOffset(__instance);
            int maxOffset = logs.Count - 1;
            if (maxOffset < 0) maxOffset = 0;

            if (state.IsKeyDown(Keys.Home)) offset = maxOffset;
            else if (state.IsKeyDown(Keys.End)) offset = 0;
            else if (state.IsKeyDown(Keys.PageUp)) offset += 15;
            else if (state.IsKeyDown(Keys.PageDown)) offset -= 15;
            else if (state.IsKeyDown(Keys.Up)) offset += 1;
            else if (state.IsKeyDown(Keys.Down)) offset -= 1;

            offset = Math.Max(0, Math.Min(offset, maxOffset));
            _scrollOffsets[__instance] = offset;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(IRCSystem), "GetLogsFromFile")]
        private static void AfterGetLogs(IRCSystem __instance, ref List<IRCSystem.IRCLogEntry> __result)
        {
            int offset = GetOffset(__instance);
            if (offset <= 0 || __result == null || __result.Count <= 1) return;

            int take = __result.Count - offset;
            if (take < 1) take = 1;
            __result = __result.GetRange(0, take);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(IRCSystem), "Draw")]
        private static void AfterDraw(IRCSystem __instance)
        {
            if (!__instance.isCurrentlyBeingViewed)
                _scrollOffsets.Remove(__instance);
        }
    }
}
