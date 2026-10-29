using System;
using HarmonyLib;
using NeoModLoader.General;

namespace DiplomacyOverhaul
{
    // 军队提示和军队窗口：玩家下过令的军队多一行「命令」，如「驻扎 王城」「扎营」「进攻 敌城」；
    // 没有玩家命令的禁军编队在驻守都城、巡逻边境、换防时多一行「任务」
    static class ArmyOrderText
    {
        internal const string Key = "vd_army_order";
        const string DutyKey = "vd_army_duty";

        // 两样都没有时返回 null
        internal static string Of(Army pArmy, out string pColor, out string pKey)
        {
            pColor = "#80FF8C";
            pKey = Key;
            if (!Orders.IsCommanded(pArmy))
            {
                pColor = "#C8D2E6";
                pKey = DutyKey;
                return GuardAI.Duty(pArmy);
            }
            if (Orders.IsForced(pArmy))
            {
                pColor = "#FF7366";
                return LM.Get("vd_army_order_attack").Replace("$city$", Orders.AttackTarget(pArmy)?.name);
            }
            City garrison = Orders.GetGarrison(pArmy);
            if (garrison != null) return LM.Get("vd_army_order_garrison").Replace("$city$", garrison.name);
            return LM.Get("vd_army_order_hold");
        }
    }

    // 鼠标悬停的军队提示：「队长」一行后面接命令；没有队长就接在最后
    [HarmonyPatch(typeof(TooltipLibrary), "showArmy")]
    static class Patch_ArmyTooltip
    {
        internal static Army Current;

        [SafeBy("只读一个字段存起来")]
        static void Prefix(TooltipData pData)
        {
            Current = pData?.army;
        }

        static void Postfix(Tooltip pTooltip)
        {
            if (!Safety.On(Sys.Interface))
            {
                Current = null;
                return;
            }
            try
            {
                if (Current != null) Patch_ArmyTooltipLine.Add(pTooltip);
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "军队提示框", e); }
            Current = null;
        }
    }

    [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.addLineText))]
    static class Patch_ArmyTooltipLine
    {
        static void Postfix(Tooltip __instance, string pID)
        {
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                if (pID == "captain" && Patch_ArmyTooltip.Current != null) Add(__instance);
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "军队提示框命令行", e); }
        }

        internal static void Add(Tooltip pTooltip)
        {
            Army army = Patch_ArmyTooltip.Current;
            Patch_ArmyTooltip.Current = null;
            string text = ArmyOrderText.Of(army, out string color, out string key);
            if (text != null) pTooltip.addLineText(key, text, color);
        }
    }

    [HarmonyPatch(typeof(ArmyWindow), nameof(ArmyWindow.showStatsRows))]
    static class Patch_ArmyWindowOrder
    {
        static void Postfix(ArmyWindow __instance)
        {
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                string text = ArmyOrderText.Of(SelectedMetas.selected_army, out string color, out string key);
                if (text != null) __instance.showStatRow(key, text, color, MetaType.None, -1L, true, "iconArmy");
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "军队窗口命令行", e); }
        }
    }
}
