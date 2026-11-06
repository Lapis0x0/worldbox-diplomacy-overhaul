# Guard and Local Armies

Each kingdom's troops are divided into two parts. The **Guard** is paid for by the treasury and deployed by the kingdom as a whole, and it goes on campaign and rescues cities. **Local armies** are paid for by each city and mainly guard the borders. The split is set by parliament's [Army](parliament.md#military) policy.

When "Military overhaul" is turned off in the settings, armies behave as in vanilla: there is no Guard and no pay, cities send out their own armies, and the army command powers are removed.

## The Guard

- All troops in the capital belong to the Guard. In other cities, the Army policy decides what share is drafted into the Guard, and the rest are local troops.
- Guard soldiers are still registered in their home city. Each month every city adjusts its Guard headcount toward its quota: surplus troops return to the local army, and shortfalls are filled from local troops.
- Each Guard unit has at most 100 soldiers and is named "Kingdom Guard I", "Kingdom Guard II", and so on. New recruits join the nearest unit that is not full, and units stationed in the same city top each other up.
- On the map, a Guard unit's flag has a gold border and a gold star on dark cloth; local armies use the vanilla flag.
- When a city is taken by an enemy, the Guard troops it supplied scatter on the spot. Taking an enemy city therefore weakens the enemy's Guard.

### How the Kingdom Deploys the Guard

Each month the kingdom assigns a task to every Guard unit that has none, and each task runs until it ends.

| Task | Condition | Lasts until |
|---|---|---|
| **Rescue** | A city of the kingdom or a vassal is under pressure from an enemy army larger than its garrison | One month after the enemy leaves. If the city is lost, the task becomes a campaign to retake it |
| **Campaign** | There is a war and no city needs rescuing | The city is taken, a ceasefire is made, or the unit is worn down and sent to rest |
| **Rest** | A campaign or rescue wore the unit down | The unit refills at the nearest city of the kingdom, to 80% of its starting strength or for half a year |
| **Standby** | There is a war but the unit cannot attack, or there is no war and the unit is below half strength | The unit can take a new task at any time |

- Rescue sends the nearest units first, until the force is enough to overpower the enemy. A capital in danger has top priority and can recall units that are away on campaign.
- Campaigns prefer cities with a large population, a small garrison, and a short distance, and enemy capitals first. The kingdom concentrates its forces on one city and splits them only when they are enough.
- Units that the player has ordered are not deployed by the kingdom, which resumes control once the order is canceled.

### In Peace

- **Guarding the capital**: One Guard unit holds the capital.
- **Patrolling the border**: The other units with at least half strength are sent to the borders where the kingdom's camp (the kingdom, its overlord, vassals, and allies) meets other kingdoms. The stronger and closer the neighbor, the more units are sent. A unit camps at each stretch of the border for half a year in turn. If the home continent has no outside neighbor, units sail to borders overseas.
- **Relief**: After ten years at the capital, the unit that has patrolled longest returns to take over, and the unit that was at the capital goes out on patrol.
- Units below half strength return to the capital to refill, and go out on patrol once they are back above half strength. A kingdom with only one Guard unit keeps it at the capital.
- The army tooltip and the army window show each Guard unit's current duty.

## Local Armies

Local armies are the city lord's own troops. Under Noble Retinues, local armies act as in vanilla: once they reach 70% of their authorized strength, they attack the nearest enemy city. Under the other Army policies, local armies mainly defend.

- **City under siege**: The army returns and holds the city.
- **During war**: The army goes to the nearest border city and holds it. After taking an enemy city, the army makes that city its new post.
- **Sorties**: Local armies at the same post combine. When their strength is at least that of the garrison of a neighboring enemy city, they attack it together.
- **In peace**: The army returns to the area around its home city and acts as in vanilla.

## Armament

Armament is the standard gear that the kingdom issues to the Guard and that each city issues to its local troops: a weapon, plus a helmet, armor, and boots of the same material. The material starts at copper and rises one tier at a time: copper, bronze, iron, steel, mythril, adamantine.

| | Guard | Local troops |
|---|---|---|
| Who pays | The treasury, with the amount set by parliament's [Fiscal Priority](parliament.md#domestic) | The city's stockpile |
| Reserved first | The treasury's [reserve](treasury.md#where-money-goes) | Two years of local troop pay and the cost of building houses |
| Gear traits | More than civilian-made gear | More than civilian-made gear, fewer than the Guard |
| Materials | Bought from the kingdom's cities, and from caravans if not available | Only from the city's stockpile |

- **Troop types**: About a quarter are archers, and in a kingdom whose culture prefers bows, half are archers. Melee troops are issued the culture's preferred weapon: sword, axe, hammer, or spear.
- **Re-equipping**: Each year after pay is issued, the soldiers with the worst gear are re-equipped first. Gear better than the standard is left alone. Usable old gear in the stockpile is used first, and gear is bought only if there is none. When many recruits must be equipped after an army was wiped out and money is short, cheaper gear is issued first, and it is replaced with the standard over the following years.
- **Upgrading**: When soldiers making up 90% of the authorized strength are equipped to the current standard, the standard goes up one material tier the next year. Caravans do not sell mythril or adamantine, so these tiers are reached only when the kingdom's own stock is enough to re-equip the whole army.
- **Weapon bonus**: The higher the tier of a weapon made by the kingdom or a city, the more damage its wielder deals: +10% for copper, +50% for steel, and +80% for adamantine.
- **Repair**: The kingdom and the cities pay to repair the gear they issued. In peacetime this costs almost nothing.
- **Recycling**: Replaced gear is melted down and part of its cost is refunded. Gear left by soldiers who died defending a city is reclaimed by the city.

The kingdom window shows the Guard's equipment, and the city window shows the local troops' equipment. Both show the tier most soldiers actually wear, which is not necessarily the standard. Under Public Works Only, the Guard is not issued gear and forges its own as in vanilla.

## Pay

Both the Guard and local troops need pay. When pay cannot be paid, the army shrinks and fights less effectively; see [Treasury, Pay and Public Works](treasury.md#paying-the-troops).
