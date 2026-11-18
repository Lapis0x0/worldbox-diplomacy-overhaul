namespace DiplomacyOverhaul
{
    // 阶段效果：抽中的候选累加起来存进 kingdom.data，忠诚和单位属性结算时直接读。
    // 单位属性在并行任务里重算，这里不用静态缓存
    public static partial class Situations
    {
        const string KeyLoyalty = "vd_sit_loyalty", KeyDamage = "vd_sit_damage";

        public static int Loyalty(City pCity)
        {
            Kingdom k = pCity.kingdom;
            if (k == null) return 0;
            k.data.get(KeyLoyalty, out int loyalty, 0);
            return loyalty;
        }

        public static int DamagePercent(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyDamage, out int damage, 0);
            return damage;
        }

        static void Store(Kingdom pKingdom, string pKey, int pValue)
        {
            if (pValue == 0) pKingdom.data.removeInt(pKey);
            else pKingdom.data.set(pKey, pValue);
        }

        static void Refresh(Kingdom pKingdom)
        {
            int loyalty = 0, damage = 0;
            foreach (SituationDef d in All)
            {
                if (!IsActive(pKingdom, d)) continue;
                for (int s = 0; s < StageCount; s++)
                {
                    SituationPick pick = Picked(pKingdom, d, s);
                    if (pick == null) continue;
                    loyalty += pick.loyalty;
                    damage += pick.damage;
                }
            }
            Store(pKingdom, KeyLoyalty, loyalty);
            if (damage == DamagePercent(pKingdom)) return;
            Store(pKingdom, KeyDamage, damage);
            foreach (Actor a in pKingdom.units) a.setStatsDirty();
        }
    }
}
