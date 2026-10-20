using System.Collections.Generic;

namespace DiplomacyOverhaul
{
    // 军队大修的总开关：禁军与国家指挥、地方军 AI、命令与框选、攻城、军械、军饷、跨海运兵。
    // 关掉时各处补丁直接走原版，留下的禁军编队、驻扎、欠饷当场清掉；国库照常（上缴、密谋、赔款还要用）
    public static class Military
    {
        public static bool On => Settings.MilitaryEnabled;

        public static void OnToggle(bool pEnabled)
        {
            if (!pEnabled && World.world?.armies != null)
            {
                Orders.ClearAll();
                Guard.DisbandAll();
                Treasury.ClearArrears();
                BoxCommand.Clear();
                if (World.world.isAnyPowerSelected()) PowerButtonSelector.instance.unselectAll();
            }
            ModTab.Build();
            if (World.world?.kingdoms == null) return;
            // 欠饷减伤、海军方针的伤害倍数跟着开关变
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
                if (k.isAlive()) foreach (Actor a in k.units) a.setStatsDirty();
        }
    }
}
