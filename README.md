# 外交大修 Diplomacy Overhaul

WorldBox 的 NeoModLoader 模组：附庸国、独立战争、战败投降与政体系统。

- Steam 创意工坊：https://steamcommunity.com/sharedfiles/filedetails/?id=3817246294
- 依赖：[NeoModLoader](https://github.com/WorldBoxOpenMods/ModLoader)

## 本地开发

- 游戏读取的是 `worldbox/Mods/DiplomacyOverhaul`，它是指向本仓库的符号链接
- 本地编译检查：`dotnet build`（需要 .NET SDK，引用游戏目录里的程序集）
- 游戏内由 NML 编译 `Code/` 下的源码；`bin/`、`obj/` 和以 `.` 开头的文件不会被编译或上传
- 热重载：游戏开着时运行 `tools/hot-reload.sh`，它把 `Code/` 编译到 `bin/hot/` 并写入 `bin/reload.signal`；模组每秒检查一次信号，停掉旧版本 `Core`、载入新 dll 并启动，同时重载 `Locales/`
  - 需要重启游戏的改动：`Main.cs`、`DevReload.cs`、`mod.json`、`default_config.json`（新增设置项）
  - `Core.Start` 必须可重复执行：资源用 `Core.Upsert` 注册，Tab 按钮会被清掉重建

## 进度
- [x] 附庸关系、外交规则、独立密谋、战败投降
- [x] 外交大修 Tab、王国窗口宗主/附庸行、王国地图描边
- [x] 游戏内加载（v0.1：日志确认编译、patch 无报错）
- [x] 联盟视图：附庸显示为宗主所在联盟的颜色；宗主无联盟时整个集团用宗主颜色
- [x] 政体特质组（城邦/王国/帝国/公国）+ 窗口标题显示 + 自动判定（手动指定后锁定，删掉政体特质即恢复自动）
- [x] 预览图、创意工坊介绍
- [x] 分封附庸：点击非首都城市，独立成原王国的附庸
- [x] 指定进攻 / 举国进攻：直接改写城市进攻目标，未交战则宣战，跳过 70% 兵力门槛；跨海时立即派最近的空闲运输船，没有就在港口造一艘
- [x] 指定进攻 / 举国进攻第二下点自己国家的城市：军队前往该城（跨海同样调船），到达后交还原版 AI
- [x] 选择目标时的箭头与区域高亮
- [ ] 游戏内实测 v0.2（联盟视图、政体、窗口标题）
- [x] 上传创意工坊（在游戏里由 NML 上传：模组列表里连点封面 8 次后等 3 秒）
- [ ] 政体的数值加成与行为差异

## 许可证

MIT，见 [LICENSE](LICENSE)。
