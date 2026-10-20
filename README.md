# Diplomacy Overhaul 外交大修

English | [中文](README.zh-CN.md)

<img src="preview.png" alt="Diplomacy Overhaul" width="320">

Deeper diplomacy and warfare for WorldBox: vassals, independence and peace talks, parliaments and elections, governments and how they change, dynastic cycles and great rebellions, treasuries and a central guard, plus a set of powers for directing diplomacy and armies yourself.

- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3817246294
- Requires: [NeoModLoader](https://github.com/WorldBoxOpenMods/ModLoader)
- Fully localized in English and Chinese; follows the game's language setting

## Features

- Vassals: overlords and vassals fight side by side. Large kingdoms grant rebel cities and lands they can't govern to vassals or royal relatives, and send envoys to pressure weak neighbors into submission. Vassals have integration: loyal ones become Provinces, royal fiefs become Hereditary Provinces, overlords absorb Provinces once they can govern them, and restless vassals plot for independence
- Parliament and policies: families win seats from the cities they hold, vote on bills by their stance, and enact domestic, military and diplomatic policies. A kingdom's regime is named after its mix of policies, and its parliament is called differently under each; a family's stance can be set in the clan editor. Republics elect their leader in parliament; the Elections & Offices window shows candidates, the line of succession and each family's lands
- Governments: monarchies as Kingdoms or Empires; Republics, Merchant Republics and Military Republics; vassals are Duchies or Dominions. Rulers are titled by government: King, Emperor, Consul, Duke and more. New nations pick a government from their culture, and each government has its own leanings in war and rule
- Changing governments: republican leaders are elected and stand again when their term ends; a monarchy whose royal line dies out may become a republic instead of splitting; an ambitious leader of a great republic at its prime may proclaim an empire; generals may seize power when a dynasty collapses
- Dynastic cycles: a great power's Mandate of Heaven rises and falls with its cities, ruler, vassals and wars, and affects how well its armies fight. Problems pile up, and putting down a great rebellion leaves old wounds. Dynasties go through Renewal, Prime, Decline and Collapse; in decline vassals drift away and cities grow unstable, and a strong ruler can plot a Restoration
- Great Rebellion: uprisings break out across the realm at once and vassals declare independence; triggered when the capital falls or the Mandate runs out
- Treasury and central guard: city governors send taxes to the national treasury, and cities pay for their own troops from their own gold. Troops from the capital and those sent up by other cities form a central guard, paid by the treasury and commanded by the kingdom, while local armies hold the borders. Unpaid armies fight worse, and long arrears lead to layoffs. The Treasury page in the kingdom window shows where the money flows and the ledger
- War: when a war has gone far enough, both sides hold peace talks and settle by the results with peace, reparations, ceding vassals or submission. Besieging armies hunt down defenders and can storm a city outright with overwhelming numbers
- Army command: box-select armies and right-click to attack foreign cities, garrison your own or friendly cities, or camp in the field; boats are arranged automatically across the sea
- City management: transfer cities, annex kingdoms
- Performance: removes redundant calculations in vanilla, about 20% higher frame rate on large maps with identical results

Everything lives in the Diplomacy Overhaul tab at the bottom, and values can be tuned from the settings button in that tab, where each module can also be turned off. Data is stored in vanilla saves, so it works with existing worlds.

See the [player guide](docs/en/README.md) for how each system works. Full rules and implementation notes are in the [developer docs](docs/dev/README.md) (Chinese).

## Manual installation

If you don't use the Workshop:

1. Install [NeoModLoader](https://github.com/WorldBoxOpenMods/ModLoader)
2. Download the latest `DiplomacyOverhaul-v*.zip` from [Releases](https://github.com/Lapis0x0/worldbox-diplomacy-overhaul/releases)
3. Extract it into the `Mods` folder in the game directory so that you get `Mods/DiplomacyOverhaul/mod.json`. On Steam, find the game directory by right-clicking WorldBox in your library → Manage → Browse local files
4. Launch the game; NML compiles and loads the mod

To update, delete the old `DiplomacyOverhaul` folder before extracting the new one. If you already subscribe on the Workshop, don't install manually as well; the two copies conflict.

## Development

- Put (or symlink) the repository at `worldbox/Mods/DiplomacyOverhaul`. NML compiles the sources under `Code/` at startup; `bin/`, `obj/` and files starting with `.` are not compiled or uploaded
- Local build check: `dotnet build` (needs the .NET SDK; references assemblies from the game directory)
- Hot reload: with the game running, run `tools/hot-reload.sh` and changes take effect within a second or two without restarting
  - Changes to `Core/Main.cs`, `Core/DevReload.cs`, `mod.json` and `default_config.json` still need a restart
  - `Core.Start` must be safe to run repeatedly: register assets with `Core.Upsert`; tab buttons are cleared and rebuilt
- Release: bump the version in `mod.json`, write this version's Chinese and English notes in `CHANGELOG.md` and commit, then run `tools/release.sh`. The script checks the working tree, version, locale files and build, then tags and pushes; GitHub Actions creates the Release with the package attached (`tools/package.sh` can also package on its own)
- Upload to the Workshop: run `tools/workshop.sh`, which uses SteamCMD to upload the files in the release package along with this version's change notes, leaving the title and description alone. It needs `brew install --cask steamcmd`, your Steam account name in `bin/steam_user`, and a one-time login in a terminal with `steamcmd +login <account> +quit`. The NML upload window (click the cover 8 times in the mod list and wait 3 seconds) also works, but its change-note box has a length limit and truncates long notes

## License

MIT, see [LICENSE](LICENSE).
