using System;
using System.Collections.Generic;
using System.IO;
using NeoModLoader.api;
using NeoModLoader.constants;
using Newtonsoft.Json;

namespace DiplomacyOverhaul
{
    // 外壳：NML 加载的入口。功能都在 Core 里，开发时由 DevReload 热替换
    public class Main : BasicMod<Main>
    {
        // 模组 ID 原来是 shizhiyun_diplomacy_overhaul。NML 按 ID 给玩家的设置建文件，换了 ID 就找不到原来的设置
        const string OldConfigFile = "SHIZHIYUN_DIPLOMACY_OVERHAUL.config";

        protected override void OnModLoad()
        {
            string folder = GetDeclaration().FolderPath;
            MoveOldSettings(GetConfig());
            Core.Start(GetConfig(), folder);
            DevReload.Install(gameObject, folder, GetConfig());
            LogInfo("外交大修 loaded");
        }

        // 换 ID 后第一次启动：把旧文件里的设置搬进现在的设置并存盘，旧文件改名，之后不再搬
        void MoveOldSettings(ModConfig pConfig)
        {
            string old = Path.Combine(Paths.ModsConfigPath, OldConfigFile);
            if (!File.Exists(old)) return;
            try
            {
                var groups = JsonConvert.DeserializeObject<Dictionary<string, List<ModConfigItem>>>(File.ReadAllText(old));
                int moved = 0;
                if (groups != null)
                    foreach (var group in groups)
                    foreach (ModConfigItem item in group.Value)
                    {
                        // 新版本去掉的组或开关跳过
                        try
                        {
                            pConfig[group.Key][item.Id].SetValue(item.GetValue(), true);
                            moved++;
                        }
                        catch (KeyNotFoundException) { }
                    }
                pConfig.Save();
                File.Move(old, old + ".moved");
                LogInfo($"已从旧的模组 ID 搬来 {moved} 项设置");
            }
            catch (Exception e)
            {
                LogWarning("搬旧设置失败，按默认设置启动：" + e.Message);
            }
        }
    }
}
