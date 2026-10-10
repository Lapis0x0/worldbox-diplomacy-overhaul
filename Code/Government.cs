using System.Collections.Generic;
using HarmonyLib;

namespace DiplomacyOverhaul
{
    // 政体：一个原版王国特质组，组内特质互斥；按城市数和附庸状态自动判定
    public static class Government
    {
        public const string GroupId = "vd_government";
        public const string CityState = "vd_gov_city_state";
        public const string Kingdom = "vd_gov_kingdom";
        public const string Empire = "vd_gov_empire";
        public const string Duchy = "vd_gov_duchy";

        // 最近一次自动设置的政体；与当前政体不一致说明玩家手动改过
        const string KeyAuto = "vd_gov_auto";
        const string KeyLocked = "vd_gov_locked";

        static readonly string[] Ids = { CityState, Kingdom, Empire, Duchy };
        static readonly HashSet<string> IdSet = new HashSet<string>(Ids);

        public static void Init()
        {
            AssetManager.kingdoms_traits_groups.add(new KingdomTraitGroupAsset
            {
                id = GroupId,
                name = "trait_group_vd_government",
                color = "#FFD27F"
            });

            AddTrait(CityState, "ui/Icons/iconCity");
            AddTrait(Kingdom, "ui/Icons/iconKingdom");
            AddTrait(Empire, "ui/Icons/iconCrown");
            AddTrait(Duchy, "ui/Icons/iconKings");

            // 特质库在模组加载前已经 link 过，互斥关系要自己补上
            var traits = new List<KingdomTrait>();
            foreach (string id in Ids) traits.Add(AssetManager.kingdoms_traits.get(id));
            foreach (KingdomTrait t in traits)
            {
                t.opposite_traits = new HashSet<KingdomTrait>();
                foreach (KingdomTrait other in traits)
                {
                    if (other == t) continue;
                    t.addOpposite(other.id);
                    t.opposite_traits.Add(other);
                }
            }
        }

        static void AddTrait(string pId, string pIcon)
        {
            AssetManager.kingdoms_traits.add(new KingdomTrait
            {
                id = pId,
                group_id = GroupId,
                path_icon = pIcon,
                spawn_random_trait_allowed = false,
                needs_to_be_explored = false,
                has_description_2 = false,
                rarity = Rarity.R0_Normal
            });
        }

        public static KingdomTrait Current(Kingdom pKingdom)
        {
            foreach (KingdomTrait t in pKingdom.getTraits())
            {
                if (IdSet.Contains(t.id)) return t;
            }
            return null;
        }

        static string Desired(Kingdom pKingdom)
        {
            if (Vassals.IsVassal(pKingdom)) return Duchy;
            int cities = pKingdom.countCities();
            if (cities <= 0) return null;
            if (cities == 1) return CityState;
            if (cities <= 5) return Kingdom;
            return Empire;
        }

        public static void Update(Kingdom pKingdom)
        {
            if (!pKingdom.isAlive() || !pKingdom.isCiv()) return;
            KingdomData data = pKingdom.data;
            string current = Current(pKingdom)?.id;
            data.get(KeyAuto, out string last, null);
            data.get(KeyLocked, out bool locked, false);

            if (locked)
            {
                // 玩家把政体删掉 = 交还给自动判定
                if (current != null) return;
                data.removeBool(KeyLocked);
            }
            else if (last != null && current != null && current != last)
            {
                // 玩家在特质编辑器里改过政体：锁定
                data.set(KeyLocked, true);
                return;
            }

            string desired = Desired(pKingdom);
            if (desired == null || desired == current) return;
            pKingdom.addTrait(desired, true);
            data.set(KeyAuto, desired);
        }

        public static void UpdateAll()
        {
            if (!Main.GovernmentAutoEnabled) return;
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms)) Update(k);
        }
    }

    // 和投降检查一样，每个游戏年判定一次
    [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.update))]
    static class Patch_GovernmentTick
    {
        static double _lastCheck = -1;

        static void Postfix()
        {
            double now = World.world.getCurWorldTime();
            // 读档或换世界后时间可能倒退
            if (_lastCheck >= 0 && now >= _lastCheck && now - _lastCheck < Capitulation.YearLength) return;
            _lastCheck = now;
            Government.UpdateAll();
        }
    }
}
