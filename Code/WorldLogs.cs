using UnityEngine;

namespace DiplomacyOverhaul
{
    // AI 自己发生的事记进左上角的世界日志，和原版的建国、开战消息放在一起，不占屏幕正中
    public static class WorldLogs
    {
        public const string Feudal = "vd_log_feudal";           // $lord$ 将 $city$ 分封给了 $ruler$
        public const string Capitulated = "vd_log_capitulated"; // $vassal$ 投降，成为 $lord$ 的附庸
        public const string Tribute = "vd_log_tribute";         // $vassal$ 向 $lord$ 称臣

        public static void Init()
        {
            Register(Feudal, "ui/Icons/iconVillages", Toolbox.color_log_neutral, "$lord$", "$city$", "$ruler$");
            Register(Capitulated, "ui/Icons/iconWar", Toolbox.color_log_warning, "$vassal$", "$lord$");
            Register(Tribute, "ui/Icons/iconCrown", Toolbox.color_log_neutral, "$vassal$", "$lord$");
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
