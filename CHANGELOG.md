# 更新日志 Changelog

每个版本的中文、英文说明。GitHub Release 和创意工坊的更新说明都从这里取。

## 0.9.0

### 中文

1. 新增「中央禁军」机制，由地方向中央供给兵员并由国家统一指挥
2. 优化经济系统，新增国库与军饷机制
3. 优化新机制下的战争 ai，分化中央禁军和地方军的决策逻辑
4. 引入「附庸贡赋」议案，附庸国可能会向宗主国上缴收入
5. 新增宗室继位：王室无人继承时，附庸里的宗室将会继承王位，封国并入宗主
6. 新增各政体领袖的称呼，如帝国的皇帝、公国的公爵、军事共和国的护国公
7. 优化宗主和附庸的领土划分机制，减少版图里的飞地和犬牙交错
8. 优化氏族机制，提高家族传承几率
9. 削弱强制同化政策的效果
10. 议会窗口现在对于所有国家都会显示议席半圆，取消原本对于城市数量的限制
11. 选举与任命窗口里的头像现在可以点击以查看人物详情
12. 修复强大国家无视联盟，直接威逼联盟成员称臣的问题

### English

1. New Central Guard mechanic: cities send troops up to the capital, and the kingdom commands them directly
2. Improved the economy with a new treasury and army pay system
3. Improved war AI under the new mechanics, with separate decision logic for the central guard and local armies
4. New Vassal Tribute bill: vassals may hand part of their income to their overlord
5. New royal-kin succession: when the royal line has no heir, royal kin in a vassal inherit the throne and their realm merges into the overlord
6. New titles for rulers of each government, such as Emperor for empires, Duke for duchies and Lord Protector for military republics
7. Improved how land is divided between overlords and vassals, reducing enclaves and tangled borders
8. Improved clans, making family lines more likely to carry on
9. Weakened the Forced Assimilation policy
10. The parliament window now shows the seat arc for every kingdom, no longer limited by city count
11. Portraits in the Elections & Offices window can now be clicked to view the person's details
12. Fixed strong kingdoms ignoring alliances and forcing alliance members to submit

## 0.8.0

### 中文

1. 引入动态议会与国家政策机制
2. 引入全新的选举机制，新增「选举与任命」窗口
3. 天命值现在会影响国家军队的战斗力
4. 新增沉疴机制，王朝平定大叛乱后会留下沉疴，缩短之后的王朝周期
5. 中兴密谋失败时会降低天命值
6. 王朝衰落期的城市忠诚改为随天命值下降而不是机械的随时间推移
7. 去除原版攻下城市后全国所有城市忠诚大幅提升的设定
8. 修复宗主的盟友可能会对宗主的附庸宣战的问题
9. 修复帝国进入崩溃期后，城市和附庸的不满会消失的问题
10. 修复打开模组设置页时刷「missing text」报错的问题

### English

1. New dynamic parliament and national policy system
2. New election system, with a new Elections & Offices window
3. Mandate now affects the strength of a kingdom's army
4. New Old Wounds mechanic: a dynasty that puts down a great rebellion is left with old wounds that shorten its later cycles
5. A failed Restoration plot now lowers Mandate
6. City loyalty during dynastic decline now falls with Mandate instead of mechanically over time
7. Removed the vanilla rule where conquering a city sharply raises loyalty in every city of the kingdom
8. Fixed an overlord's allies being able to declare war on the overlord's vassals
9. Fixed city and vassal discontent disappearing once an empire enters collapse
10. Fixed "missing text" errors spamming when opening the mod settings page

## 0.7.0

### 中文

1. 新增战争和谈机制，战争会按战果以和平、赔款、割让附庸或称臣结束，取代原来的投降
2. 引入天命值系统，王朝周期改为由天命值驱动，受城市忠诚、君主能力、附庸整合度、民生和战争胜负影响
3. 添加新的附庸类型：世袭行省；宗主分封出去的附庸默认直接成为行省或世袭行省
4. 国王新增「收归直辖」密谋，国土未达治理上限时会将行省的城市收归直接统治
5. 王国视图下，行省的颜色与宗主国保持一致
6. 优化附庸叛乱逻辑：附庸独立时退出随宗主参加的战争，宗主作战期间离心的附庸更容易起兵
7. 优化战争机制和附庸攻城后的分配逻辑
8. 重新设计并绘制模组相关图标
9. 修复宗主国规模缩小后不再经历王朝周期、仍能长期保有大量附庸的问题
10. 修复与其他模组同时使用时，好感计算出错导致报错刷屏的问题

### English

