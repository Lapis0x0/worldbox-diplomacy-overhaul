# 外交大修 Diplomacy Overhaul

WorldBox 的 NeoModLoader 模组：附庸国、独立战争、战败投降与政体系统。

- Steam 创意工坊：https://steamcommunity.com/sharedfiles/filedetails/?id=3817246294
- 依赖：[NeoModLoader](https://github.com/WorldBoxOpenMods/ModLoader)

## 本地开发

- 游戏读取的是 `worldbox/Mods/DiplomacyOverhaul`，它是指向本仓库的符号链接
- 本地编译检查：`dotnet build`（需要 .NET SDK，引用游戏目录里的程序集）
- 游戏内由 NML 编译 `Code/` 下的源码；`bin/`、`obj/` 和以 `.` 开头的文件不会被编译或上传

## 进度
- [x] 附庸关系、外交规则、独立密谋、战败投降
- [x] 外交大修 Tab、王国窗口宗主/附庸行、王国地图描边
- [x] 游戏内加载（v0.1：日志确认编译、patch 无报错）
- [x] 联盟视图：附庸显示为宗主所在联盟的颜色；宗主无联盟时整个集团用宗主颜色
- [x] 政体特质组（城邦/王国/帝国/公国）+ 窗口标题显示 + 自动判定（手动指定后锁定，删掉政体特质即恢复自动）
- [x] 预览图、创意工坊介绍
- [x] 分封附庸：点击非首都城市，独立成原王国的附庸
- [ ] 游戏内实测 v0.2（联盟视图、政体、窗口标题）
- [x] 上传创意工坊（在游戏里由 NML 上传：模组列表里连点封面 8 次后等 3 秒）
- [ ] 政体的数值加成与行为差异

## 许可证

MIT，见 [LICENSE](LICENSE)。
