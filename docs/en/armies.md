# Guard and Local Armies

Each kingdom's troops are divided into two parts. The **Guard** is paid for by the treasury and deployed by the kingdom as a whole, and goes on campaign and rescues cities. **Local armies** are paid for by each city and mainly guard the borders. The split is set by parliament's [Army](parliament.md#military) policy.

If you don't want these changes, turn off "Military overhaul" in the settings. Armies then behave as in vanilla: no Guard and no pay, cities send their own armies, and the army command powers are removed.

## The Guard

- All troops in the capital belong to the Guard. In other cities, the Army policy decides what share is drafted into the Guard, and the rest are local troops.
- Guard soldiers are still registered in their home city and are only assigned to serve in the Guard. Each month, every city adjusts its Guard headcount toward its quota. Extra troops go back to the local army, and shortfalls are filled from local troops.
- Each Guard unit has at most 100 soldiers and is named "Kingdom Guard I", "Kingdom Guard II", and so on. New recruits join the nearest unit that is not full. Small units stationed in the same city merge.
- On the map, a Guard army's flag has a gold border and a gold star on dark cloth. Local armies use the vanilla flag.
- When a city is taken by an enemy, the Guard troops it supplied scatter on the spot. Taking an enemy city therefore weakens the enemy's Guard.

### How the Kingdom Deploys the Guard

Each month the kingdom assigns tasks to idle Guard units, and each task runs until it ends:

| Task | When | Lasts until |
|---|---|---|
| **Rescue** | A city of the kingdom or a vassal is under pressure from an enemy army larger than its garrison | One month after the enemy leaves. If the city is lost, the task becomes a campaign to retake it |
| **Campaign** | There is a war and no city needs rescuing | The city is taken, a ceasefire is made, or the unit is worn down and sent to rest |
| **Rest** | A campaign or rescue wore the unit down | The unit refills at the nearest city of the kingdom, to 80% or for half a year |
| **Standby** | There is a war but the unit cannot attack, or there is no war | Ready for a new task at any time. In peace, one unit guards the capital and the rest guard the border next to the strongest neighbor |

- Rescue sends the nearest units first, until the force is enough to overpower the enemy. A capital in danger has top priority and can pull back units that are away on campaign.
- Campaigns prefer cities with a large population, a small garrison, and a short distance. Enemy capitals are more attractive. The kingdom first concentrates its forces on one city and splits up only when it has enough.
- Units you have ordered yourself are left alone by the kingdom. It takes them over again once the order is canceled.

## Local Armies

These are the city lord's own troops. Under Noble Retinues, local armies act as in vanilla: once they have gathered 70% of their strength, they attack the nearest enemy city. Under other Army policies, local armies mainly defend:

- **City under pressure from an enemy army**: Return and hold the city.
- **During war**: Go to the nearest border city and hold it. After taking an enemy city, that city becomes the new post.
- **Sorties**: Local armies at the same post combine. If their strength is at least that of the garrison of a nearby enemy city, they attack it together.
- **In peace**: Go back to near their home city and act as in vanilla.

## Armament

The kingdom equips the Guard, and each city equips its own local troops, with standard gear: a weapon, plus a helmet, armor, and boots of the same material. The material starts at copper and rises one tier at a time: copper, bronze, iron, steel, mythril, adamantine.

| | Guard | Local troops |
|---|---|---|
| Who pays | The treasury, following parliament's [Armament](parliament.md#military) policy | The city's stockpile |
| Reserved first | Two years of Guard pay | Two years of local troop pay and building costs |
| Gear traits | More than civilian-made gear | More than civilian-made gear, fewer than the Guard |
| Materials | Bought from the kingdom's cities, and from caravans if not available | Only from the city's stockpile |

- **Troop types**: About a quarter are archers, and in a kingdom whose culture likes bows, half are archers. Melee troops get the culture's preferred weapon: sword, axe, hammer, or spear.
- **Re-equipping**: Each year after pay is issued, the soldiers with the worst gear are re-equipped first. Gear better than the standard is left alone. Usable old gear is first looked for in the stockpile, and money is spent only if there is none.
- **Upgrading**: When most soldiers are fully equipped, the material goes up one tier the next year. Caravans do not sell mythril or adamantine. The kingdom reaches these tiers only if its own stockpile is enough to re-equip the whole army.
- **Repair**: The kingdom and cities pay to repair the gear they issued. In peacetime this costs almost nothing.
- **Recycling**: Replaced gear is melted down for part of its cost back. Gear left by soldiers who died defending a city is reclaimed by the city.

The kingdom window shows the Guard's current standard, and the city window shows the local troops' standard. Under Self-Equipped, the Guard is not issued gear and forges its own as in vanilla.

## Pay

Both the Guard and local troops need pay. If it cannot be paid, troops are discharged and fighting strength drops. See [Treasury and Pay](treasury.md#paying-the-troops).
