using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using UnityEngine;

namespace DiplomacyOverhaul
{
    public class Main : BasicMod<Main>
    {
        public static PowersTab tab;
        public static PlotAsset plot_independence;

        public static int OpinionBonus => Cfg("vd_opinion_bonus", 30);
        public static int RebelOpinion => Cfg("vd_rebel_opinion", 0);
        public static int RebelPowerRatio => Cfg("vd_rebel_power_ratio", 120);
        public static bool CapitulationEnabled
        {
            get
            {
                try { return Instance.GetConfig()["vd_group_capitulation"]["vd_capitulation_enabled"].BoolVal; }
                catch { return true; }
            }
        }
        public static bool GovernmentAutoEnabled
        {
            get
            {
                try { return Instance.GetConfig()["vd_group_government"]["vd_gov_auto_enabled"].BoolVal; }
                catch { return true; }
            }
        }
        public static int CapitulationMinYears => Cfg("vd_capitulation_min_years", 5, "vd_group_capitulation");
        public static int CapitulationCityLoss => Cfg("vd_capitulation_city_loss", 50, "vd_group_capitulation");
        public static int CapitulationPowerRatio => Cfg("vd_capitulation_power_ratio", 200, "vd_group_capitulation");

        static int Cfg(string pId, int pDefault, string pGroup = "vd_group_vassal")
        {
            try { return Instance.GetConfig()[pGroup][pId].IntVal; }
            catch { return pDefault; }
        }

        protected override void OnModLoad()
        {
            Harmony.CreateAndPatchAll(typeof(Main).Assembly, "shizhiyun.diplomacy_overhaul");
            AddOpinion();
            AddPlot();
            Government.Init();
            AllianceView.Init();
            Powers.Init();
            CreateTab();
            LogInfo("外交大修 loaded");
        }

        static void CreateTab()
        {
            tab = TabManager.CreateTab("vd_tab", "vd_tab", "vd_tab_description",
                SpriteTextureLoader.getSprite("ui/Icons/iconKingdom"));
            tab.parentObj = tab.transform.parent.parent.gameObject;

            PowerButtonCreator.AddButtonToTab(PowerButtonCreator.CreateGodPowerButton(
                Powers.MakeVassal, SpriteTextureLoader.getSprite("ui/Icons/iconCrown")), tab);
            PowerButtonCreator.AddButtonToTab(PowerButtonCreator.CreateGodPowerButton(
                Powers.ReleaseVassal, SpriteTextureLoader.getSprite("ui/Icons/iconRebellion")), tab);
            PowerButtonCreator.AddButtonToTab(PowerButtonCreator.CreateSimpleButton(
                "vd_settings", () => NeoModLoader.ui.ModConfigureWindow.ShowWindow(Instance.GetConfig()),
                SpriteTextureLoader.getSprite("ui/Icons/iconOptions")), tab);
        }

        static void AddOpinion()
        {
            AssetManager.opinion_library.add(new OpinionAsset
            {
                id = "opinion_vd_overlord",
                translation_key = "opinion_vd_overlord",
                calc = (pMain, pTarget) => Vassals.GetOverlord(pMain) == pTarget ? OpinionBonus : 0
            });
        }

        static void AddPlot()
        {
            plot_independence = new PlotAsset
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
                    if (World.world.diplomacy.getOpinion(k, lord).total >= RebelOpinion) return false;
                    // 宗主实力 < 附庸实力 × 比例 时，才认为宗主已经衰落
                    return Vassals.Power(lord) * 100 < Vassals.Power(k) * RebelPowerRatio;
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
            };
            AssetManager.plots_library.add(plot_independence);
            AssetManager.plots_library.basic_plots.Add(plot_independence);
        }

        public static void StartIndependenceWar(Kingdom pRebel, Kingdom pLord)
        {
            Vassals.Release(pRebel);
            War war = World.world.diplomacy.startWar(pRebel, pLord, WarTypeLibrary.normal);
            if (war == null) return;
            war.data.set(Vassals.KeyWarLord, pLord.id);
            war.data.set(Vassals.KeyWarRebel, pRebel.id);
        }
    }
}
