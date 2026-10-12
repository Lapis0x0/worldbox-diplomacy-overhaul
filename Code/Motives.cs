using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityPools;

namespace DiplomacyOverhaul
{
    // 密谋的动机：条件决定能不能做，动机决定有多想做。
    // 原版国王隔一段时间把所有可行的密谋放进池子随机抽一个，池子大多是空的或只有一两个，
    // 所以动机不跟别的密谋抢份数，而是每次考虑时以 动机 / (动机 + 1) 的概率把这个密谋放进池子。
    // 动机 = 局势 × 性格，性格用原版的性格数值（好战、治理、外交）和几个特质
    public static class Motives
    {
        static readonly Dictionary<string, Func<Actor, float>> _motives = new Dictionary<string, Func<Actor, float>>();

        public const string Aggression = "personality_aggression";
        public const string Administration = "personality_administration";
        public const string Diplomacy = "personality_diplomatic";

        public static void Register(PlotAsset pAsset, Func<Actor, float> pMotive) => _motives[pAsset.id] = pMotive;

        // 主导性格（数值 0.5）×1.5，其余 ×0.6–0.9；争权类密谋野心勃勃 ×1.5；知足 ×0.5
        public static float Personality(Actor pActor, float pStat, bool pPowerGrab)
        {
            float m = 0.5f + 2f * pStat;
            if (pPowerGrab && pActor.hasTrait("ambitious")) m *= 1.5f;
            if (pActor.hasTrait("content")) m *= 0.5f;
            return m;
        }

        public static float Stat(Actor pActor, string pId) => pActor.stats[pId];

        // 这次考虑时要不要把密谋放进池子
        static bool Wants(Actor pActor, Func<Actor, float> pMotive)
        {
            float m = pMotive(pActor);
            // 狂人忽冷忽热
            if (pActor.stats["personality_rationality"] < 0f) m *= Randy.randomFloat(0f, 2f);
            if (m <= 0f) return false;
            return Randy.randomChance(m / (m + 1f));
        }

        internal static void Filter(Actor pActor, ListPool<PlotAsset> pPool)
        {
            foreach (KeyValuePair<string, Func<Actor, float>> pair in _motives)
            {
                PlotAsset asset = AssetManager.plots_library.get(pair.Key);
                if (asset == null || !pPool.Contains(asset)) continue;
                if (!Wants(pActor, pair.Value)) pPool.RemoveAll(p => p == asset);
            }
        }
    }

    // fillRandomPlots 每次考虑只调用一次（fillPlotsToTry 会分别为普通密谋和宗教仪式各调用一次）
    [HarmonyPatch(typeof(BehTryNewPlot), "fillRandomPlots")]
    static class Patch_PlotMotives
    {
        static void Postfix(Actor pActor, ListPool<PlotAsset> pPotPlots) => Motives.Filter(pActor, pPotPlots);
    }
}
