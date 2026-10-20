# Governments and Rulers

Every kingdom is either a **monarchy** or a **republic**. A monarchy is ruled by a royal house from generation to generation. A republic has no royal house. Its consul is elected by parliament and re-elected at fixed intervals. The government shown also depends on the kingdom's size and on whether it is a vassal, and the ruler's title changes with it.

## Governments and Titles

The government is shown as a kingdom trait. It also appears after the kingdom name and in the kingdom window subtitle.

| Government | Which kingdom | Ruler title |
|---|---|---|
| Kingdom | A monarchy with 5 cities or fewer | King |
| Empire | A monarchy with 6 cities or more | Emperor |
| Republic | An ordinary republic | Consul |
| Merchant Republic | A republic whose culture has Golden Rule | Consul |
| Military Republic | A republic founded when generals seized power | Lord Protector |
| Duchy | A monarchy that is a vassal | Duke |
| Dominion | A republic that is a vassal | Consul |
| Province | A vassal fully merged with its overlord | Viceroy |
| Hereditary Province | A province with the same royal house as its overlord | Prince |

Becoming a vassal only changes the title. It reverts automatically on independence. For provinces and hereditary provinces, see [Vassals](vassals.md#provinces).

Everywhere vanilla says "King", the ruler's actual title is used: windows, tooltips, loyalty and opinion breakdowns, and the world log messages for accession and death. If you don't want the government shown after kingdom names, turn it off in the settings.

## Government Temperament

Governments differ in how often they go to war and how strong a rival they dare to attack. Kingdom and Empire behave like vanilla.

| Government | Wait after a war | Rivals they dare to attack | Other |
|---|---|---|---|
| Kingdom, Empire | 5 years | Not stronger than themselves | Empires are more likely to pressure neighbors into submission |
| Republic | 10 years | Not stronger than themselves | City loyalty +5 |
| Merchant Republic | 15 years | Less than half their own strength | City loyalty +5 |
| Military Republic | 2 years | At most a quarter stronger than themselves | Keeps the cities it takes and does not grant them as vassals |
| Vassal | n/a | n/a | Never starts wars on its own |

Strength is counted for the whole bloc: overlord, vassals, and allies all count. The parliament's [Foreign Policy](parliament.md#diplomacy) adjusts these further. Roughly, a Kingdom fights a war every dozen years or so, a Merchant Republic only every twenty-odd years, and a Military Republic in under ten years.

## Government of New Kingdoms

When a kingdom is founded, the game rolls once:

| Kingdom's culture | Chance of being a republic |
|---|---|
| Diplomatic Ascension, Golden Rule | 50% |
| Fame's Crown | 25% |
| Other | 10% |

After that, the government changes only through the historical events below and parliament votes. It does not change with culture.

## Consuls of Republics

- **Election**: The consul is elected by parliament. There are at most three candidates: the current consul, plus the city lords from other clans with the best mix of ability and renown, with renown weighing more. Each clan gives all of its seats to the candidate closest to its own stance. The candidate with the most votes wins.
- **Term**: 12 years. After that a new election is held. A consul can be re-elected without limit. If the consul dies, an election is held at once.
- The capital's seats belong to the consul's clan, so the sitting consul has the advantage at re-election.
- A republic has no royal house and does not split apart when a line dies out.

## The Elections & Offices Window

The third tab on the left of the kingdom window.

- **Republic**: Shows the current consul, the years until the next election, the expected vote for each of the three candidates, and who each clan votes for. You can **hold an election** right away, or click a candidate to **appoint** him or her consul without a vote.
- **Monarchy**: Shows the king and the line of succession. The first in line takes the throne when the king dies. Kings of the royal house who rule vassals are also listed further down.
- **Domains**: A map of the kingdom on the right, with each city colored by the clan that holds it. Hover over a region to see which cities the clan holds and how many seats it has.

## How Governments Change

When "Historic upheavals" is on in the settings (the default), the events below can happen. All of them are recorded in the world log. Parliament can also change the government, but it needs three quarters of the seats. See [Parliament and Policies](parliament.md#domestic).

### Inheriting the Throne

When a king dies and nobody in the kingdom's own royal house can inherit, a king of the same royal house who rules a vassal can return to take the throne. An example is a prince who was earlier granted a vassal kingdom. His vassal is merged into the overlord.

### Extinct Royal Line: Switch to Republic

In vanilla, when the royal house dies out and no heir can be found, a multi-city kingdom splits into many small kingdoms. Now a monarchy has a chance to let parliament elect a consul instead. It becomes a republic and stays whole:

| Kingdom's culture | Chance of switching |
|---|---|
| Diplomatic Ascension, Golden Rule | 80% |
| Other | 40% |

If it does not switch, it splits as in vanilla.

### Republic Crowns an Emperor

A republic with 6 or more cities at its [Prime](cycle.md) may see an ambitious consul crown himself or herself emperor. The consul's clan becomes the royal house and the kingdom becomes an Empire. Ambitious, Bloodlust, and Evil consuls are the most likely to do it. Content, Honest, Pacifist, and Peaceful consuls never do. In a republic with an ordinary consul, about a quarter crown an emperor during a whole Prime phase.

### Coup in Times of Collapse

A kingdom in [Collapse](cycle.md) has a 10% chance each year of being taken over by the general with the most kills in the kingdom (20% if the culture has Warriors Ascension):

- If the general is ambitious, he or she deposes the ruler, becomes king, and founds a new dynasty. The dynastic cycle returns to Renewal.
- If the general is not ambitious, the generals take power together and form a Military Republic with the general as Lord Protector.

## Changing Government by Hand

In the vanilla trait editor, give the kingdom a different government trait. Choosing a republic type makes it a republic. Choosing Kingdom or Empire makes it a monarchy. The title still follows the number of cities: a monarchy with only 2 cities that is given Empire still shows as Kingdom. Giving a vassal Province or Hereditary Province makes it a province at once. Choosing Duchy or Dominion takes it out of province status.
