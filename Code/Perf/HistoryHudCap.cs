using System;
using HarmonyLib;

namespace DiplomacyOverhaul.Perf
{
    // 左上角事件提示：超出 10 行的条目要往上滑出才回收，而每来一条新消息，原版都对它们重新 moveToAndDestroy，
    // 滑动计时清零、淡出倒计时重设，消息密集时这些条目走不掉，积压到几十上百条，每帧都在动、都要重新合批。
    // 正在滑出的最多留 3 条，更早的直接用原版的 makeInactive 回收。每条消息照样弹出，也照样记进世界历史
    [HarmonyPatch(typeof(HistoryHud), "newText")]
    static class HistoryHudCap
    {
        const int Visible = 10;
        const int Leaving = 3;

        static void Prefix(HistoryHud __instance)
        {
            if (!Opt.history_hud_cap) return;
            try
            {
                var items = __instance._history_items;
                while (items.Count >= Visible + Leaving) __instance.makeInactive(items[0]);
            }
            catch (Exception e) { Perf.Fail(nameof(Opt.history_hud_cap), e); }
        }
    }
}
