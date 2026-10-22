# Vassals

A vassal is a kingdom that is subordinate to another kingdom, its overlord. A vassal keeps its own king, cities, and armies, but it fights the overlord's wars and cannot declare war or make alliances on its own. Over time a vassal grows loyal and finally becomes a province of its overlord. It can also build up resentment and rise for independence when the overlord declines.

## Overlords and Vassals

- The kingdom window has an "Overlord" or "Vassals" row. A vassal's subtitle reads "Duchy · overlord name".
- On the map, a vassal's border line uses the overlord's color. A province uses the overlord's color for its fill too.
- An overlord cannot also be someone's vassal, and a kingdom that has vassals cannot become someone's vassal.
- When an overlord is destroyed, all its vassals are freed.

**Groups and blocs**: An overlord and its vassals form a group. Add the overlord's allies and it is a bloc. Kingdoms in the same bloc never go to war with each other. When the AI picks a rival, it counts the strength of the whole bloc, so attacking a vassal means facing its overlord and the overlord's allies. The alliance map is also shown by bloc.

### Wars Are Shared

- If the overlord is attacked, all vassals join the defense.
- If the overlord attacks, each vassal decides by its Integration whether to join. Provinces always join.
- If a vassal is attacked, the overlord comes to help.
- If a city rebels inside a vassal, the vassal fights to put it down. The overlord decides by the vassal's Integration whether to send troops. For a province it always does.
- When the overlord leaves a war, its vassals leave with it. Apart from putting down rebellions, a vassal is never dragged into a war the overlord is not in.

If an overlord and its vassal end up on opposite sides of the same war (for example, you use a power to make the overlord attack its vassal), the vassal relationship ends at once.

## Where Vassals Come From

| Source | How it happens | State when it becomes a vassal |
|---|---|---|
| Surrender | A war ends with the loser submitting, see [War and Peace Talks](war.md) | Resentful |
| Ceded in peace talks | The loser hands its own vassals to the winner | Estranged |
| Pressured into submission | A strong kingdom sends envoys to pressure a weak neighbor | Estranged |
| Rebellion turned into a grant | A city lord's rebellion succeeds and the overlord grants the city to him | Settled |
| Conquest turned into a grant | A large kingdom takes a city beyond what it can govern and grants it to locals | United (Province) |
| Royal appanage | The king grants outlying cities to royal kin | United (Hereditary Province) |

**Rebellion turned into a grant**: In vanilla, a successful city rebellion makes the lord found his own kingdom and start a war. Now the overlord may grant the city instead, getting a vassal rather than an enemy. At Prime this happens most of the time, in Decline rarely, and in Collapse never. The capital and cities next to the capital are not granted.

**Conquest turned into a grant**: When a kingdom has reached vanilla's city-governing limit, the next cities it takes are mostly not merged into it. They are given to a small neighboring vassal, or granted to a resident of the city to found a new vassal. A Military Republic never grants cities.

**Cities taken by a vassal**: A vassal fights alongside its overlord, and it can grow to at most 3 cities through war. Cities it takes beyond that go to the overlord or to other vassals, which makes the vassal a little unhappy.

**Pressure into submission**: The king of a strong kingdom can send envoys to pressure a neighboring weak kingdom. The target must meet all of these: it is not at war with anyone, it is not a vassal and has no vassals, its opinion of the strong kingdom is not negative, and the strong kingdom plus its vassals has at least 3 times the strength of the target's whole bloc (2 times for an Empire). A target with allies backing it is hard to intimidate. A declining kingdom will not do this.

**Royal appanage**: For a monarchy with more cities than it can govern, the king grants the cities farthest from the capital to a member of the royal house. It grants at most 3, and prefers his or her own children.

