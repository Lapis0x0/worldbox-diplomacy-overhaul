using System;
using System.IO;
using System.Reflection;
using NeoModLoader.api;
using NeoModLoader.General;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 开发用热重载：tools/hot-reload.sh 编译出新 dll 并写入 bin/reload.signal，
    // 这里发现信号后停掉当前版本的 Core，载入新 dll 并启动它的 Core。
    // bin/ 不会被 NML 编译或上传，所以玩家那边永远不会触发。
    public class DevReload : MonoBehaviour
    {
        string _folder;
        ModConfig _config;
        Type _core = typeof(Core);
        float _timer;

        public static void Install(GameObject pHost, string pFolder, ModConfig pConfig)
        {
            DevReload reload = pHost.AddComponent<DevReload>();
            reload._folder = pFolder;
            reload._config = pConfig;
        }

        string SignalPath => Path.Combine(_folder, "bin", "reload.signal");

        void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < 1f) return;
            _timer = 0f;
            if (File.Exists(SignalPath)) Reload();
        }

        void Reload()
        {
            string dll = File.ReadAllText(SignalPath).Trim();
            File.Delete(SignalPath);
            try
            {
                string pdb = Path.ChangeExtension(dll, ".pdb");
                Assembly assembly = File.Exists(pdb)
                    ? Assembly.Load(File.ReadAllBytes(dll), File.ReadAllBytes(pdb))
                    : Assembly.Load(File.ReadAllBytes(dll));
                Type next = assembly.GetType("DiplomacyOverhaul.Core", true);

                Call(_core, "Stop");
                try
                {
                    Call(next, "Start", _config, _folder);
                }
                catch
                {
                    // 新版本启动失败：恢复旧版本，游戏继续可用
                    Call(next, "Stop");
                    Call(_core, "Start", _config, _folder);
                    throw;
                }
                _core = next;
                ReloadLocales();
                Debug.Log("[外交大修] hot reloaded " + Path.GetFileName(dll));
                WorldTip.showNow("外交大修：热重载完成", false, "top");
            }
            catch (Exception e)
            {
                Debug.LogError("[外交大修] hot reload failed: " + e);
                WorldTip.showNow("外交大修：热重载失败，详见日志", false, "top");
            }
        }

        static void Call(Type pType, string pMethod, params object[] pArgs)
        {
            try
            {
                pType.GetMethod(pMethod, BindingFlags.Public | BindingFlags.Static).Invoke(null, pArgs);
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException ?? e;
            }
        }

        void ReloadLocales()
        {
            string dir = Path.Combine(_folder, "Locales");
            if (!Directory.Exists(dir)) return;
            foreach (string file in Directory.GetFiles(dir, "*.json"))
                LM.LoadLocale(Path.GetFileNameWithoutExtension(file), file);
            LM.ApplyLocale();
        }
    }
}
