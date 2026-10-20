# 开销

模组逻辑的开销取决于两件事：被调得多频繁、每次多贵。两者相乘，再看它随国家、城市、氏族、附庸、单位的数量怎么增长。测试档里的国家不大，增长快的写法在测试档里看不出来，到了玩家的大世界才爆发：附庸贡赋的风险曾经在每股势力、每个选项里各算一遍，测试档里最大的帝国（13 个氏族、3 个附庸）起草一次议案要 12 秒。

写代码时按本文的两张表过一遍；`tools/perf-lint.sh` 从这两张表读规则，列出改动里新加的热入口和昂贵调用。

## 频率

### 每帧、每个单位

几千个单位每帧都可能走到，补丁里只能做常数时间的事：读字段、查一两次 `data`，不遍历任何列表。

| 原版方法 | 补丁 | 说明 |
|---|---|---|
| `BatchActors.b4_checkTaskVerifier`、`b6_0_updateDecision`、`b6_updateAI` | `Guard.cs` | 接管了单位循环，每个单位每帧三次 |
| `Actor.updateStats` | `Policies.cs`（单位伤害倍数） | 属性变脏的单位都要重算，跑在并行线程上，不能碰 Unity 接口和非线程安全的缓存 |
| `Actor.isAbleToSkipPriorityLevels` | `Orders.cs` | 每次决策 |
| 军队、攻城的行为（`BehCheckCityActorArmyGroup`、`BehFindRandomTile`、`BehCityActorCheckAttack`） | `Orders.cs`、`Guard.cs`、`Siege.cs` | 每个执行这一步行为的单位，行为之间有随机等待 |
| `DiplomacyManager.getRelation` | `Compat.cs` | 原版到处调，每次拼一个字符串作键 |

### 每帧、每座城或每个王国

| 原版方法 | 补丁 | 说明 |
|---|---|---|
| `City.updateCapture` | `Siege.cs` | 每座城每 0.1 秒 |
| `War.update` | `PeaceTalks.cs` | 每场战争每帧 |
| 城市 AI（`CityBehCheckAttackZone`、`CityBehCheckArmy`、`CityBehCheckLeader.checkFindLeader`）、`City.isOkToSendArmy` | `Orders.cs`、`Guard.cs`、`Offices.cs` | 每座城的决策节奏 |
| `KingdomBehCheckKing` | `Regime.cs`、`Succession.cs` | 每个王国的决策节奏 |
| `ArmyManager.update` | `Guard.cs` | 每帧；`Guard.Tick` 每秒一次，所有军队在同一帧 |
| `ZoneCalculator.applyMetaColorsToZone`、`TileZone.getAllianceOnZone` | `KingdomView.cs`、`AllianceView.cs` | 地图染色时每个区块一次 |
| `NameplateText.getStringForNameplate`、`NameplateLibrary.actionAlliance` | `KingdomView.cs`、`AllianceView.cs` | 屏幕上每个名牌 |

### 计算回调

原版自己决定什么时候调，次数往往比看上去多：

- **好感**（`OpinionAsset.calc`）：`getOpinion` 每调用一次，就把这对王国两个方向的全部好感项重算一遍，模组的约 8 项都在里面。原版 AI 挑战争目标、结盟、密谋时成批调用。
- **忠诚**（`LoyaltyAsset.calc`）：每座城最多每 3 个游戏秒算一次；城市列表、城市提示框、选中城市时强制重算，列表里每一行一次。

回调里只做常数时间的事；要算贵的东西，按年缓存（如 `Parliament.CityModifiers`）。

### 定期结算

`Clock.cs` 的每年、每月结算把所有国家放在同一帧里跑。单个国家几毫秒，乘上国家数就是一次顿卡。按国家算的东西要看最大的国家要多久：城市、氏族、附庸最多的那一个。

| 节奏 | 内容 |
|---|---|
| 每年 | 政体、国运、整合度、同化、国库、军械、议会（到期的国家开会：表决、生效、起草下一期） |
| 每月 | 附庸战争同步、禁军名额、禁军 AI、地方军 AI |

