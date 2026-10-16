using HarmonyLib;

namespace DiplomacyOverhaul
{
    // 氏族传承：原版只有父母一方在任（国王、城主）时孩子才进氏族，一代没人当官就断。
    // 现在沿承嗣的一方往下传，离最近一位在任过的祖先不超过 Generations 代都算族人
    public static class Lineage
    {
        // 离最近一位在任过的祖先隔了几代：在任过的人是 0
        const string KeyGen = "vd_clan_gen";
        const int Generations = 3;

        public static void MarkOffice(Actor pActor)
        {
            if (pActor == null || pActor.data == null) return;
            pActor.data.set(KeyGen, 0);
        }

        // 没有记录的族人多半是原版里在任者的子女
        static int Gen(Actor pActor)
        {
            if (pActor.isKing() || pActor.isCityLeader()) return 0;
            pActor.data.get(KeyGen, out int gen, 1);
            return gen;
        }

        public static void OnBirth(Actor pParent1, Actor pParent2, Actor pBaby)
        {
            // 原版已经按在任的父母给了氏族
            if (pBaby.hasClan())
            {
                pBaby.data.set(KeyGen, 1);
                return;
            }
            Actor heir = LineParent(pParent1, pParent2, pBaby);
            if (heir == null || !heir.hasClan()) return;
            int gen = Gen(heir) + 1;
            if (gen > Generations || heir.clan.isFull()) return;
            pBaby.setClan(heir.clan);
            pBaby.data.set(KeyGen, gen);
        }

        // 承嗣的一方：母系文化看母亲，其他看父亲；父母同性别时取离在任祖先近的
        static Actor LineParent(Actor pParent1, Actor pParent2, Actor pBaby)
        {
            if (pParent2 == null) return pParent1;
            Culture culture = pBaby.culture ?? pParent1.culture;
            bool matri = culture != null && culture.hasTrait("matriarchy");
            bool line1 = matri ? pParent1.isSexFemale() : pParent1.isSexMale();
            bool line2 = matri ? pParent2.isSexFemale() : pParent2.isSexMale();
            if (line1 != line2) return line1 ? pParent1 : pParent2;
            if (!pParent1.hasClan()) return pParent2;
            if (!pParent2.hasClan()) return pParent1;
            return Gen(pParent2) < Gen(pParent1) ? pParent2 : pParent1;
        }
    }

    [HarmonyPatch(typeof(BabyHelper), nameof(BabyHelper.applyParentsMeta))]
    static class Patch_LineageBirth
    {
        static void Postfix(Actor pParent1, Actor pParent2, Actor pBaby) => Lineage.OnBirth(pParent1, pParent2, pBaby);
    }

    [HarmonyPatch(typeof(City), nameof(City.setLeader))]
    static class Patch_LineageLeader
    {
        static void Postfix(City __instance, Actor pActor)
        {
            if (pActor != null && __instance.leader == pActor) Lineage.MarkOffice(pActor);
        }
    }

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
    static class Patch_LineageKing
    {
        static void Postfix(Actor pActor) => Lineage.MarkOffice(pActor);
    }
}
