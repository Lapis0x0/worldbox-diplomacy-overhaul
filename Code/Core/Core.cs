using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.utils;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 模组的全部功能都从这里启动和停止。热重载时旧版本 Stop，新编译的版本 Start，
    // 所以 Start 必须能在已经注册过资源、已经建过 Tab 的游戏里重复执行。
    public static class Core
    {
        const string HarmonyId = "lapis0x0.diplomacy_overhaul";

        static Harmony _harmony;

        public static void Start(ModConfig pConfig, string pFolder)
        {
            Settings.Config = pConfig;
            Perf.Perf.Start(pFolder);
            LoadIcons(pFolder);
            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(Core).Assembly);
            if (Perf.Dev.Enabled) Safety.Audit(_harmony);
            Vassals.Init();
            Independence.Init();
            Feudal.Init();
            Government.Init();
            Policies.Init();
            Cycle.Init();
            Assimilation.Init();
            Treasury.Init();
            Parliament.Init();
            Politics.Init();
            Restoration.Init();
            PeaceTalks.Init();
            Appanage.Init();
            DirectRule.Init();
            Integration.Init();
            WorldLogs.Init();
            AllianceView.Init();
            Powers.Init();
            Orders.Init();
            Visuals.Init();
            BoxCommand.Init();
            ParliamentWindow.Init();
            ElectionWindow.Init();
            ClanRelationsView.Init();
            TreasuryWindow.Init();
            SettingsWindow.Init();
            ModTab.Build();
        }

        public static void Stop()
        {
            Perf.Perf.Stop();
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        // NML 只在游戏启动时加载 GameResources/ 下的图片；这里再注册一遍，热重载时改过的图标也能换上。
        // 原版 SpriteTextureLoader 按路径缓存，要把旧的清掉
        static void LoadIcons(string pFolder)
        {
            string root = Path.Combine(pFolder, "GameResources");
            if (!Directory.Exists(root)) return;
            foreach (string file in Directory.GetFiles(root, "*.png", SearchOption.AllDirectories))
            {
                Sprite[] sprites = SpriteLoadUtils.LoadSprites(file);
                if (sprites.Length == 0) continue;
                string path = file.Substring(root.Length + 1).Replace('\\', '/');
                path = path.Substring(0, path.Length - ".png".Length);
                ResourcesPatch.PatchResource(path, sprites[0]);
                SpriteTextureLoader._cached_sprites.Remove(path);
            }
        }

        // 资源已存在时不替换对象（王国、进行中的密谋都引用着它），只把委托和简单字段更新成新代码的版本
        public static T Upsert<T>(AssetLibrary<T> pLibrary, T pAsset) where T : Asset
        {
            if (!pLibrary.dict.TryGetValue(pAsset.id, out T existing)) return pLibrary.add(pAsset);
            foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Type type = field.FieldType;
                if (typeof(Delegate).IsAssignableFrom(type) || type.IsPrimitive || type.IsEnum || type == typeof(string))
                    field.SetValue(existing, field.GetValue(pAsset));
            }
            return existing;
        }
    }
}
