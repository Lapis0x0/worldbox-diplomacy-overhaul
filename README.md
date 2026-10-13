# 外交大修 Diplomacy Overhaul

<img src="preview.png" alt="外交大修" width="320">

为 WorldBox 加入更丰富的外交与战争玩法：附庸国、独立与投降、政体与政体变迁、王朝周期与大叛乱，以及一组直接指挥外交和军队的神力。

- Steam 创意工坊：https://steamcommunity.com/sharedfiles/filedetails/?id=3817246294
- 依赖：[NeoModLoader](https://github.com/WorldBoxOpenMods/ModLoader)

## 主要内容

- 附庸体系：宗主与附庸同进同退；大国会把叛乱的城市、管不过来的新领土分封为附庸，也会遣使威逼弱小的邻国称臣；附庸不满、宗主衰落时会密谋独立
- 政体：君主制的王国 / 帝国，共和国 / 商人共和国 / 军事共和国，附庸称公国或自治领；新国家按文化倾向决定政体，不同政体开战和治理各有倾向
- 政体变迁：共和国由各家族轮流执政；王室绝嗣时君主国可能改制为共和国而不分裂；鼎盛的大共和国可能被野心勃勃的执政改为帝国；王朝崩溃时将领可能夺权
- 王朝周期：大国经历新生、鼎盛、衰落、崩溃；衰落时附庸离心、城市不稳
- 大叛乱：国内多地同时揭竿而起，附庸纷纷独立；首都沦陷或天命已尽时爆发
- 战争：战败一方可能投降成为附庸；攻城时军队主动清剿守军，兵力悬殊时可以直接破城
- 指挥军队：框选军队后右键下令，进攻别国城市、驻守本国和友方城市、城外扎营，跨海自动调船
- 城镇管理：移交城市、吞并王国
- 性能优化：去掉原版的重复计算，大地图上帧率提高约 20%，游戏结果不变

所有功能都在底部「外交大修」Tab 里，各项数值可在 Tab 里的设置按钮中调整。数据保存在原版存档中，可以直接用于已有存档。

每个系统的具体规则和实现见 [docs/](docs/README.md)。

## 手动安装

不用创意工坊时，可以手动安装：

1. 先安装 [NeoModLoader](https://github.com/WorldBoxOpenMods/ModLoader)
2. 从 [Releases](https://github.com/Lapis0x0/worldbox-diplomacy-overhaul/releases) 下载最新的 `DiplomacyOverhaul-v*.zip`
3. 解压到游戏目录下的 `Mods` 文件夹，解压后应该是 `Mods/DiplomacyOverhaul/mod.json`。Steam 版的游戏目录可以在库里右键 WorldBox →「管理」→「浏览本地文件」找到
4. 启动游戏，NML 会自动编译加载

更新时删掉旧的 `DiplomacyOverhaul` 文件夹再解压新的。已经在创意工坊订阅的不要再手动安装，两份会冲突。

## 本地开发

- 把仓库放进（或软链接到）`worldbox/Mods/DiplomacyOverhaul`，NML 启动时会编译 `Code/` 下的源码；`bin/`、`obj/` 和以 `.` 开头的文件不会被编译或上传
- 本地编译检查：`dotnet build`（需要 .NET SDK，引用游戏目录里的程序集）
- 热重载：游戏开着时运行 `tools/hot-reload.sh`，改动会在一两秒内生效，不用重启游戏
  - 改 `Main.cs`、`DevReload.cs`、`mod.json`、`default_config.json` 仍需重启
  - `Core.Start` 必须能重复执行：资源用 `Core.Upsert` 注册，Tab 按钮会被清掉重建
- 发版：改 `mod.json` 的版本号，在 `CHANGELOG.md` 写好这个版本的中英文说明并提交，然后运行 `tools/release.sh`。脚本检查工作区、版本号、语言文件和编译后打 tag 推送，GitHub Actions 自动建 Release 并附上发布包（`tools/package.sh` 也可以单独打包）
- 上传创意工坊：在 NML 模组列表里连点封面 8 次后等 3 秒打开上传窗口；更新时在顶部填创意工坊物品 ID，空着会新建物品

## 许可证

MIT，见 [LICENSE](LICENSE)。