1. New war peace talks: wars now end by peace, reparations, ceding vassals, or submission depending on how they went, replacing the old capitulation
2. New Mandate system: dynastic cycles are now driven by Mandate, which is shaped by city loyalty, ruler ability, vassal integration, living conditions, and war outcomes
3. New vassal type: Hereditary Province; vassals granted out by an overlord now become provinces or hereditary provinces right away
4. New king plot: Direct Rule, which brings a province's cities under direct rule when the kingdom is below its governance limit
5. Provinces now share their overlord's color in the kingdom map view
6. Improved vassal rebellion: vassals leave their overlord's wars when they declare independence, and disloyal vassals are more likely to rise up while their overlord is at war
7. Improved war mechanics and how cities conquered by vassals are assigned
8. Redesigned and redrew the mod's icons
9. Fixed overlords that had shrunk no longer going through dynastic cycles and keeping many vassals for a long time
10. Fixed error spam from opinion calculation when used together with other mods

## 0.6.0

### 中文

1. 新增附庸整合度机制，附庸类型会随整合度变化而变化，并影响对应国家的外交行为
2. 添加新的附庸类型：行省
3. 国王新增「中兴」和「分封宗室」密谋
4. 优化联盟视图下的展示逻辑
5. 去除原版代码中的大量重复计算，约优化提高 20% 的性能
6. 修复开启原版「军队旁显示人数」选项后，未选中的军队不显示人数的问题
7. 修复右键盟友附庸国的城市时，军队直接向其宣战而不是前往驻扎的问题

### English

1. New vassal integration system: a vassal's type changes with its integration and shapes its kingdom's diplomacy
2. New vassal type: Province
3. New king plots: Restoration and Appanage
4. Improved how the alliance map view is displayed
5. Removed a large amount of redundant computation from the base game, improving performance by about 20%
6. Fixed unselected armies not showing their size when the base game's "Show Amount Near Army" option is enabled
7. Fixed armies declaring war on an ally's vassal instead of garrisoning when you right-click its city

## 0.5.0

### 中文

**政体改版**
- 取消城邦。政体分君主制和共和国两大类：君主制按国力显示王国或帝国；共和国分共和国、商人共和国、军事共和国
- 附庸按原来的政体称公国或自治领，独立后恢复
- 新国家按文化倾向决定政体，崇尚外交、财富的文化更容易建立共和国
- 不同政体各有倾向：共和国不轻易开战、城市更安定；商人共和国最不爱打仗；军事共和国好战成性；帝国更容易让邻国称臣

**政体变迁**
- 共和国由各家族轮流执政，执政任满换人
- 王室绝嗣时，君主国可能改制为共和国，不再分崩离析
- 鼎盛的大共和国可能被野心勃勃的执政改为帝国
- 王朝崩溃时将领可能夺权，建立新王朝或军事共和国

**外交**
- 大国会把叛乱的城市、超出治理能力的新领土分封为附庸
- 新密谋「威逼称臣」：强国的国王遣使威逼弱小的邻国俯首称臣
- 独立密谋、威逼称臣都可以在原版密谋编辑器里让国王强制发起
- 地图名牌和悬停提示的国名后面显示政体，可在设置里关闭
- 分封、投降、称臣、政体变迁记入左上角的世界日志

**军队**
- 缩远的地图上也能框选军队，右键下令：别国城市进攻，本国和友方城市驻扎，城外空地扎营；右键原驻地解除命令，Esc 取消选择
- 驻扎的军队真正守城：敌军进城就去打最近的那个；可以驻扎在宗主、附庸、盟友的城里协防
- 军队在国内任何一座城都能补员，下令时士兵立刻放下手头的事跟上
- 选中的军队在旗子上显示人数

### English

**Government overhaul**
- City-states are gone. Realms are now monarchies or republics: monarchies show as Kingdom or Empire by size; republics come as Republic, Merchant Republic or Military Republic
- Vassals are titled Duchy or Dominion depending on their former government, and get it back on independence
- New realms pick a government from their culture; cultures valuing diplomacy or wealth lean towards republics
- Each government has its own temper: republics are slow to go to war and have calmer cities; merchant republics hate war most; military republics are always spoiling for a fight; empires cow their neighbours more easily

**Governments change with history**
- Great families take turns leading a republic; leaders step down when their term ends
- When a royal line dies out, a monarchy may become a republic instead of falling apart
- A great republic at its height may be turned into an empire by an ambitious leader
- When a dynasty collapses, a general may seize power and found a new dynasty or a military republic

**Diplomacy**
- Great realms grant rebellious cities and conquests beyond their reach to new vassals
- New plot "Demand Tribute": the king of a mighty realm sends envoys to make a weak neighbour submit
- The independence and tribute plots can be forced from the vanilla plot editor
- Nameplates and tooltips show the government after the realm's name (can be turned off in settings)
- Grants, surrenders, submissions and changes of government are recorded in the world log

**Armies**
- Box-select armies on the zoomed-out map and right-click to order them: attack foreign cities, garrison your own or friendly cities, camp on open ground; right-click their post again to release them, Esc to deselect
- Garrisons actually defend their city, going after the nearest intruder; armies can garrison an overlord's, vassal's or ally's city
- Armies take in recruits in any city of their own realm, and soldiers drop what they are doing when ordered
- Selected armies show their headcount on the flag
