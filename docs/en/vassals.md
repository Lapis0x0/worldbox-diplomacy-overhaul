# Vassals

A vassal is a kingdom that is subordinate to another kingdom, its overlord. A vassal keeps its own king, cities, and armies, but it fights the overlord's wars and cannot declare war or make alliances on its own. A vassal's Integration rises over time until it becomes a province of its overlord; when the overlord declines, a vassal may rise for independence.

## Overlords and Vassals

- A vassal's kingdom window has an "Overlord" row, and its subtitle reads "Duchy · overlord name". An overlord's kingdom window gets a "Vassals" row below Diplomacy, ordered from highest to lowest Integration. Hover over a banner to see the Integration, opinion of the overlord, and last year's tribute.
- On the map, a vassal's border line uses the overlord's color, and a province's fill uses it too.
- An overlord cannot also be someone's vassal, and a kingdom that has vassals cannot become someone's vassal.
- When an overlord is destroyed, all its vassals are freed.

**Camps**: An overlord, its vassals, and the overlord's allies form a camp. Kingdoms in the same camp never go to war with each other. When the AI picks a rival, it counts the strength of the whole camp, so attacking a vassal means facing its overlord and the overlord's allies. The alliance map is also shown by camp.

### Wars Are Shared

- When the overlord is attacked, all vassals join the defense.
- When the overlord attacks, each vassal decides by its Integration whether to join (at least a 30% chance). Provinces always join.
- When a vassal is attacked, the overlord comes to help.
- When a city rebels inside a vassal, the vassal fights to put it down. The overlord decides by the vassal's Integration whether to send troops, and a province's overlord always does.
- When the overlord leaves a war, its vassals leave with it. Apart from putting down rebellions, a vassal is never drawn into a war the overlord is not in.

If an overlord and its vassal end up on opposite sides of the same war (for example, you use a power to make the overlord attack its vassal), the vassal relationship ends at once.

## Where Vassals Come From

| Source | How it happens | Starting Integration |
|---|---|---|
| Surrender | A war ends with the loser submitting, see [War and Peace Talks](war.md) | Resentful |
| Ceded in peace talks | The loser hands its own vassals to the winner | Estranged |
| Demand Tribute | A strong kingdom demands submission from a weak neighbor | Estranged |
| Make Vassal power | The player names an overlord and a vassal | Estranged |
| Lost war of independence | A vassal's war of independence ends with the overlord winning | Resentful |
| Rebellion turned into a grant | A city lord's rebellion succeeds and the overlord grants the city to him | Settled |
| Conquest turned into a grant | A kingdom takes a city beyond its governing limit and grants it to locals | United (Province) |
| Royal Appanage | The king grants outlying cities to royal kin | United (Hereditary Province) |
| Release as Vassal power | The player turns a city into a vassal kingdom | United (Province) |

- **Rebellion turned into a grant**: In vanilla, a successful city rebellion makes the lord found his own kingdom and start a war. Now the overlord may grant the city instead, getting a vassal rather than an enemy. At Prime this happens most of the time, in Decline rarely, and in Collapse never. The capital and the cities next to it are not granted.
- **Conquest turned into a grant**: When a kingdom has reached vanilla's governing limit on cities, most cities it takes afterward are not annexed. They are given to a small neighboring vassal, or granted to a resident of the city to found a new vassal. A Military Republic never grants cities.
- **Cities taken by a vassal**: A vassal fights alongside its overlord, and it can grow to at most 3 cities through war. Cities it takes beyond that go to the overlord or to other vassals, and the vassal's Integration falls slightly.
- **Demand Tribute**: The king of a strong kingdom can demand submission from a bordering weak kingdom. The target must not be at war with anyone, must not be a vassal or have vassals, must have a non-negative opinion of the strong kingdom, and the strong kingdom plus its vassals must be at least 3 times as strong as the target's whole camp (2 times for an Empire). A target with allies is therefore hard to pressure. A declining kingdom does not do this.
- **Royal Appanage**: A monarchy with more cities than it can govern grants the cities farthest from the capital (at most 3) to members of the royal house, preferring the king's own children.

