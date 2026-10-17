using System.Collections.Generic;

namespace DiplomacyOverhaul
{
    // 阵营：附庸归入宗主的阵营；宗主有联盟时阵营是整个联盟（成员和成员的附庸），否则是宗主和它的附庸。
    // AI 挑战争目标、估兵力，玩家下令时的友方，联盟图层的染色、名牌和提示都按阵营
    public static class Camp
    {
        public static NanoObject Of(Kingdom k)
        {
            Kingdom root = Vassals.Root(k);
            return (NanoObject)root.getAlliance() ?? root;
        }

        public static bool Same(Kingdom a, Kingdom b) => a != null && b != null && a != b && Of(a) == Of(b);

        // 一次遍历算出每个阵营的总兵力
        public static Dictionary<NanoObject, int> Warriors()
        {
            var result = new Dictionary<NanoObject, int>();
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                NanoObject c = Of(k);
                result.TryGetValue(c, out int n);
                result[c] = n + k.countTotalWarriors();
            }
            return result;
        }

        public static int Warriors(NanoObject pCamp) => Sum(pCamp, k => k.countTotalWarriors());

        public static int Population(NanoObject pCamp) => Sum(pCamp, k => k.getPopulationPeople());

        public static int Power(NanoObject pCamp) => Sum(pCamp, Vassals.Power);

        static int Sum(NanoObject pCamp, System.Func<Kingdom, int> pValue)
        {
            int n = 0;
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (k.isAlive() && k.isCiv() && Of(k) == pCamp) n += pValue(k);
            }
            return n;
        }
    }
}
