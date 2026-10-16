using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using NeoModLoader.utils;
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

        public static void Start(ModConfig pConfig, string pFolder)
        {
            Settings.Config = pConfig;
            Perf.Perf.Start(pFolder);
            LoadIcons(pFolder);
            foreach (string t in new[] { "vd_cycle_renewal", "vd_cycle_prime", "vd_cycle_decline", "vd_cycle_collapse" })
            {
                Sprite s = AssetManager.kingdoms_traits.get(t)?.getSprite();
                Debug.Log($"[icontest] {t} {(s == null ? "null" : s.name + " " + s.rect)}");
            }
            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(Core).Assembly);
            AddOpinion();
            AddPlot();
            Feudal.Init();
            Government.Init();
            Policies.Init();
            Cycle.Init();
            Restoration.Init();
            PeaceTalks.Init();
            Appanage.Init();
            DirectRule.Init();
            Integration.Init();
            WorldLogs.Init();
            AllianceView.Init();
            Powers.Init();
            Orders.Init();
            Visuals.Init();
            BoxCommand.Init();
            ParliamentWindow.Init();
            BuildTab();
        }

        public static void Stop()
        {
            Perf.Perf.Stop();
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        // NML 只在游戏启动时加载 GameResources/ 下的图片；这里再注册一遍，热重载时改过的图标也能换上。
        // 原版 SpriteTextureLoader 按路径缓存，要把旧的清掉
        static void LoadIcons(string pFolder)
        {
            string root = Path.Combine(pFolder, "GameResources");
            if (!Directory.Exists(root)) return;
            foreach (string file in Directory.GetFiles(root, "*.png", SearchOption.AllDirectories))
            {
                Sprite[] sprites = SpriteLoadUtils.LoadSprites(file);
                if (sprites.Length == 0) continue;
                string path = file.Substring(root.Length + 1).Replace('\\', '/');
                path = path.Substring(0, path.Length - ".png".Length);
                ResourcesPatch.PatchResource(path, sprites[0]);
                SpriteTextureLoader._cached_sprites.Remove(path);
            }
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
            AddPower(tab, Powers.MakeVassal, "ui/Icons/vd_power_make_vassal");
            AddPower(tab, Powers.InciteIndependence, "ui/Icons/vd_power_incite_independence");
            AddPower(tab, Powers.GreatRebellion, "ui/Icons/iconFireSkull");
            AddPower(tab, Powers.Mandate, "ui/Icons/vd_power_mandate");
            AddPower(tab, Powers.ReleaseCity, "ui/Icons/vd_power_release_city");
            AddSeparator(tab);
            AddPower(tab, Powers.CommandArmy, "ui/Icons/vd_power_command_army");
            AddPower(tab, Powers.OrderKingdomAttack, "ui/Icons/vd_power_order_kingdom_attack");
            AddSeparator(tab);
            AddPower(tab, Powers.TransferCity, "ui/Icons/vd_power_transfer_city");
            AddPower(tab, Powers.Annex, "ui/Icons/vd_power_annex");

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
            Upsert(AssetManager.opinion_library, new OpinionAsset
            {
                id = "opinion_vd_cycle",
                translation_key = "opinion_vd_cycle",
                calc = (pMain, pTarget) => Vassals.GetOverlord(pMain) == pTarget ? Cycle.VassalOpinion(pTarget) : 0
            });
            Upsert(AssetManager.opinion_library, new OpinionAsset
            {
                id = "opinion_vd_weak_lord",
                translation_key = "opinion_vd_weak_lord",
                calc = (pMain, pTarget) => Vassals.GetOverlord(pMain) == pTarget ? Cycle.WeakLordOpinion(pTarget) : 0
            });
            Upsert(AssetManager.opinion_library, new OpinionAsset
            {
                id = "opinion_vd_autonomy",
                translation_key = "opinion_vd_autonomy",
                calc = (pMain, pTarget) => Vassals.GetOverlord(pMain) == pTarget
                                           && Policies.Value(pTarget, Policies.Vassals) == Policies.Autonomy ? Assimilation.AutonomyOpinion : 0
            });
            Upsert(AssetManager.opinion_library, new OpinionAsset
            {
                id = "opinion_vd_assimilation",
                translation_key = "opinion_vd_assimilation",
                calc = (pMain, pTarget) => Vassals.GetOverlord(pMain) == pTarget && Assimilation.Resents(pMain, pTarget) ? Assimilation.ResentOpinion : 0
            });
            Upsert(AssetManager.loyalty_library, new LoyaltyAsset
            {
                id = "loyalty_vd_cycle",
                translation_key = "loyalty_vd_cycle_prime",
                translation_key_negative = "loyalty_vd_cycle_decline",
                calc = Cycle.CityLoyalty
            });
            Upsert(AssetManager.loyalty_library, new LoyaltyAsset
            {
                id = "loyalty_vd_republic",
                translation_key = "loyalty_vd_republic",
                translation_key_negative = "loyalty_vd_republic",
                calc = Government.CityLoyalty
            });
            Upsert(AssetManager.loyalty_library, new LoyaltyAsset
            {
                id = "loyalty_vd_dissent",
                translation_key = "loyalty_vd_dissent_content",
                translation_key_negative = "loyalty_vd_dissent",
                calc = Parliament.CityLoyalty
            });
            // 原版的文化、语言、宗教：扣的部分按教化政策变
            SetLoyalty("culture", Assimilation.CultureLoyalty);
            SetLoyalty("language", Assimilation.LanguageLoyalty);
            SetLoyalty("religion", Assimilation.ReligionLoyalty);
            // 原版「新征服」：王国攻下任何一座城，全国城市十年内都加忠诚。战争胜负已经算进天命，去掉
            LoyaltyAsset conquest = AssetManager.loyalty_library.get("new_conquest");
            if (conquest != null) conquest.calc = _ => 0;
        }

        static void SetLoyalty(string pId, LoyaltyDelegateCalc pCalc)
        {
            LoyaltyAsset asset = AssetManager.loyalty_library.get(pId);
            if (asset != null) asset.calc = pCalc;
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
                    if (IsPlotRunning(plot_independence, k)) return false;
                    // 宗主处在王朝衰落或崩溃期，附庸看准它外强中干，不再看实力对比
                    CyclePhase phase = Cycle.Get(lord);
                    if (phase == CyclePhase.Decline || phase == CyclePhase.Collapse) return true;
                    // 宗主实力 < 附庸实力 × 比例 时，才认为宗主已经衰落；整合度越高，比例越低（行省约一半）
                    float ratio = Settings.RebelPowerRatio * (1f - Integration.Get(k) / 200f);
                    return Vassals.Power(lord) * 100 < Vassals.Power(k) * ratio;
                },
                try_to_start_advanced = (pActor, pAsset, pForced) =>
                {
                    Kingdom lord = Vassals.GetOverlord(pActor.kingdom);
                    if (lord == null) return false;
                    Plot plot = World.world.plots.newPlot(pActor, pAsset, pForced);
                    plot.target_kingdom = lord;
                    return true;
                },
                check_can_be_forced = pActor => Vassals.IsVassal(pActor.kingdom),
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
            Motives.Register(plot_independence, IndependenceMotive);
        }

        // 独立的动机：怨恨 × 性格。好感低于设置值才开始怨恨，每再低 50 点 +1，最多 2；整合度越低越容易记恨
        static float IndependenceMotive(Actor pActor)
        {
            Kingdom k = pActor.kingdom;
            Kingdom lord = Vassals.GetOverlord(k);
            if (lord == null) return 0f;
            int opinion = World.world.diplomacy.getOpinion(k, lord).total;
            float grudge = Mathf.Clamp((Settings.RebelOpinion - opinion) / 50f, 0f, 2f);
            if (grudge <= 0f) return 0f;
            float integration = Integration.Get(k);
            grudge *= 1.2f - integration / 100f;
            // 宗主亲自打仗时后方空虚，整合度越低越想趁机起兵：整合度 0 时 ×2，50 以上不变
            if (LordAtWar(lord)) grudge *= 1f + Mathf.Clamp01(1f - integration / 50f);
            grudge *= Policies.IndependenceFactor(k);
            return grudge * Motives.Personality(pActor, Motives.Stat(pActor, Motives.Aggression), true);
        }

        // 宗主是不是某场战争的主攻方或主守方；帮别人打的仗不算
        static bool LordAtWar(Kingdom pLord)
        {
            foreach (War w in pLord.getWars())
            {
                if (!w.hasEnded() && (w.isMainAttacker(pLord) || w.isMainDefender(pLord))) return true;
            }
            return false;
        }

        // 这个国家是否已经有人在进行这种密谋。原版的 isPlotTypeAlreadyRunning 查的是全世界
        public static bool IsPlotRunning(PlotAsset pAsset, Kingdom pKingdom)
        {
            foreach (Plot plot in World.world.plots)
            {
                if (plot.isActive() && plot.isSameType(pAsset) && plot.getAuthor()?.kingdom == pKingdom) return true;
            }
            return false;
        }

        // 开战失败时恢复附庸关系并返回 null
        public static War StartIndependenceWar(Kingdom pRebel, Kingdom pLord)
        {
            float integration = Integration.Get(pRebel);
            Vassals.Release(pRebel);
            War war = World.world.diplomacy.startWar(pRebel, pLord, WarTypeLibrary.normal);
            if (war == null)
            {
                Vassals.MakeVassal(pLord, pRebel, integration);
                return null;
            }
            war.data.set(Vassals.KeyWarLord, pLord.id);
            war.data.set(Vassals.KeyWarRebel, pRebel.id);
            LeaveLordWars(pRebel, pLord, war);
            return war;
        }

        // 退出随宗主参加的战争：起兵的附庸不再替宗主打仗，免得同时与宗主和宗主的敌人为敌。自己是主攻方或主守方的仗照打
        static void LeaveLordWars(Kingdom pRebel, Kingdom pLord, War pIndependence)
        {
            foreach (War w in new List<War>(pRebel.getWars()))
            {
                if (w == pIndependence || w.hasEnded() || w.isTotalWar()) continue;
                if (w.isMainAttacker(pRebel) || w.isMainDefender(pRebel)) continue;
                bool sameSide = (w.isAttacker(pRebel) && w.isAttacker(pLord))
                                || (w.isDefender(pRebel) && w.isDefender(pLord));
                if (sameSide) w.removeFromWar(pRebel, true);
            }
        }
    }
}
