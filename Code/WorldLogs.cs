using UnityEngine;

namespace DiplomacyOverhaul
{
    // AI 自己发生的事记进左上角的世界日志，和原版的建国、开战消息放在一起，不占屏幕正中
    public static class WorldLogs
    {
        public const string Feudal = "vd_log_feudal";           // $lord$ 将 $city$ 分封给了 $ruler$
        public const string Capitulated = "vd_log_capitulated"; // $vassal$ 投降，成为 $lord$ 的附庸
        public const string Ceded = "vd_log_ceded";             // $vassal$ 归附 $lord$
        public const string PeaceWhite = "vd_log_peace_white";             // $attacker$ 与 $defender$ 议和
        public const string PeaceReparations = "vd_log_peace_reparations"; // $loser$ 向 $winner$ 赔款求和
        public const string PeaceCession = "vd_log_peace_cession";         // $loser$ 赔款并让出附庸，与 $winner$ 议和
        public const string Tribute = "vd_log_tribute";         // $vassal$ 向 $lord$ 称臣
        public const string Province = "vd_log_province";            // $vassal$ 成为 $lord$ 的行省
        public const string ProvinceLost = "vd_log_province_lost";   // $vassal$ 不再是 $lord$ 的行省
        public const string HereditaryProvince = "vd_log_hereditary_province"; // $vassal$ 成为 $lord$ 的世袭行省
        public const string HereditaryLost = "vd_log_hereditary_lost";         // $vassal$ 不再世袭，改为 $lord$ 的行省
        public const string Appanage = "vd_log_appanage";             // $lord$ 将 $city$ 封给宗室 $ruler$
        public const string DirectRule = "vd_log_direct_rule";            // $lord$ 将 $vassal$ 收归直辖
        public const string DirectRulePart = "vd_log_direct_rule_part";   // $lord$ 将 $vassal$ 的 $count$ 座城收归直辖
        public const string Restored = "vd_log_restored";             // $ruler$ 中兴 $kingdom$
        public const string RestoreFailed = "vd_log_restore_failed";  // $ruler$ 中兴 $kingdom$ 失败，国势更衰
        public const string Elected = "vd_log_elected";                  // $ruler$ 当选 $kingdom$ 执政
        public const string RepublicFounded = "vd_log_republic_founded"; // $kingdom$ 王室绝嗣，$ruler$ 被推举执政，改制为共和国
        public const string Crowned = "vd_log_crowned";                  // $ruler$ 在 $kingdom$ 称帝
        public const string Usurped = "vd_log_usurped";                  // $ruler$ 篡夺 $kingdom$ 王位，建立新王朝
        public const string Junta = "vd_log_junta";                      // $kingdom$ 的将领们夺取政权，$ruler$ 执政
        public const string PolicyEnacted = "vd_log_policy";             // $kingdom$ 推行$policy$

        public static void Init()
        {
            Register(Feudal, "ui/Icons/vd_power_release_city", Toolbox.color_log_neutral, "$lord$", "$city$", "$ruler$");
            Register(Capitulated, "ui/Icons/iconWar", Toolbox.color_log_warning, "$vassal$", "$lord$");
            Register(Ceded, "ui/Icons/vd_power_make_vassal", Toolbox.color_log_neutral, "$vassal$", "$lord$");
            Register(PeaceWhite, "ui/Icons/iconWar", Toolbox.color_log_neutral, "$attacker$", "$defender$");
            Register(PeaceReparations, "ui/Icons/iconWar", Toolbox.color_log_neutral, "$loser$", "$winner$");
            Register(PeaceCession, "ui/Icons/iconWar", Toolbox.color_log_warning, "$loser$", "$winner$");
            Register(Tribute, "ui/Icons/vd_power_make_vassal", Toolbox.color_log_neutral, "$vassal$", "$lord$");
            Register(Province, "ui/Icons/vd_gov_province", Toolbox.color_log_neutral, "$vassal$", "$lord$");
            Register(HereditaryProvince, "ui/Icons/vd_gov_province", Toolbox.color_log_neutral, "$vassal$", "$lord$");
            Register(HereditaryLost, "ui/Icons/vd_gov_province", Toolbox.color_log_warning, "$vassal$", "$lord$");
            Register(ProvinceLost, "ui/Icons/vd_gov_dominion", Toolbox.color_log_warning, "$vassal$", "$lord$");
            Register(Appanage, "ui/Icons/vd_power_release_city", Toolbox.color_log_neutral, "$lord$", "$city$", "$ruler$");
            Register(DirectRule, "ui/Icons/vd_power_annex", Toolbox.color_log_neutral, "$lord$", "$vassal$");
            Register(DirectRulePart, "ui/Icons/vd_power_annex", Toolbox.color_log_neutral, "$lord$", "$vassal$", "$count$");
            Register(Restored, "ui/Icons/vd_cycle_prime", Toolbox.color_log_good, "$ruler$", "$kingdom$");
            Register(RestoreFailed, "ui/Icons/vd_cycle_decline", Toolbox.color_log_neutral, "$ruler$", "$kingdom$");
            Register(Elected, "ui/Icons/vd_gov_republic", Toolbox.color_log_neutral, "$ruler$", "$kingdom$");
            Register(RepublicFounded, "ui/Icons/vd_gov_republic", Toolbox.color_log_neutral, "$kingdom$", "$ruler$");
            Register(Crowned, "ui/Icons/iconKingdomList", Toolbox.color_log_warning, "$ruler$", "$kingdom$");
            Register(Usurped, "ui/Icons/vd_cycle_renewal", Toolbox.color_log_warning, "$ruler$", "$kingdom$");
            Register(Junta, "ui/Icons/vd_gov_military_republic", Toolbox.color_log_warning, "$kingdom$", "$ruler$");
            Register(PolicyEnacted, ParliamentWindow.IconPath, Toolbox.color_log_neutral, "$kingdom$", "$policy$");
        }

        // 文案里的占位符依次对应消息的 special1~3
        static void Register(string pId, string pIcon, Color pColor, params string[] pKeys)
        {
            Core.Upsert(AssetManager.world_log_library, new WorldLogAsset
            {
                id = pId,
                group = "kingdoms",
                path_icon = pIcon,
                color = pColor,
                text_replacer = (WorldLogMessage pMessage, ref string pText) =>
                {
                    for (int i = 0; i < pKeys.Length; i++) pText = pText.Replace(pKeys[i], pMessage.getSpecial(i + 1));
                }
            });
        }

        // pTexts 与 pColors 一一对应，名字按所属国家的颜色显示；pUnit 存活时点消息镜头跟着它
        public static void Add(string pId, Vector2 pLocation, Actor pUnit, string[] pTexts, Kingdom[] pColors)
        {
            var message = new WorldLogMessage(AssetManager.world_log_library.get(pId),
                pTexts.Length > 0 ? pTexts[0] : null, pTexts.Length > 1 ? pTexts[1] : null, pTexts.Length > 2 ? pTexts[2] : null)
            {
                kingdom = pColors[0],
                unit = pUnit,
                location = pLocation
            };
            if (pColors.Length > 0) message.color_special1 = pColors[0].getColor().getColorText();
            if (pColors.Length > 1) message.color_special2 = pColors[1].getColor().getColorText();
            if (pColors.Length > 2) message.color_special3 = pColors[2].getColor().getColorText();
            message.add();
        }
    }
}