The Land Grants policy changes how often grants happen, see [Parliament and Policies](parliament.md#domestic). Grants and Demand Tribute can be turned off in the settings.

## Integration

Integration shows how close a vassal is to full assimilation. It appears as a kingdom trait, and hovering over it shows the exact value.

| Trait | State |
|---|---|
| Resentful | Old grudges against the overlord remain |
| Estranged | Distant from the overlord |
| Settled | Gradually drawing closer to the overlord |
| United | One with its overlord, and a province |

Integration changes each year.

| Raises it | Lowers it |
|---|---|
| Overlord is in Renewal or Prime | Overlord is in Decline or Collapse, faster as the Mandate drops |
| Borders the overlord | Does not border the overlord |
| Same culture and religion as the overlord | Not the same species |
| The two kings are from the same clan | The vassal has at least half as many cities as the overlord |
| The vassal's Fealty is Loyal Subject | The overlord has only a few cities of its own |
| Overlord wins a war | Overlord loses a war |
| | The overlord's Vassal Policy is Charter of Autonomy, or the vassal's Fealty is Seek Autonomy |

A Resentful vassal rises more slowly. In good times, a vassal that borders the overlord and shares its culture becomes a province in about 7 years if it came from a rebellion turned into a grant, about 18 years if it came from Demand Tribute, and about 40 years if it came from surrender. After the overlord declines, a province falls back to an ordinary vassal in about ten years.

## Provinces

When Integration reaches United, the vassal becomes a province.

- **Hereditary Province**: The vassal's king is from the same clan as the overlord, such as royal kin who were granted land. It keeps its royal house and succession as normal, and the ruler's title is Prince.
- **Province**: Every other case. It becomes a republic, and parliament elects a Viceroy.

Both kinds always join the overlord's wars, rarely rebel in a Great Rebellion, and use the overlord's color on the map. When the overlord changes dynasty, or a hereditary province's own line dies out, the hereditary province becomes an ordinary province.

### Direct Rule

An overlord that is not itself a vassal, is not in Decline or Collapse, and has not reached its governing limit can start a "Direct Rule" plot to take back a province's cities one by one, starting with bordering provinces and those with high Integration. If there is enough room, the whole province merges into the overlord.

Royal Appanage and Direct Rule balance each other: when there are too many cities, they are granted out, and when there is room, they are taken back from provinces. The overlord's directly held territory thus adjusts around the governing limit. Once a year, the overlord and its provinces also tidy up their borders, and an enclave surrounded by the other side may be transferred to it.

## Vassal Opinion

A vassal's opinion of its overlord includes these items, which appear in the vanilla opinion breakdown.

- **Loyal to overlord**: A fixed bonus.
- **Overlord's fortunes**: A bonus when the overlord is at Prime and a penalty in Decline, larger as the Mandate drops.
- **Weak overlord**: A penalty when the overlord has fewer than 6 cities of its own.
- **Common Enemies** (vanilla): Counted only in a war where the vassal itself was attacked and the overlord came to help.
- **Charter of autonomy**, **Forced assimilation**, **Vassal tribute**: The overlord's policies, see [Parliament and Policies](parliament.md#diplomacy).

## Independence

A vassal's king can start an "Independence Plot", and if it succeeds, a war of independence against the overlord begins.

- **Conditions**: The overlord is in Decline or Collapse, or the overlord is weak enough compared with the vassal. The higher the Integration, the weaker the overlord must be.
- **Willingness**: The lower the vassal's opinion of the overlord and the more warlike its king, the more likely it is to start. When the overlord is fighting a large war elsewhere, a vassal with low Integration is more likely to seize the chance.

If the overlord wins the war of independence, the vassal becomes a vassal again. Otherwise the vassal becomes fully independent. During a Great Rebellion, vassals also rise according to their Integration.

## Powers

- **Make Vassal**: Click the overlord, then the vassal.
- **Release as Vassal**: Click a non-capital city. It becomes a kingdom and a vassal of its former kingdom.
- **Incite Independence**: Click a vassal. It immediately starts a war of independence against its overlord.
