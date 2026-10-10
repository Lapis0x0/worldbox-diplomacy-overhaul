using NeoModLoader.api;

namespace DiplomacyOverhaul
{
    // 外壳：NML 加载的入口。功能都在 Core 里，开发时由 DevReload 热替换
    public class Main : BasicMod<Main>
    {
        protected override void OnModLoad()
        {
            Core.Start(GetConfig());
            DevReload.Install(gameObject, GetDeclaration().FolderPath, GetConfig());
            LogInfo("外交大修 loaded");
        }
    }
}
