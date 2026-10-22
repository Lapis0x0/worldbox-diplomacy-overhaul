# Diplomacy Overhaul 外交大修

English | [中文](README.zh-CN.md)

<img src="preview.png" alt="Diplomacy Overhaul" width="320">

**Diplomacy Overhaul** adds politics, vassals and warfare to WorldBox that run on their own.

An expanding kingdom may grant its frontier to royal relatives and press its neighbors into submission. Families inside the kingdom sit in parliament and decide its policies, and the dynasty rises or falls with its wars and the mood of its cities. A republic may turn into an empire, and a kingdom at the height of its power may break apart in rebellion.

You can watch all of this unfold on its own, or step in yourself.

- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3817246294
- Requires: [NeoModLoader](https://github.com/WorldBoxOpenMods/ModLoader)
- Player guide: [all chapters](#player-guide)
- Fully localized in English and Chinese; follows the game's language setting

## More Than Conquest and Collapse

Great powers no longer have to rule everything directly. They grant newly won lands they can't govern, and cities taken back from rebels, to vassals, and send envoys to demand that weak neighbors submit. Overlords and vassals go to war together, expanding or holding off enemies side by side; when a rebellion breaks out inside a vassal, the overlord sends troops to help.

Vassal ties change over time. A vassal that has been loyal for long enough may grow into a Province of its overlord and finally be absorbed into direct rule; a fief held by a royal relative may become a Hereditary Province. A vassal that no longer wants to serve may plot for independence.

## Kingdoms Have Politics

Every kingdom has a parliament. Families win seats from the cities they hold, vote by their own stance, and set the kingdom's domestic, military and diplomatic policies. Each mix of policies forms a different **regime**, the system the kingdom actually runs by, and the parliament takes a matching name.

<img src="docs/images/parliament.png" alt="Parliament window" width="720">

A kingdom can be a Kingdom or an Empire, or a Republic, Merchant Republic or Military Republic. A new nation's government follows the leanings of its culture, and an existing one does not stay fixed.

Republics elect their leader at regular intervals. When a royal line dies out, a monarchy may become a republic instead of splitting apart; an ambitious leader of a republic at its prime may proclaim an empire, and generals may seize power from a collapsing dynasty.

You can follow the policy votes in parliament, see candidates, the line of succession and family lands in the Elections & Offices window, and set a family's political stance directly in the clan editor.

<img src="docs/images/election.png" alt="Elections & Offices window" width="720">

## The Rise and Fall of Dynasties

Even mighty kingdoms go through cycles of rise and decline.

A great power holds the **Mandate of Heaven**, which reflects the mood of its cities, its ruler, its vassals and the course of its wars, and affects how well its armies fight. While its rule is secure, the kingdom climbs toward its prime; as problems pile up, cities grow restless, vassals drift away, and the dynasty slides into decline.

When the capital falls or the Mandate runs out, a Great Rebellion may sweep the realm: many cities rise at once and vassals declare independence. Even after the rebellion is put down, the problems that built up over the years don't disappear overnight.

Decline is not the end, though. A capable ruler may plot a Restoration and try to turn the dynasty's fortunes around.

## War, Treasury and Armies

Wars end according to how they went. Once a war has gone far enough, both sides hold peace talks and settle with peace, reparations, ceded vassals or submission. Besieging armies hunt down defenders and can storm a city outright when they vastly outnumber them.

Military power now rests on finances. City governors send taxes to the national treasury, while each city pays for its local troops from its own funds. Soldiers from the capital and those sent up by other cities form the **central guard**, paid by the treasury and commanded by the kingdom, while local armies hold the borders.

War costs money. Unpaid armies fight worse, and troops that go unpaid for too long may be disbanded. The kingdom's treasury ledger shows where the money comes from and where it goes.

<img src="docs/images/treasury.png" alt="Treasury page" width="720">

## Step In Yourself

Besides watching kingdoms develop on their own, you can box-select armies and right-click to order them to attack enemy cities, garrison your own or friendly cities, or camp in the field. Boats are arranged automatically for crossings by sea.

The mod also adds a set of powers: make one kingdom a vassal of another, split a city off as a vassal, incite a vassal to independence, push a dynasty through its cycle, or start a Great Rebellion yourself. You can also send a kingdom's entire army against a city, transfer cities, or annex kingdoms.

## Settings and Compatibility

Everything is in the **Diplomacy Overhaul** tab at the bottom of the screen. Each module can be turned on or off separately, and values can be tuned in the settings.

- **Existing saves**: the mod's data is stored in vanilla saves, so it works with existing worlds.
- **Mobile**: works on mobile platforms that NeoModLoader runs on.
- **Performance**: removes redundant calculations in vanilla for about 20% higher frame rate on large maps, with identical results.
- **Error protection**: when a patch throws an exception, it is isolated and the affected module is switched off, so errors can't bring the game down.

## Player Guide

The [player guide](docs/en/README.md) explains how each system plays and its key numbers. Jump straight to a chapter:

| Guide | Contents |
|---|---|
| [Governments and Rulers](docs/en/government.md) | Monarchy and republic, titles, consul elections, changes of government, crowning an emperor, seizing power |
| [Parliament and Policies](docs/en/parliament.md) | How clans get seats, the four factions, the effect of every policy, how bills are proposed and voted on |
| [Rise and Fall of Dynasties](docs/en/cycle.md) | Mandate of Heaven, the four phases from Renewal to Collapse, Restoration, Great Rebellion |
| [Vassals](docs/en/vassals.md) | Where vassals come from, Integration and provinces, why vassals break away |
| [War and Peace Talks](docs/en/war.md) | When kingdoms go to war, war score, treaties, sieges |
| [Commanding Armies](docs/en/commands.md) | Giving orders to armies as the player: attack, garrison, camp |
| [Guard and Local Armies](docs/en/armies.md) | How a kingdom deploys its armies, the split between Guard and local armies, armament |
| [Treasury and Pay](docs/en/treasury.md) | Where money comes from, where it goes, what happens when pay is missed |
| [Plots](docs/en/plots.md) | The plots added by the mod and when kings start them |
| [Powers and Settings](docs/en/powers.md) | Every power and setting in the tab |
| [FAQ](docs/en/faq.md) | "Why did this happen?" |

## Manual Installation

If you don't use the Workshop:

1. Install [NeoModLoader](https://github.com/WorldBoxOpenMods/ModLoader)
2. Download the latest `DiplomacyOverhaul-v*.zip` from [Releases](https://github.com/Lapis0x0/worldbox-diplomacy-overhaul/releases)
3. Extract it into the `Mods` folder in the game directory so that you get `Mods/DiplomacyOverhaul/mod.json`. On Steam, find the game directory by right-clicking WorldBox in your library → Manage → Browse local files
4. Launch the game; NML compiles and loads the mod

To update, delete the old `DiplomacyOverhaul` folder before extracting the new one. If you already subscribe on the Workshop, don't install manually as well; the two copies conflict.

## Development

Full rules, implementation notes and the local development workflow are in the [developer docs](docs/dev/README.md) (Chinese).

## License

MIT, see [LICENSE](LICENSE).
