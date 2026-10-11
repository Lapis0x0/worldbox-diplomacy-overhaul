using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 框选下令：不拿神力时左键框选，右键点城市，选中的军队整支执行：敌城进攻，本国城市驻扎。
    // 缩远时框选的是军队（看旗帜即队长的位置，只取框里军队最多的那个国家）；
    // 拉近时仍是原版的框选单位，右键时换算成这些士兵所属的军队。其余情况交还原版右键
    public static class BoxCommand
    {
        // 缩远时框选出的军队
        static readonly List<Army> _armies = new List<Army>();

        public static void Clear() => _armies.Clear();

        public static bool HasBoxSelection()
        {
            foreach (Army army in _armies)
            {
                if (Powers.AliveArmy(army)) return true;
            }
            return false;
        }

        // Esc 取消框选的军队；开着窗口时 Esc 留给原版关窗口。返回 true 表示 Esc 已被用掉
        public static bool Escape()
        {
            if (_armies.Count == 0) return false;
            if (ScrollWindow.isWindowActive() || ScrollWindow.isAnimationActive()) return false;
            _armies.Clear();
            WorldTip.showNow("vd_tip_cancelled", true, "top");
            return true;
        }

        // 缩远时框选结束：替换原版的"选中框里的单位"。框太小就是一次单击，清空选择
        public static void SelectInBox(Vector2 pFrom, Vector2 pTo)
        {
            _armies.Clear();
            SelectedUnit.clear();
            float minX = Mathf.Min(pFrom.x, pTo.x), maxX = Mathf.Max(pFrom.x, pTo.x);
            float minY = Mathf.Min(pFrom.y, pTo.y), maxY = Mathf.Max(pFrom.y, pTo.y);
            if (maxX - minX < 2f && maxY - minY < 2f) return;

            var byKingdom = new Dictionary<Kingdom, List<Army>>();
            foreach (Army army in World.world.armies)
            {
                if (!Powers.AliveArmy(army) || army.getCity() == null) continue;
                Actor lead = army.getCaptain();
                if (lead == null || lead.isRekt()) continue;
                Vector2 pos = lead.current_position;
                if (pos.x < minX || pos.x > maxX || pos.y < minY || pos.y > maxY) continue;
                Kingdom k = army.getCity().kingdom;
                if (!byKingdom.TryGetValue(k, out List<Army> list)) byKingdom[k] = list = new List<Army>();
                list.Add(army);
            }
            foreach (List<Army> list in byKingdom.Values)
            {
                if (list.Count > _armies.Count)
                {
                    _armies.Clear();
                    _armies.AddRange(list);
                }
            }
            if (_armies.Count > 0)
                WorldTip.showNow(LM.Get("vd_tip_box_selected").Replace("$count$", _armies.Count.ToString()), false, "top");
        }
        public static void Init()
        {
            // 原版选框只在拉近时画，缩远也要看得见
            QuantumSpriteAsset box = AssetManager.quantum_sprites.get("square_selection");
            if (box != null) box.render_map = true;
        }

        // 当前选中的军队，依次看：缩远框选的军队；单击旗帜后原版选中的那支军队；
        // 拉近时选中的单位所属的军队（去重）
        public static List<Army> SelectedArmies()
        {
            var result = new List<Army>();
            foreach (Army army in _armies)
            {
                if (Powers.AliveArmy(army) && army.getCity() != null) result.Add(army);
            }
            if (result.Count > 0) return result;
            if (SelectedObjects.getSelectedNanoObject() is Army picked && Powers.AliveArmy(picked) && picked.getCity() != null)
            {
                result.Add(picked);
                return result;
            }
            if (!SelectedUnit.isSet()) return result;
            foreach (Actor unit in SelectedUnit.getAllSelected())
            {
                Army army = unit?.army;
                if (Powers.AliveArmy(army) && army.getCity() != null && !result.Contains(army)) result.Add(army);
            }
            return result;
        }

        // 返回 true 表示右键已被处理
        public static bool RightClick(WorldTile pTile)
        {
            City city = pTile?.zone?.city;
            if (city == null || city.isRekt() || city.kingdom == null || city.kingdom.isNeutral()) return false;
            bool fromBox = HasBoxSelection();
            List<Army> armies = SelectedArmies();
            if (armies.Count == 0) return false;

            int done = 0;
            string error = null;
            bool own = false;
            foreach (Army army in armies)
            {
                own = city.kingdom == army.getCity().kingdom;
                string e = Orders.CommandArmy(army, city);
                if (e == null) done++;
                else error = error ?? e;
            }
            if (done == 0)
            {
                WorldTip.showNow(error, true, "top");
                return true;
            }
            WorldTip.showNow(LM.Get(own ? "vd_tip_box_garrison" : "vd_tip_box_attack")
                .Replace("$count$", done.ToString()).Replace("$target$", city.data.name), false, "top");
            if (fromBox)
            {
                // 缩远框选的命令下完了：清空选择，地图留在城市视图，不跳回原来的视图
                _armies.Clear();
                UseCityLayer();
            }
            return true;
        }

        // 等同于点了工具栏上的"城市图层"
        static void UseCityLayer()
        {
            if (!PlayerConfig.dict.TryGetValue("map_city_layer", out PlayerOptionData option)) return;
            option.boolVal = true;
            PowerLibrary.disableAllOtherMapModes("city_layer");
            PlayerConfig.saveData();
        }
    }

    // 原版在缩远的地图画面里直接关掉框选；这里照抄原版判定，只去掉那一段
    [HarmonyPatch(typeof(PlayerControl), "checkSquareMultiSelection")]
    static class Patch_BoxSelectOnMap
    {
        static bool Prefix(PlayerControl __instance, ref bool __result)
        {
            if (!MapBox.isRenderMiniMap()) return true;
            __result = Check(__instance);
            return false;
        }

        static bool Check(PlayerControl pc)
        {
            if (ControllableUnit.isControllingUnit() || World.world.isAnyPowerSelected()) return false;
            if (ScrollWindow.isWindowActive() || ScrollWindow.isAnimationActive()) return false;
            if (!InputHelpers.mouseSupported) return false;
            if (!InputHelpers.GetMouseButton(0) || InputHelpers.GetMouseButton(1) || InputHelpers.GetMouseButton(2)) return false;
            if (!pc.square_selection_started && !pc._ignore_square_selection)
            {
                if (pc.isOverUI())
                {
                    pc._ignore_square_selection = true;
                }
                else
                {
                    pc.square_selection_started = true;
                    pc.square_selection_position_current = pc.getMousePos();
                }
            }
            return true;
        }
    }

    // 框选了军队时切到城市视图，方便挑目标城市；原版拿神力时按神力的 force_map_mode 强制视图，这里补一条
    [HarmonyPatch(typeof(Zones), nameof(Zones.getForcedMapMode))]
    static class Patch_BoxCityView
    {
        static void Postfix(ref MetaType __result)
        {
            if (__result.isNone() && !World.world.isAnyPowerSelected() && BoxCommand.HasBoxSelection()) __result = MetaType.City;
        }
    }

    // 原版 Esc 依次关控制台、关窗口、退标签页、取消神力、取消选中单位，都没有才弹退出窗口；
    // 框选了军队时先取消框选
    [HarmonyPatch(typeof(HotkeyLibrary), "escapeAction")]
    static class Patch_BoxEscape
    {
        static bool Prefix() => !BoxCommand.Escape();
    }

    // 缩远时框选结束：选军队而不是选单位
    [HarmonyPatch(typeof(PlayerControl), "checkSelectedUnits")]
    static class Patch_BoxSelectArmies
    {
        static bool Prefix(PlayerControl __instance)
        {
            if (!MapBox.isRenderMiniMap())
            {
                // 拉近时是原版框选单位，之前缩远框选的军队作废
                BoxCommand.Clear();
                return true;
            }
            BoxCommand.SelectInBox(__instance.square_selection_position_current, __instance.getMousePos());
            return false;
        }
    }

    // 原版框选进行中会把镜头最远拉到看得见小人的距离（MoveCamera.zoomToBounds），
    // 原版只在拉近时框选所以不明显；缩远框选时不要把镜头拉近
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.isSelectionHappens))]
    static class Patch_BoxSelectNoZoom
    {
        static void Postfix(ref bool __result)
        {
            if (__result && MapBox.isRenderMiniMap()) __result = false;
        }
    }

    // 右键：选中的有士兵且点在城市上时按军队下令，否则走原版（单位走过去 / Ctrl 攻击）
    [HarmonyPatch(typeof(PlayerControl), "tryToMoveSelectedUnits")]
    static class Patch_BoxRightClick
    {
        static bool Prefix(PlayerControl __instance, ref bool __result)
        {
            if (!InputHelpers.mouseSupported || !InputHelpers.GetMouseButtonUp(1)) return true;
            if (MoveCamera.camera_drag_activated || __instance.already_used_camera_drag) return true;
            if (!BoxCommand.RightClick(__instance.getMouseTilePosCachedFrame())) return true;
            __result = true;
            return false;
        }
    }
}
