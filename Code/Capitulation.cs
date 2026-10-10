using HarmonyLib;

namespace DiplomacyOverhaul
{
    // 投降：战争中被打得很惨但还没灭国的一方，成为对方（的宗主）的附庸
    public static class Capitulation
    {
        const string KeyAttackerCities = "vd_att_cities";
        const string KeyDefenderCities = "vd_def_cities";
        const string KeyLastCheck = "vd_last_check";
        const double CheckInterval = 60.0; // 约一年（Date 里一个月 = 5）

        public static void RecordStart(War pWar)
        {
            if (pWar.isTotalWar()) return;
            pWar.data.set(KeyAttackerCities, pWar.main_attacker?.countCities() ?? 0);
            pWar.data.set(KeyDefenderCities, pWar.main_defender?.countCities() ?? 0);
        }

        public static void Check(War pWar)
        {
            if (!Main.CapitulationEnabled || pWar.hasEnded() || pWar.isTotalWar()) return;

            double now = World.world.getCurWorldTime();
            pWar.data.get(KeyLastCheck, out float last, -1f);
            if (last >= 0 && now - last < CheckInterval) return;
            pWar.data.set(KeyLastCheck, (float)now);

            if (pWar.getAge() < Main.CapitulationMinYears) return;

            Kingdom attacker = pWar.main_attacker;
            Kingdom defender = pWar.main_defender;
            if (attacker == null || defender == null || !attacker.isAlive() || !defender.isAlive()) return;

            pWar.data.get(KeyAttackerCities, out int attackerStart, -1);
            pWar.data.get(KeyDefenderCities, out int defenderStart, -1);
            if (attackerStart < 0 || defenderStart < 0)
            {
                // 装模组之前就开始的战争：从现在开始计算
                RecordStart(pWar);
                return;
            }

            if (ShouldSurrender(defender, defenderStart, attacker))
                Surrender(pWar, defender, attacker, WarWinner.Attackers);
            else if (ShouldSurrender(attacker, attackerStart, defender))
                Surrender(pWar, attacker, defender, WarWinner.Defenders);
        }

        static bool ShouldSurrender(Kingdom pLoser, int pStartCities, Kingdom pWinner)
        {
            int cities = pLoser.countCities();
            if (cities == 0 || pStartCities <= 0) return false;
            // 城市损失比例
            if ((pStartCities - cities) * 100 < pStartCities * Main.CapitulationCityLoss) return false;
            // 实力差距
            if (Vassals.Power(pWinner) * 100 < Vassals.Power(pLoser) * Main.CapitulationPowerRatio) return false;
            // 不能向自己的附庸投降
            return Vassals.Root(pWinner) != pLoser;
        }

        static void Surrender(War pWar, Kingdom pLoser, Kingdom pWinner, WarWinner pResult)
        {
            Kingdom lord = Vassals.Root(pWinner);
            // 投降方原有的附庸全部独立（不允许附庸链）
            foreach (Kingdom v in Vassals.GetVassals(pLoser)) v.data.removeLong(Vassals.KeyOverlord);

            World.world.wars.endWar(pWar, pResult);
            if (Vassals.MakeVassal(lord, pLoser) == null)
            {
                WorldTip.showNow(NeoModLoader.General.LM.Get("vd_tip_capitulated")
                    .Replace("$vassal$", pLoser.data.name).Replace("$lord$", lord.data.name), false, "top");
            }
        }
    }

    [HarmonyPatch(typeof(WarManager), nameof(WarManager.newWar))]
    static class Patch_NewWar
    {
        static void Postfix(War __result)
        {
            if (__result != null) Capitulation.RecordStart(__result);
        }
    }

    [HarmonyPatch(typeof(War), nameof(War.update))]
    static class Patch_WarUpdate
    {
        static void Postfix(War __instance) => Capitulation.Check(__instance);
    }
}
