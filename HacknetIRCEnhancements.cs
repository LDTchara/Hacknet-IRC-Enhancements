using BepInEx.Hacknet;
using BepInEx;
using HarmonyLib;
using Hacknet;
using Hacknet.Daemons.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Reflection;
using System.Reflection.Emit;

namespace HacknetIRCEnhancements
{
    /// <summary>
    /// HacknetIRCEnhancements 入口
    /// SoftDependency 确保 KF 和 TAXCore 先加载，运行时检测它们各自的 GUID
    /// </summary>
    [BepInPlugin(ModGUID, ModName, ModVer)]
    [BepInDependency("com.LDTchara.KernelFix", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.TAXCore.Dsl", BepInDependency.DependencyFlags.SoftDependency)]
    public class HacknetIRCEnhancements : HacknetPlugin
    {
        public const string ModGUID = "com.LDTchara.IRCEnhancements";
        public const string ModName = "HacknetIRCEnhancements";
        public const string ModVer = "1.0.2";

        internal static bool IsTAXCoreLoaded =>
            HacknetChainloader.Instance?.Plugins?.ContainsKey("com.TAXCore.Dsl") == true;
        internal static bool IsKFLoaded =>
            HacknetChainloader.Instance?.Plugins?.ContainsKey("com.LDTchara.KernelFix") == true;

        public override bool Load()
        {
            if (IsKFLoaded)
                Log.LogInfo("KF detected — IRCEnhancements will skip timestamp fix");
            if (IsTAXCoreLoaded)
                Log.LogInfo("TAXCore detected — IRCEnhancements will skip Paging Patch");
            HarmonyInstance.PatchAll();
            Log.LogInfo("IRCEnhancements loaded");
            return true;
        }

        public override bool Unload()
        {
            IRCPagingPatches.Cleanup();
            HarmonyInstance.UnpatchSelf();
            return true;
        }
    }

    // ====================================================================
    // 时间戳修复
    // 原版 SAAddIRCMessage.Trigger 中，Delay<0 时 d -= TimeSpan.FromSeconds(Delay)
    // 负 Delay 反变成加未来时间。Prefix 拦截后用 d += 修正。
    // ====================================================================
    [HarmonyPatch]
    internal static class IRCDelayFixPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SAAddIRCMessage), "Trigger")]
        private static bool FixBackdate(SAAddIRCMessage __instance, object os_obj)
        {
            // KF 有同名修复和配置开关，让 KF 处理
            if (HacknetIRCEnhancements.IsKFLoaded) return true;
            // 非负 Delay 或接近 0 的 Delay 不需要修正
            if (__instance.Delay >= 0f || Math.Abs(__instance.Delay) < 0.001f)
                return true;

            var os = (OS)os_obj;
            var comp = Programs.getComputer(os, __instance.TargetComp);
            if (comp == null) return true;   // 找不到目标节点时放回原版
            var irc = comp.getDaemon(typeof(IRCDaemon)) as IRCDaemon;
            var sys = irc?.System ?? (comp.getDaemon(typeof(DLCHubServer)) as DLCHubServer)?.IRCSystem;
            if (sys == null) return true;    // 没有 IRC 守护进程时放回原版

            // 修正后：d += TimeSpan.FromSeconds(-60) → 99 秒前的正确时间戳
            var d = DateTime.Now + TimeSpan.FromSeconds(__instance.Delay);
            var ts = d.Hour.ToString("00") + ":" + d.Minute.ToString("00");
            sys.AddLog(__instance.Author, __instance.Message, ts);
            return false;   // 跳过原版，防止重复添加
        }
    }

    // ====================================================================
    // 翻页功能
    // 三个 Harmony 补丁 + 一个 Transpiler：
    //   OnDraw (Prefix)  — 处理滚轮和 Home/End 输入，管理 offset
    //   TrimLog (Prefix) — 每次 GetLogsFromFile 前裁剪日志文件
    //   AfterGetLogs     — 按 offset 截断日志列表
    //   AfterDraw        — 恢复 messagesAddedSinceLastView，离开 IRC 重置
    //   Transpiler       — 分界线 Y 超出可见区时不绘制
    // ====================================================================
    [HarmonyPatch]
    internal static class IRCPagingPatches
    {
        // ── 每个 IRCSystem 实例的滚动偏移、分界线备份、按键节流 ──
        private static readonly Dictionary<IRCSystem, int> _scrollOffsets = new();
        private static readonly Dictionary<IRCSystem, int> _savedAdded = new();
        private static readonly Dictionary<IRCSystem, DateTime> _lastKey = new();

        private static int GetOffset(IRCSystem irc) =>
            _scrollOffsets.TryGetValue(irc, out var o) ? o : 0;

        private static int LineHeight =>
            (int)(GuiData.ActiveFontConfig.tinyFontCharHeight + 4f);

        const int MaxLogEntries = 10000;

        // ── 日志裁剪 ──
        // 在 GetLogsFromFile 返回数据前，如果文件条目 > 10000 则裁剪最老的部分
        [HarmonyPrefix]
        [HarmonyPatch(typeof(IRCSystem), "GetLogsFromFile")]
        private static void TrimLog(IRCSystem __instance)
        {
            var file = __instance.ActiveLogFile;
            if (file == null || string.IsNullOrEmpty(file.data)) return;
            string raw = file.data;
            if (raw.StartsWith("#")) raw = raw.Substring(1);
            string[] entries = raw.Split(IRCSystem.EntryLineDelimiter, StringSplitOptions.RemoveEmptyEntries);
            if (entries.Length <= MaxLogEntries) return;
            var sb = new System.Text.StringBuilder();
            sb.Append("#");
            for (int i = entries.Length - MaxLogEntries; i < entries.Length; i++)
            {
                if (i > entries.Length - MaxLogEntries) sb.Append(IRCSystem.EntryLineDelimiter);
                sb.Append(entries[i]);
            }
            file.data = sb.ToString();
        }

        // ── 列表截断 ──
        // 按 offset 从列表头部切除，只留下 offset..Count 部分
        // offset = 0 → 显示最新消息；offset = max → 显示最老消息
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

        // ── 输入处理 ──
        // Prefix 在原始 Draw 之前运行，处理滚轮和 Home/End
        // maxOffset 直接从日志文件算（不经过 AfterGetLogs 截断），保证 Home 跳到绝对最老
        [HarmonyPrefix]
        [HarmonyPatch(typeof(IRCSystem), "Draw")]
        private static void OnDraw(IRCSystem __instance, Rectangle dest, SpriteBatch sb,
            Dictionary<string, Color> HighlightKeywords)
        {
            if (!__instance.isCurrentlyBeingViewed) return;
            if (HacknetIRCEnhancements.IsTAXCoreLoaded) return;

            // 从原始文件算总条目数，避免 AfterGetLogs 截断干扰 maxOffset 计算
            int totalEntries = 0;
            var file = __instance.ActiveLogFile;
            if (file != null && !string.IsNullOrEmpty(file.data))
            {
                string raw = file.data;
                if (raw.StartsWith("#")) raw = raw.Substring(1);
                totalEntries = raw.Split(IRCSystem.EntryLineDelimiter,
                    StringSplitOptions.RemoveEmptyEntries).Length;
            }
            if (totalEntries <= 0) return;

            int maxOffset = totalEntries - 1;
            int offset = GetOffset(__instance);
            bool changed = false;

            // 滚轮：GuiData.getMouseWheelScroll() 返回每帧的滚轮增量 / 120
            // 正值为滚上（= 显示更早的消息），负值为滚下
            float ws = GuiData.getMouseWheelScroll();
            if (ws != 0f) { offset -= (int)ws; changed = true; }

            // Home/End 直接跳转，不需要 150ms 节流（跳转本身无副作用）
            var kState = Microsoft.Xna.Framework.Input.Keyboard.GetState();
            if (kState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Home))
                { offset = maxOffset; changed = true; }
            else if (kState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.End))
                { offset = 0; changed = true; }

            if (changed)
            {
                offset = Math.Max(0, Math.Min(offset, maxOffset));
                _scrollOffsets[__instance] = offset;
            }

            // 翻页时暂时衰减 messagesAddedSinceLastView，AfterDraw 恢复
            if (offset > 0)
            {
                _savedAdded[__instance] = __instance.messagesAddedSinceLastView;
                __instance.messagesAddedSinceLastView = Math.Max(0,
                    __instance.messagesAddedSinceLastView - offset);
            }
        }

        // ── 分界线恢复 + 离开 IRC 重置 ──
        [HarmonyPostfix]
        [HarmonyPatch(typeof(IRCSystem), "Draw")]
        private static void AfterDraw(IRCSystem __instance)
        {
            // 恢复 messagesAddedSinceLastView（OnDraw 中临时减小了它）
            if (_savedAdded.TryGetValue(__instance, out var saved))
            {
                __instance.messagesAddedSinceLastView = saved;
                _savedAdded.Remove(__instance);
            }
            // 离开 IRC 时清除偏移，让下次进入时从最新消息开始
            if (!__instance.isCurrentlyBeingViewed)
                _scrollOffsets.Remove(__instance);
        }

        // ── Transpiler：分界线超出可见区时不绘制 ──
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(IRCSystem), "DrawLogEntry")]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs)
        {
            var codes = new List<CodeInstruction>(instrs);
            var drawMethod = typeof(SpriteBatch).GetMethod("Draw",
                new[] { typeof(Texture2D), typeof(Rectangle), typeof(Color) });

            for (int i = 0; i < codes.Count; i++)
            {
                if (i >= 1 && codes[i].opcode == OpCodes.Ldc_R4
                    && (float)codes[i].operand == 0.5f)
                {
                    int mulIdx = i + 1, drawIdx = i + 2;
                    if (mulIdx < codes.Count && drawIdx < codes.Count
                        && codes[mulIdx].opcode == OpCodes.Call
                        && codes[mulIdx].operand is MethodInfo mulMi
                        && mulMi.Name == "op_Multiply"
                        && codes[drawIdx].opcode == OpCodes.Callvirt
                        && codes[drawIdx].operand is MethodInfo drawMi
                        && drawMi == drawMethod)
                    {
                        // 在 Draw 调用前插入 ldarg.2（startingDest），改为调用 DrawSepIfVisible
                        codes.Insert(drawIdx, new CodeInstruction(OpCodes.Ldarg_2));
                        codes[drawIdx + 1] = new CodeInstruction(OpCodes.Call,
                            typeof(IRCPagingPatches).GetMethod("DrawSepIfVisible",
                                BindingFlags.NonPublic | BindingFlags.Static));
                        break;
                    }
                }
            }
            return codes;
        }

        // ── 仅在分界线 Y 在可见区内时绘制 ──
        private static void DrawSepIfVisible(SpriteBatch sb, Texture2D tex, Rectangle rect,
            Color color, Rectangle bounds)
        {
            if (rect.Y >= bounds.Y)
                sb.Draw(tex, rect, color);
        }

        // ── 卸载时清空静态缓存 ──
        public static void Cleanup()
        {
            _scrollOffsets.Clear();
            _savedAdded.Clear();
            _lastKey.Clear();
        }
    }
}
