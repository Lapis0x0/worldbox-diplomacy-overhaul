# Governments and Rulers

Every kingdom is either a **monarchy** or a **republic**. A monarchy is ruled by a royal house from generation to generation. A republic has no royal house; its consul is elected by parliament and re-elected at fixed intervals. The government shown also depends on the kingdom's size and on whether it is a vassal, and the ruler's title follows the government.

## Governments and Titles

The government is shown as a kingdom trait, and also after the kingdom name and in the kingdom window subtitle. The government after the kingdom name can be turned off in the settings.

| Government | Which kingdom | Ruler title |
|---|---|---|
| Kingdom | A monarchy with 5 cities or fewer | King |
| Empire | A monarchy with 6 cities or more, counting its vassals | Emperor |
| Republic | An ordinary republic | Consul |
| Merchant Republic | A republic whose culture has Golden Rule | Consul |
| Military Republic | A republic founded when generals seized power | Lord Protector |
| Duchy | A monarchy that is a vassal | Duke |
| Dominion | A republic that is a vassal | Consul |
| Province | A vassal fully merged with its overlord | Viceroy |
| Hereditary Province | A province with the same royal house as its overlord | Prince |

Becoming a vassal only changes the title, and independence restores it. For provinces, see [Vassals](vassals.md#provinces). Everywhere vanilla says "King" in the interface and the world log, the ruler's actual title is used, and a republic's crown icon is replaced by a golden olive wreath. In the kingdom window, Past rulers lists each ruler with the title they held at the end of their reign, so you can still tell who was emperor after an empire declines.

The titles of King, Emperor, and Consul also change with the [regime](parliament.md#regime-names), for example Khan, Hegemon, and Princeps Senatus. The titles of Lord Protector and of vassals do not change. When parliament is turned off, titles do not change with the regime.

## Government Tendencies

Governments differ in how often they go to war and whom they dare to attack. Kingdom and Empire behave as in vanilla.

| Government | Minimum gap between wars | Rivals they will challenge | Other |
|---|---|---|---|
| Kingdom, Empire | 5 years | Not stronger than themselves | Empires are more likely to demand tribute from neighbors |
| Republic | 10 years | Not stronger than themselves | City loyalty +5 |
| Merchant Republic | 15 years | Less than half their own strength | City loyalty +5 |
| Military Republic | 2 years | At most 1.25 times their own strength | Cities taken are annexed, not granted |
| Vassal | n/a | n/a | Never starts wars on its own |

Strength is counted for the whole camp: overlord, vassals, and allies all count. Parliament's [Foreign Policy](parliament.md#diplomacy) adjusts the gap and the range of rivals further.

## Government of New Kingdoms

When a kingdom is founded, its chance of being a republic depends on its culture:

| Kingdom's culture | Chance |
|---|---|
| Diplomatic Ascension, Golden Rule | 50% |
| Fame's Crown | 25% |
| Other | 10% |

After that, the government changes only through the historic events below and parliament votes.

## Consuls of Republics

- **Election**: The consul is elected by parliament. There are at most three candidates: the current consul and the city lords with the highest ability and renown from other clans. Each clan gives all its seats to the candidate closest to its own stance, and the candidate with the most votes wins.
- **Term**: 12 years, after which a new election is held. A consul can be re-elected. If the consul dies, an election is held at once.
- The capital's seats belong to the consul's clan, so the sitting consul has the advantage at re-election.
- A republic has no royal house and does not split apart when a line dies out.

## The Elections & Offices Window

The third tab on the left of the kingdom window.

- **Republic**: Shows the current consul, the years until the next election, the expected vote for each of the three candidates, and whom each clan votes for. "Hold election" holds an election at once on the expected result. "Appoint" names the selected candidate consul without a vote.
- **Monarchy**: Shows the king and the line of succession; the first in line takes the throne when the king dies. Kings of the royal house who rule vassals are listed after the members of the kingdom's own royal house.
- **Domains**: A map with each city colored by the clan that holds it. Hover over a region to see the cities the clan holds and its seats.

## Historic Events

When "Historic upheavals" is on in the settings (the default), the events below can happen, and each is recorded in the world log. Parliament can also change the government, which needs three quarters of the seats; see [Parliament and Policies](parliament.md#domestic).

### Inheriting the Throne

When a king dies and nobody in the kingdom's own royal house can inherit, a king of the same royal house who rules a vassal can return to take the throne, for example a prince who was earlier granted land. His vassal is merged into the overlord.

### Extinct Royal Line

In vanilla, when the royal house dies out and no heir can be found, a multi-city kingdom splits into many small kingdoms. With the parliament on, the kingdom no longer splits. The throne stands empty instead: the clan with the most seats eyes it, and the clans struggle over it in parliament, most of them unwilling to see a rival house crowned. Parliament governs in the meantime, and the longer it drags on, the more the kingdom leans toward a republic. In the end, either parliament elects a consul and the kingdom becomes a republic, or that clan's head takes the throne and founds a new dynasty. While the throne is empty the kingdom has no ruler, city loyalty drops, and a long vacancy drags the dynasty into decline.

With the parliament off, a monarchy has a chance to become a republic instead, with parliament electing a consul, and the kingdom stays whole.

| Kingdom's culture | Chance of switching |
|---|---|
| Diplomatic Ascension, Golden Rule | 80% |
| Other | 40% |

A kingdom that does not switch splits as in vanilla.

### Republic Crowns an Emperor

A republic with 6 or more cities (counting its vassals) that is not a vassal and is at its [Prime](cycle.md) may see its consul crown himself or herself emperor. The consul's clan becomes the royal house and the kingdom becomes an Empire. Ambitious, Bloodlust, and Evil consuls are the most likely to do it. Content, Honest, Pacifist, and Peaceful consuls never do.

With the parliament on, the consul's clan reaches for the crown, opening the Talk of a Crown situation, and the clans struggle over it in parliament for several years. Conservative clans back the empire and radical clans defend the republic. A kingdom at its height lends the idea weight, the longer the debate drags on the less likely it succeeds, and once the kingdom leaves Prime the talk gradually dies down. If the consul's clan loses the next election, or the parliament keeps the republic, nothing comes of it. If it succeeds, the consul is crowned. With the parliament off, the consul crowns himself or herself at once.

### Mutiny Crisis

Two things can set off a mutiny crisis:

- Each year, a kingdom in [Collapse](cycle.md) has a 10% chance (20% if the culture has Warriors Ascension) of facing a mutiny crisis, led by the army captain with the most kills.
- When the Guard is [unpaid](treasury.md#paying-the-troops), the longer the arrears, the more likely trouble breaks out, led by the Guard captain with the most kills.

With the parliament on, the leading captain gathers his or her troops and presses the court (when the Guard is unpaid, by stirring up the troops), backed by his or her clan; a captain without a clan founds one. The royal house is the one being pressed, and the clans struggle over it in parliament. Martial clans back the mutiny, clans far from the capital are wary of it, clans on good terms with the royal house defend it, and clans that bear it a grudge welcome it. The larger the captain's share of the kingdom's troops, the longer the Guard has gone unpaid, and the deeper the disarray of Collapse, the more likely a mutiny breaks out; the longer it drags on, the more its support melts away. The crisis is decided within months to a year, and is defused if the captain dies along the way.

Each end has three possible outcomes. When the crisis reaches an end, one is drawn according to the situation at that moment:

| Crisis Defused | Mutiny Breaks Out |
|---|---|
| Stand Down: the matter is dropped. More likely the steadier the troops | Seize the Throne: the captain takes power. More likely the more troops he or she holds, and more so in Collapse |
| Crushed: the captain is executed and his or her clan resents the royal house. More likely the stronger the clans against the mutiny | Deposition: the ruler is deposed and killed, and a successor raised. More likely the more the backing clans resent the royal house |
| Desertion: the captain's troops disband and go home. More likely the longer the arrears | Plunder: the capital is looted to pay the arrears. More likely the longer the arrears |

When the captain seizes the throne, his or her character decides what follows:

- If the general is ambitious, he or she deposes the ruler, becomes king, and founds a new dynasty. The dynastic cycle returns to Renewal.
- If the general is not ambitious, the generals take power together and form a Military Republic, with the general as Lord Protector.

When no captain can lead it, or the parliament is off, the captain with the most kills in a collapsing kingdom takes power at once, and unpaid troops do not cause a mutiny crisis.

### Power Behind the Throne

When a monarchy gets a new ruler (other than at its founding), the weaker the new ruler (the lower their stewardship plus intelligence), the more likely a powerful figure takes over the court: one roll is made by how weak the ruler is. An ordinary ruler never faces it; the weakest tenth of new rulers have about a one-in-four chance. The power behind the throne is either the head of the largest royal branch or the head of the largest clan outside the royal house, whichever has more renown: a royal branch head rules as regent, an outsider holds the reins as an overmighty lord. The royal house is the one sidelined. Conservative clans uphold the rightful ruler, radical clans care more for strength than for title, martial clans lean toward the regent, the royal branches side with the ruler against an outsider, and every clan's feelings toward both sides count too. An ambitious royal regent is far more likely to reach for the crown, while one without ambition usually hands power back. The longer the ruler reigns, the firmer their footing. The situation advances yearly and often takes over a decade to settle.

| Personal Rule | Power Changes Hands |
|---|---|
| Power Returned: the regent steps down with grace. More likely the more capable the ruler | Usurpation: the royal regent deposes the ruler and takes the throne; the royal house stays the same |
| Dismissal: the ruler takes back power and drives the regent from court, and the regent's side resents the royal house from then on. More likely the stronger the clans against the regent | The Crown Changes Hands: the outsider takes the throne and his or her clan becomes the royal house, founding a new dynasty |

If the ruler being overshadowed dies, or the regent dies or leaves the kingdom, the situation ends.

## Changing Government by Hand

In the vanilla trait editor, give the kingdom a different government trait. Choosing a republic type makes it a republic; choosing Kingdom or Empire makes it a monarchy. The title shown still follows the number of cities: a monarchy with only 2 cities that is given Empire still shows as Kingdom. Giving a vassal Province or Hereditary Province makes it a province; choosing Duchy or Dominion takes it out of province status.
