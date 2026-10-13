using System.Collections.Generic;
using System.Reflection;

namespace DiplomacyOverhaul.Perf
{
    // 每项优化一个开关，补丁里直接读这些静态字段。
    // 玩家只看到一个总开关（Perf 按它整体切换）；开发命令 opt 可以按字段名单独切换
    public static class Opt
    {
        // 对照验证：打开后，号称结果不变的改写会同时用原版算法再算一遍并比较，不一致写日志（开发用，很慢）
        public static bool verify = false;

        // 地图边缘图层只重画变化的地块及其上下邻居，不再整片重画周围 9 个区块
        public static bool edges_incremental = true;

        // 收藏物品、收藏单位维护一份名单，按钮状态和地图上的收藏物品图标只查名单，不再每次遍历全部物品和单位
        public static bool favorites_cache = true;

        // 只影响显示的刷新降频：国界每 0.1 秒重画，右下角统计面板每 25 帧重数，家族物种图标位置每 0.2 秒重算
        public static bool visual_throttle = true;

        // 野生王国（遗迹、蔓延等）的建筑名单不再每次有植物生灭就重扫全部建筑，改为在用到名单之前才重建
        public static bool wild_buildings_lazy = true;

        // 城市建筑名单只重建发生变化的城市，不再任何一个区域变化就重建所有城市
        public static bool city_buildings_partial = true;

        // 单位找敌人时不再为遍历列表分配枚举器（结果和随机数消耗与原版相同）
        public static bool enemy_search_noalloc = true;

        // 属性表清空时回收属性容器，重算时复用，不再每次重算都新建几十个对象
        public static bool stats_container_pool = true;

        // 区块建筑名单只重新登记有变动的区块里的建筑，不再每次遍历全部建筑（顺序与原版相同）
        public static bool sim_zones_buildings = true;

        public static IEnumerable<FieldInfo> All()
        {
            foreach (FieldInfo field in typeof(Opt).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.FieldType == typeof(bool)) yield return field;
        }

        public static bool Set(string pName, bool pValue)
        {
            bool found = false;
            foreach (FieldInfo field in All())
            {
                if (pName == "all" ? field.Name == nameof(verify) : field.Name != pName) continue;
                field.SetValue(null, pValue);
                found = true;
            }
            return found;
        }


        public static string Describe()
        {
            var parts = new List<string>();
            foreach (FieldInfo field in All()) parts.Add(field.Name + "=" + ((bool)field.GetValue(null) ? "on" : "off"));
            return string.Join(" ", parts);
        }
    }
}
