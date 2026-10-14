# 更新日志 Changelog

每个版本的中文、英文说明。GitHub Release 和创意工坊的更新说明都从这里取。

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
