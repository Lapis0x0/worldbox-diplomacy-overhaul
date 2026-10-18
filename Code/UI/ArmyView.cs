using HarmonyLib;
using NeoModLoader.General;

namespace DiplomacyOverhaul
{
    // 军队提示和军队窗口：玩家下过令的军队多一行「命令」，如「驻扎 王城」「扎营」「进攻 敌城」
    static class ArmyOrderText
    {
        internal const string Key = "vd_army_order";

        // 没有玩家命令时返回 null
        internal static string Of(Army pArmy, out string pColor)
        {
            pColor = "#80FF8C";
            if (!Orders.IsCommanded(pArmy)) return null;
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

        static void Prefix(TooltipData pData) => Current = pData?.army;

        static void Postfix(Tooltip pTooltip)
        {
            if (Current != null) Patch_ArmyTooltipLine.Add(pTooltip);
            Current = null;
        }
    }

    [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.addLineText))]
    static class Patch_ArmyTooltipLine
    {
        static void Postfix(Tooltip __instance, string pID)
        {
            if (pID == "captain" && Patch_ArmyTooltip.Current != null) Add(__instance);
        }

        internal static void Add(Tooltip pTooltip)
        {
            Army army = Patch_ArmyTooltip.Current;
            Patch_ArmyTooltip.Current = null;
            string text = ArmyOrderText.Of(army, out string color);
            if (text != null) pTooltip.addLineText(ArmyOrderText.Key, text, color);
        }
    }

    [HarmonyPatch(typeof(ArmyWindow), nameof(ArmyWindow.showStatsRows))]
    static class Patch_ArmyWindowOrder
    {
        static void Postfix(ArmyWindow __instance)
        {
            string text = ArmyOrderText.Of(SelectedMetas.selected_army, out string color);
            if (text != null) __instance.showStatRow(ArmyOrderText.Key, text, color, MetaType.None, -1L, true, "iconArmy");
        }
    }
}
