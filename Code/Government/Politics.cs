using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 氏族的政见特质：保守、激进、集权、分权，保守和激进互斥，集权和分权互斥。
    // 只由玩家在氏族特质编辑器里加。带了哪个，家族在那条轴上就站在那一边，离中线至少 Floor；
    // 门第、族长这些只决定它站多深
    public static class Politics
    {
        public const string GroupId = "vd_politics";
        public const string Conservative = "vd_conservative", Radical = "vd_radical";
        public const string Central = "vd_central", Local = "vd_local";

        // 0.5 时三档政策里也会投自己这一边的那一档，不只是反对另一头
        const float Floor = 0.5f;

        public static void Init()
        {
            Core.Upsert(AssetManager.clan_trait_groups, new ClanTraitGroupAsset
            {
                id = GroupId,
                name = "trait_group_" + GroupId,
                color = "#FFD27F"
            });
            // 编辑器按 priority 从大到小排：保守、激进、集权、分权
            Add(Conservative, "ui/Icons/iconBooks", 4);
            Add(Radical, "ui/Icons/iconFire", 3);
            Add(Central, "ui/Icons/iconKingdom", 2);
            Add(Local, "ui/Icons/iconVillages", 1);
            // 特质库在模组加载前已经 link 过，互斥关系要自己补上
            Oppose(Conservative, Radical);
            Oppose(Central, Local);
        }

        static void Add(string pId, string pIcon, int pPriority)
        {
            ClanTrait trait = Core.Upsert(AssetManager.clan_traits, new ClanTrait
            {
                id = pId,
                group_id = GroupId,
                path_icon = pIcon,
                priority = pPriority,
                spawn_random_trait_allowed = false,
                needs_to_be_explored = false,
                has_description_2 = false,
                rarity = Rarity.R0_Normal
            });
            // 热重载时图标可能换了，清掉缓存的旧图
            trait.cached_sprite = null;
        }

        static void Oppose(string pA, string pB)
        {
            ClanTrait a = AssetManager.clan_traits.get(pA), b = AssetManager.clan_traits.get(pB);
            a.opposite_list = new List<string> { pB };
            a.opposite_traits = new HashSet<ClanTrait> { b };
            b.opposite_list = new List<string> { pA };
            b.opposite_traits = new HashSet<ClanTrait> { a };
        }

        // 保守在负、激进在正；分权在负、集权在正
        internal static void Apply(Clan pClan, ref float pX, ref float pY)
        {
            if (pClan.hasTrait(Conservative)) pX = Mathf.Min(pX, -Floor);
            else if (pClan.hasTrait(Radical)) pX = Mathf.Max(pX, Floor);
            if (pClan.hasTrait(Central)) pY = Mathf.Max(pY, Floor);
            else if (pClan.hasTrait(Local)) pY = Mathf.Min(pY, -Floor);
        }
    }
}
