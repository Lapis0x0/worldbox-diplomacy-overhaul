# 外交大修 Diplomacy Overhaul

为 WorldBox 加入更丰富的外交与战争玩法：附庸国、独立与投降、政体、王朝周期与大叛乱，以及一组直接指挥外交和军队的神力。

- Steam 创意工坊：https://steamcommunity.com/sharedfiles/filedetails/?id=3817246294
- 依赖：[NeoModLoader](https://github.com/WorldBoxOpenMods/ModLoader)

## 主要内容

- 附庸体系：宗主与附庸同进同退；附庸不满、宗主衰落时会密谋独立，也可以用神力煽动独立
- 王朝周期：帝国经历新生、鼎盛、衰落、崩溃；衰落时附庸离心、城市不稳
- 大叛乱：国内多地同时揭竿而起，附庸纷纷独立；首都沦陷或天命已尽时爆发
- 战争：战败一方可能投降成为附庸；在军队视图里直接指挥军队进攻或驻扎，跨海自动调船；攻城时军队会主动清剿守军
- 政体：城邦 / 王国 / 帝国 / 公国，随国力变化
- 城镇管理：移交城市、吞并王国

所有功能都在底部「外交大修」Tab 里，各项数值可在 Tab 里的设置按钮中调整。数据保存在原版存档中，可以直接用于已有存档。

## 本地开发

- 把仓库放进（或软链接到）`worldbox/Mods/DiplomacyOverhaul`，NML 启动时会编译 `Code/` 下的源码；`bin/`、`obj/` 和以 `.` 开头的文件不会被编译或上传
- 本地编译检查：`dotnet build`（需要 .NET SDK，引用游戏目录里的程序集）
- 热重载：游戏开着时运行 `tools/hot-reload.sh`，改动会在一两秒内生效，不用重启游戏
  - 改 `Main.cs`、`DevReload.cs`、`mod.json`、`default_config.json` 仍需重启
  - `Core.Start` 必须能重复执行：资源用 `Core.Upsert` 注册，Tab 按钮会被清掉重建
- 发布：在 NML 模组列表里连点封面 8 次后等 3 秒打开上传窗口；更新时在顶部填创意工坊物品 ID，空着会新建物品

## 许可证

MIT，见 [LICENSE](LICENSE)。
