using UnityEngine;

namespace DiplomacyOverhaul.Perf
{
    // hudspam：按指定频率往左上角事件提示里塞消息（不记进世界历史），每帧数一次同时存活的条目，
    // 按 history_hud_cap 开和关分开统计，结束时打印。不阻塞后面的命令，可以中途切开关、接着 measure
    static class HudDev
    {
        class Stat
        {
            public long sum;
            public int frames;
            public int max;
        }

        static float _rate;
        static float _until;
        static float _acc;
        static int _sent;
        static Stat _on;
        static Stat _off;

        public static void Start(float pRate, float pSeconds)
        {
            _rate = pRate;
            _until = Time.realtimeSinceStartup + pSeconds;
            _acc = 0f;
            _sent = 0;
            _on = new Stat();
            _off = new Stat();
            Dev.Log($"  history_log={PlayerConfig.optionBoolEnabled("history_log")}");
        }

        public static void Tick()
        {
            if (_until == 0f) return;
            if (Time.realtimeSinceStartup >= _until)
            {
                _until = 0f;
                Dev.Log($"@hud rate={_rate}/s sent={_sent} cap_off: {Describe(_off)} cap_on: {Describe(_on)}");
                return;
            }
            // 暂停时提示条不动，塞了也只是堆着
            if (Config.paused || HistoryHud.instance == null) return;
            _acc += _rate * Time.unscaledDeltaTime;
            while (_acc >= 1f)
            {
                _acc -= 1f;
                _sent++;
                WorldLogs.Notice(WorldLogs.Isolation);
            }
            Stat s = Opt.history_hud_cap ? _on : _off;
            int n = HistoryHud.instance._history_items.Count;
            s.sum += n;
            s.frames++;
            if (n > s.max) s.max = n;
        }

        static string Describe(Stat s) => s.frames == 0 ? "-" : $"items_avg={(float)s.sum / s.frames:F1} items_max={s.max}";
    }
}
