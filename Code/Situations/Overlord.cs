using UnityEngine;

namespace DiplomacyOverhaul
{
    // 宗主：附庸的局势里，宗主算一股大势。整合度越高宗主越插得上手，刚投降的附庸宗主几乎管不着
    public static partial class Situations
    {
        public const string OverlordId = "vd_sit_overlord";
        const float OverlordWeight = 0.2f;

        // 兵变危机、王权旁落：宗主一般求稳，站在附庸君主一边往左头拉；好感 +50 以上出全力，
        // 好感低于 −50 时乐见附庸生乱，反过来往右头推
        public static float OverlordForCrown(Kingdom pKingdom)
        {
            Kingdom lord = Vassals.GetOverlord(pKingdom);
            if (lord == null) return 0f;
            int opinion = World.world.diplomacy.getOpinion(lord, pKingdom).total;
            float stance = Mathf.Clamp(0.5f + opinion / 100f, -1f, 1f);
            return -OverlordReach(pKingdom) * stance;
        }

        // 王位空悬：附庸已经没有君主可扶，宗主看附庸以后的国体，君主国宗主往新朝登基（右头）推，共和国宗主往改行共和推
        public static float OverlordForForm(Kingdom pKingdom)
        {
            Kingdom lord = Vassals.GetOverlord(pKingdom);
            if (lord == null) return 0f;
            return OverlordReach(pKingdom) * (Government.IsRepublic(lord) ? -1f : 1f);
        }

        static float OverlordReach(Kingdom pKingdom) => OverlordWeight * Mathf.Clamp01(Integration.Get(pKingdom) / 100f);
    }
}
