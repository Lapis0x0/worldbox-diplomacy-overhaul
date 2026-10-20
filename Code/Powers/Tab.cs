using System.Collections.Generic;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using NeoModLoader.utils;
using UnityEngine;
using UnityEngine.UI;

namespace DiplomacyOverhaul
{
    // 神力栏里模组的 Tab
    public static class ModTab
    {
        const string TabId = "vd_tab";
        const string SeparatorName = "_line_vd";

        static PowersTab Find()
        {
            PowersTab tab = PowerButtonCreator.GetTab(TabId);
            if (tab != null) return tab;
            foreach (PowersTab t in Resources.FindObjectsOfTypeAll<PowersTab>())
            {
                if (t.name == TabId) return t;
            }
            return null;
        }

        public static void Build()
        {
            PowersTab tab = Find();
            if (tab == null)
            {
                tab = TabManager.CreateTab(TabId, "vd_tab", "vd_tab_description",
                    SpriteTextureLoader.getSprite("ui/Icons/iconKingdom"));
                tab.parentObj = tab.transform.parent.parent.gameObject;
            }
            else
            {
                // 热重载：只清掉我们自己的按钮（id 都以 vd_ 开头，改名删掉的旧按钮也算）和分隔线再重建，NML 从原版复制来的返回按钮等要保留
                foreach (PowerButton button in new List<PowerButton>(tab._power_buttons))
                {
                    if (button == null || !button.name.StartsWith("vd_")) continue;
                    tab._power_buttons.Remove(button);
                    button.gameObject.SetActive(false);
                    Object.Destroy(button.gameObject);
                }
                for (int i = tab.transform.childCount - 1; i >= 0; i--)
                {
                    GameObject child = tab.transform.GetChild(i).gameObject;
                    if (child.name != SeparatorName) continue;
                    child.SetActive(false);
                    Object.Destroy(child);
                }
            }

            // 设置 | 附庸 | 战争与和平 | 城镇管理
            PowerButtonCreator.AddButtonToTab(PowerButtonCreator.CreateSimpleButton(
                "vd_settings", SettingsWindow.Open,
                SpriteTextureLoader.getSprite("ui/Icons/iconOptions")), tab);
            AddSeparator(tab);
            AddPower(tab, Powers.MakeVassal, "ui/Icons/vd_power_make_vassal");
            AddPower(tab, Powers.InciteIndependence, "ui/Icons/vd_power_incite_independence");
            AddPower(tab, Powers.GreatRebellion, "ui/Icons/iconFireSkull");
            AddPower(tab, Powers.Mandate, "ui/Icons/vd_power_mandate");
            AddPower(tab, Powers.ReleaseCity, "ui/Icons/vd_power_release_city");
            AddSeparator(tab);
            AddPower(tab, Powers.CommandArmy, "ui/Icons/vd_power_command_army");
            AddPower(tab, Powers.OrderKingdomAttack, "ui/Icons/vd_power_order_kingdom_attack");
            AddPower(tab, Powers.ReleaseArmy, "ui/Icons/vd_power_release_army");
            AddSeparator(tab);
            AddPower(tab, Powers.TransferCity, "ui/Icons/vd_power_transfer_city");
            AddPower(tab, Powers.Annex, "ui/Icons/vd_power_annex");

            // PowersTab 只在激活的子节点个数变化时才重新排版；热重载换按钮后个数可能不变，强制它重排
            tab._children = -1;
        }

        // 原版分组用的竖线；PowersTab.sortButtons 会把名字以 _line 开头的子节点当作分隔线排版
        static void AddSeparator(PowersTab pTab)
        {
            Image prefab = ResourcesFinder.FindResource<GameObject>("_line").GetComponent<Image>();
            Image line = Object.Instantiate(prefab, pTab.transform);
            line.name = SeparatorName;
            line.enabled = true;
            line.gameObject.SetActive(true);
            line.transform.localScale = new Vector3(1f, 48.3f, 1f);
        }

        static void AddPower(PowersTab pTab, string pPowerId, string pIcon)
        {
            PowerButtonCreator.AddButtonToTab(
                PowerButtonCreator.CreateGodPowerButton(pPowerId, SpriteTextureLoader.getSprite(pIcon)), pTab);
        }
    }
}
