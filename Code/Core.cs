using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using UnityEngine;
using UnityEngine.UI;

namespace DiplomacyOverhaul
{
    // 模组的全部功能都从这里启动和停止。热重载时旧版本 Stop，新编译的版本 Start，
    // 所以 Start 必须能在已经注册过资源、已经建过 Tab 的游戏里重复执行。
    public static class Core
    {
        const string HarmonyId = "shizhiyun.diplomacy_overhaul";
        const string TabId = "vd_tab";

        static Harmony _harmony;
        public static PlotAsset plot_independence;

        public static void Start(ModConfig pConfig)
        {
            Settings.Config = pConfig;
            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(Core).Assembly);
            AddOpinion();
            AddPlot();
            Government.Init();
            AllianceView.Init();
            Powers.Init();
            Visuals.Init();
            BuildTab();
        }

        public static void Stop()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        // 资源已存在时不替换对象（王国、进行中的密谋都引用着它），只把委托和简单字段更新成新代码的版本
        public static T Upsert<T>(AssetLibrary<T> pLibrary, T pAsset) where T : Asset
        {
            if (!pLibrary.dict.TryGetValue(pAsset.id, out T existing)) return pLibrary.add(pAsset);
            foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Type type = field.FieldType;
                if (typeof(Delegate).IsAssignableFrom(type) || type.IsPrimitive || type.IsEnum || type == typeof(string))
                    field.SetValue(existing, field.GetValue(pAsset));
            }
            return existing;
        }

        static PowersTab FindTab()
        {
            PowersTab tab = PowerButtonCreator.GetTab(TabId);
            if (tab != null) return tab;
            foreach (PowersTab t in Resources.FindObjectsOfTypeAll<PowersTab>())
            {
                if (t.name == TabId) return t;
            }
            return null;
        }

        static void BuildTab()
        {
            PowersTab tab = FindTab();
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
                    UnityEngine.Object.Destroy(button.gameObject);
                }
                for (int i = tab.transform.childCount - 1; i >= 0; i--)
                {
                    GameObject child = tab.transform.GetChild(i).gameObject;
                    if (child.name != SeparatorName) continue;
                    child.SetActive(false);
                    UnityEngine.Object.Destroy(child);
                }
            }

            // 设置 | 附庸 | 战争与和平 | 城镇管理
            PowerButtonCreator.AddButtonToTab(PowerButtonCreator.CreateSimpleButton(
                "vd_settings", () => NeoModLoader.ui.ModConfigureWindow.ShowWindow(Settings.Config),
                SpriteTextureLoader.getSprite("ui/Icons/iconOptions")), tab);
            AddSeparator(tab);
            AddPower(tab, Powers.MakeVassal, "ui/Icons/iconCrown");
            AddPower(tab, Powers.InciteIndependence, "ui/Icons/iconRebellion");
            AddPower(tab, Powers.GreatRebellion, "ui/Icons/iconFireSkull");
            AddPower(tab, Powers.ReleaseCity, "ui/Icons/iconVillages");
            AddSeparator(tab);
            AddPower(tab, Powers.CommandArmy, "ui/Icons/iconArrowAttackTarget");
            AddPower(tab, Powers.OrderKingdomAttack, "ui/Icons/iconArmyList");
            AddSeparator(tab);
            AddPower(tab, Powers.TransferCity, "ui/Icons/iconCityConquered");
            AddPower(tab, Powers.Annex, "ui/Icons/iconKingdomList");

            // PowersTab 只在激活的子节点个数变化时才重新排版；热重载换按钮后个数可能不变，强制它重排
            tab._children = -1;
        }

        const string SeparatorName = "_line_vd";

        // 原版分组用的竖线；PowersTab.sortButtons 会把名字以 _line 开头的子节点当作分隔线排版
        static void AddSeparator(PowersTab pTab)
        {
            Image prefab = ResourcesFinder.FindResource<GameObject>("_line").GetComponent<Image>();
            Image line = UnityEngine.Object.Instantiate(prefab, pTab.transform);
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

        static void AddOpinion()
        {
            Upsert(AssetManager.opinion_library, new OpinionAsset
            {
                id = "opinion_vd_overlord",
                translation_key = "opinion_vd_overlord",
                calc = (pMain, pTarget) => Vassals.GetOverlord(pMain) == pTarget ? Settings.OpinionBonus : 0
            });
        }

        static void AddPlot()
        {
            plot_independence = Upsert(AssetManager.plots_library, new PlotAsset
            {
                id = "vd_independence",
                is_basic_plot = true,
                path_icon = "plots/icons/plot_rebellion",
                group_id = "diplomacy",
                min_level = 3,
                money_cost = 20,
                can_be_done_by_king = true,
                check_target_kingdom = true,
                requires_diplomacy = true,
                check_is_possible = pActor =>
                {
                    Kingdom k = pActor.kingdom;
                    Kingdom lord = Vassals.GetOverlord(k);
                    if (lord == null) return false;
                    if (World.world.plots.isPlotTypeAlreadyRunning(pActor, plot_independence)) return false;
                    if (World.world.diplomacy.getOpinion(k, lord).total >= Settings.RebelOpinion) return false;
                    // 宗主实力 < 附庸实力 × 比例 时，才认为宗主已经衰落
                    return Vassals.Power(lord) * 100 < Vassals.Power(k) * Settings.RebelPowerRatio;
                },
                try_to_start_advanced = (pActor, pAsset, pForced) =>
                {
                    Kingdom lord = Vassals.GetOverlord(pActor.kingdom);
                    if (lord == null) return false;
                    Plot plot = World.world.plots.newPlot(pActor, pAsset, pForced);
                    plot.target_kingdom = lord;
                    return true;
                },
                check_should_continue = pActor =>
                {
                    Plot plot = pActor.plot;
                    return plot.target_kingdom != null && plot.target_kingdom.isAlive()
                           && Vassals.GetOverlord(pActor.kingdom) == plot.target_kingdom;
                },
                action = pActor =>
                {
                    Kingdom rebel = pActor.kingdom;
                    Kingdom lord = Vassals.GetOverlord(rebel);
                    if (lord == null) return false;
                    StartIndependenceWar(rebel, lord);
                    return true;
                }
            });
            if (!AssetManager.plots_library.basic_plots.Contains(plot_independence))
                AssetManager.plots_library.basic_plots.Add(plot_independence);
        }

        // 开战失败时恢复附庸关系并返回 null
        public static War StartIndependenceWar(Kingdom pRebel, Kingdom pLord)
        {
            Vassals.Release(pRebel);
            War war = World.world.diplomacy.startWar(pRebel, pLord, WarTypeLibrary.normal);
            if (war == null)
            {
                Vassals.MakeVassal(pLord, pRebel);
                return null;
            }
            war.data.set(Vassals.KeyWarLord, pLord.id);
            war.data.set(Vassals.KeyWarRebel, pRebel.id);
            return war;
        }
    }
}