Land Grants policy changes how often grants happen, see [Parliament and Policies](parliament.md#domestic). Grants and pressure into submission can be turned off in the settings.

## Integration

Each vassal has an Integration value, showing how close it is to full assimilation. Integration appears as a kingdom trait. Hover over it for the exact number:

| Trait | State |
|---|---|
| Resentful | Old grudges against the overlord remain |
| Estranged | Loyal to its overlord in name only |
| Settled | Drawing closer to its overlord |
| United | One with its overlord, and a province |

Integration changes each year:

| Raises it | Lowers it |
|---|---|
| Overlord is in Renewal or Prime | Overlord is in Decline or Collapse, faster as the Mandate drops |
| Shares a border with the overlord | Separated from the overlord by another kingdom or by sea |
| Same culture and religion as the overlord | Not the same species |
| The two kings are from the same clan | Vassal has more than half as many cities as the overlord |
| The vassal's Fealty is Loyal Subject | The overlord has only a few cities of its own |
| Overlord wins a war | Overlord loses a war |
| | The overlord gave a Charter of Autonomy, or the vassal chose Seek Autonomy |

A Resentful vassal assimilates more slowly. In good times, a vassal that borders the overlord and shares its culture becomes a province in about 7 years if it came from a rebellion turned into a grant, about 18 years if it came from pressure into submission, and around 40 years if it came from surrender. After the overlord declines, a province falls back to an ordinary vassal in about ten years.

## Provinces

When Integration rises to United, the vassal becomes a province:

- **Hereditary Province**: The vassal's king is from the same clan as the overlord, such as royal kin granted a vassal. It keeps its royal house and succession as normal. The ruler's title is Prince.
- **Province**: Every other case. It becomes a republic, and parliament elects a Viceroy.

Both kinds always join the overlord's wars, rarely rebel in a Great Rebellion, and use the overlord's color on the map. If the overlord changes dynasty, or a hereditary province's own line dies out, the hereditary province becomes an ordinary province.

### Direct Rule

An overlord that has not reached its governing limit can start a "Direct Rule" plot to take back a province's cities one by one. Bordering provinces and provinces with high Integration come first. If there is enough room, the whole province merges into the overlord.

Royal Appanage and Direct Rule work against each other. When there are too many cities, they are granted out. When there is room, they are taken back from provinces. The overlord's directly held territory adjusts around the governing limit.

Once a year, the overlord and its provinces also tidy up their borders: an enclave surrounded by the other side may be transferred to it.

## Vassal Opinion

A vassal's opinion of its overlord includes these items, which you can see in the vanilla opinion breakdown:

- **Loyal to overlord**: A fixed bonus.
- **Overlord's fortunes**: A bonus when the overlord is at Prime and a penalty in Decline, larger as the Mandate drops.
- **Weak overlord**: A penalty when the overlord has fewer than 6 cities of its own.
- **Common Enemies** (vanilla): Counted only for a war where the vassal itself was attacked and the overlord came to help.
- **Charter of autonomy**, **Forced assimilation**, **Vassal tribute**: The overlord's policies, see [Parliament and Policies](parliament.md#diplomacy).

## Independence

A vassal's king can start an "Independence Plot", and if it succeeds, a war of independence against the overlord begins. Whether it can start depends on whether the overlord can hold it down:

- If the overlord is in Decline or Collapse, it can start at any time.
- Otherwise the overlord's strength relative to the vassal must be low enough. The higher the Integration, the weaker the overlord must be.

Whether it wants to start depends on the vassal's opinion of the overlord and the king's personality. The lower the opinion, the more it wants to rebel, and the deeper the resentment, the longer it holds a grudge. When the overlord is fighting a big war elsewhere, an Estranged vassal is more tempted to take the chance.

If the overlord wins the war of independence, the vassal becomes a vassal again. In every other outcome, the vassal becomes fully independent. In a Great Rebellion, vassals also rise one after another according to their Integration.

## Powers

- **Make Vassal**: Click the overlord, then the vassal.
- **Release as Vassal**: Click a non-capital city. It becomes a kingdom and a vassal of its former kingdom.
- **Incite Independence**: Click a vassal. It immediately starts a war of independence against its overlord.