### 界面

- 王国页签里的窗口（议会、选举、国库）开着时每 0.5 秒刷新一次，算一遍数据指纹，变了才重建那一块
- 悬停提示每次移入算一次
- 点按钮时算一次

界面一次几十毫秒能接受，但每 0.5 秒都花几十毫秒就是持续掉帧。

## 新增热入口

改动里出现这些写法，说明加了新的入口，先按上面确定它属于哪一档频率。

| 写法 | 说明 |
|---|---|
| `[HarmonyPatch(` | 新补丁：查原版方法被谁调、多频繁 |
| `calc =` | 好感或忠诚回调 |
| `Run("` | `Clock.cs` 的定期结算 |
| `_shell = new KingdomTabWindow(` | 每 0.5 秒刷新的窗口 |

## 昂贵调用

单次贵，或者随世界规模增长。出现在上面任何一档频率里，或者出现在别的循环里，都要算一下乘起来是多少。

| 调用 | 代价 | 怎么用 |
|---|---|---|
| `getOpinion(` | 重算这对王国双向的全部好感项，约 0.2ms | 同一轮计算里同一对王国只取一次，不要放进按档位、按选项的循环 |
| `isOpinionTowardsKingdomGood(` | 内部调 `getOpinion` | 同上 |
| `Clans(` | 重建整个议会：所有城、所有氏族、每个氏族最多 50 个成员 | 一轮计算里建一次往下传 |
| `Vote(` | 议会表决里，掌权家族要把每股地方势力的不满全算一遍；`Regime.Vote` 要重建议会 | 不要按选项重复表决同一个议案 |
| `UnrestRisk(` | 每股地方势力 × 每项政策 | 只取决于政策和档位，同一轮起草里用 `RiskMemo` |
| `Discontent(` | 每项政策 × 每档 | — |
| `Options(` | 每个选项一次表决 | 只在起草时用 |
| `Draft(` | 每股势力 × 每个选项 × 表决：氏族数的三次方 | 只在开议会时 |
| `GetVassals(` | 遍历全部王国，分配一个列表 | 循环里要用时先取一次 |
| `HasVassals(` | 遍历全部王国 | 同上 |
| `BlocPower(` | `GetVassals` 加每个附庸的实力 | 同上 |
| `BlocCities(` | `GetVassals` 加每个附庸的城数 | 同上 |
| `IsEmpire(` | 内部调 `BlocCities` | 同上 |
| `Camp.Power(` | 遍历全部王国 | 要每个阵营的数时用 `Camp.Warriors()` 一次算全 |
| `Camp.Population(` | 遍历全部王国 | 同上 |
| `Camp.Warriors(` | 遍历全部王国 | 同上 |
| `.units)` | 遍历一个王国、城市或家族的全部单位 | 不要放进按城、按军队的循环 |
| `getUnits()` | 同上 | 同上 |
| `World.world.units` | 遍历全世界的单位 | 一轮最多一次 |
| `World.world.kingdoms` | 遍历全部王国 | 不要放进按王国的循环（王国数的平方） |
| `World.world.armies` | 遍历全部军队 | 不要放进按城的循环，先按城建索引（见 `GuardAI.Pressure`） |
| `World.world.cities` | 遍历全部城市 | 同上 |
| `recalculateNeighbourCities(` | 原版重算一座城的邻城 | 不要在广度搜索里每个节点调 |
| `Mainland(` | 从首都广度搜索全部本土城市 | 一轮算一次往下传 |
| `distanceBetweenKingdom(` | 原版按两国城市两两比较 | 先用便宜的条件筛掉候选 |
| `setStatsDirty(` | 单位下一帧重算全部属性 | 对全国单位调用会造成一次属性重算高峰，只在数值真的变了时调 |
| `NearestDefender(` | 遍历守方全国单位 | 只给队长用 |
| `NearestIntruder(` | 遍历所有交战国的全部单位 | — |
| `LM.AddToCurrentLocale(` | 拼字符串、改语言表 | 不放在每帧的路径上 |
