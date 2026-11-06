# Parliament and Policies

Parliament is the body through which a kingdom decides its policies, and every kingdom has one, monarchies included. Clans (vanilla clans) get seats in proportion to the cities they hold, and vote on each policy: taxation, army organization, land grants, and foreign policy. Parliament is the second tab on the left of the kingdom window. When a kingdom is selected on the map, there is also a parliament button at the bottom right.

## Seats

- Each city gives its lord's clan seats. The bigger the city, the more seats; a city of 100 people gives about 20. The capital's seats belong to the royal house, or to the consul's clan in a republic.
- In a kingdom with one city, the royal house holds all the seats. As the kingdom expands, the local clans' seats together can outnumber the royal house's.
- **Cadets**: In a monarchy, when members of the royal house are lords of outlying cities, those cities' seats count separately as the cadets. Royal cities near the capital still belong to the royal house. The cadets support the dynasty and never vote to abolish the monarchy, but they do not want to hand over their troops and money, and want land grants most of all. A republic has no cadets.
- Children join the clan of the parent who passes on the succession. A person still belongs to a clan if a king or city lord appears among the last three generations of ancestors. If no one has held office for over a hundred years, that branch has faded.

## Stances and Factions

Each clan has a position on two axes.

- **Conservative and Radical**: Old clans, and chiefs who are older, Content, or Honest, lean conservative. New clans, and young or ambitious chiefs, lean radical.
- **Central and Local**: Clans near the capital, and clans with Paranoid chiefs, lean central. Clans far from the capital that want to stand on their own lean local. The fewer cities a kingdom has, the less distance from the capital matters.

When a new chief takes over, the clan's stance moves only part of the way toward his or her views.

Clans can carry Politics traits: Conservative, Radical, Centralist, and Localist. A clan with one of them always stays on that side, and its lineage and chief only decide how far. They can be added in the clan window's trait editor.

The two axes make four factions, and clans in the same faction share a color.

| | Central | Local |
|---|---|---|
| Conservative | Royalists (blue): old clans at court | Aristocrats (teal): old clans in the provinces |
| Radical | Reformists (red): new powers at court | Republicans (orange): new powers in the provinces |

In a republic the Royalists are called Senators and the Republicans are called Autonomists; the colors stay the same.

## Policies

Policies are on three pages: Domestic, Military, and Diplomacy. The options of each policy lie along one axis, with the conservative or local end on one side and the radical or central end on the other. Vassal Policy and Vassal Tribute exist only for kingdoms with vassals. Fealty exists only for vassals.

### Domestic

**Government**: Monarchy or Republic. Changing to Republic removes the current ruler and parliament elects a consul. Changing to Monarchy makes the current consul's clan the royal house. This policy needs three quarters of the seats.

**Appointments**: Who takes over a city when its lord's seat is vacant.

| Option | Who takes over |
|---|---|
| Hereditary Office | A member of the previous lord's clan |
| Royal Kin First | A member of the royal house first (the vanilla behavior). In a republic this is called Seniority |
| Meritocracy | The ablest person in the kingdom, regardless of birth |

The lord's clan decides who holds the city's seats, so Appointments reshapes parliament. Hereditary Office lets local clans keep their cities, Royal Kin First grows the cadets, and Meritocracy keeps producing new clans.

**Land Grants**:

| Option | Effect |
|---|---|
| Feudalism | Rebel cities and conquered cities are more often granted as vassals, and royal kin are granted land more often |
| Limited Grants | Land is granted only when it cannot be governed |
| Direct Rule | Land is rarely granted, and provinces are more often taken back under direct rule |

**Taxation**: The share of income that city lords send up. See [Treasury, Pay and Public Works](treasury.md).

| Option | Sent up |
|---|---|
| Tax Farming | 25% |
| Shared Revenue | 50% |
| Central Collection | 75% |

Elven kingdoms start with Tax Farming, sheep kingdoms start with Central Collection, and all others start with Shared Revenue. This policy replaces vanilla's Low Tribute and High Tribute traits.

**Fiscal Priority**: The most that the money beyond the treasury's reserve may go to the Guard's equipment and to [public works](treasury.md#public-works). Both are spent only when needed, and money not spent stays in the treasury. For equipment see [Armament](armies.md#armament).

| Option | Effect |
|---|---|
| Public Works Only | All spare money goes to public works. The Guard forges its own gear, as in vanilla |
| Public Works First | Most spare money goes to public works. Missing gear is issued to the Guard, with no upgrades |
| Balanced Funding | Split evenly between public works and arms. Once soldiers making up 90% of the Guard's authorized strength are fully equipped, the material goes up one tier |
| Armament Only | All spare money goes to equipping the Guard |

**Cultural Policy**:

| Option | Effect |
|---|---|
| Local Customs | Cities whose culture, language, or religion differs from the capital's lose half as much loyalty |
| Natural Course | As in vanilla |
| Uniformity | These loyalty penalties are larger, but each year some residents switch to the kingdom's culture, language, and religion |

### Military

**Army**: The share of each city's troops that is placed in the central [Guard](armies.md). The rest are the city lord's own local army. The capital's troops are always all in the Guard.

| Option | Share of each city's troops sent to the Guard |
|---|---|
| Noble Retinues | None. Local armies attack on their own, as in vanilla |
| Limited Obligation | 25% |
| Balanced Levy | 50% |
| Central Legions | 75% |

**Naval Doctrine**:

| Option | Transports per port | Land unit damage |
|---|---|---|
| Land Power | 1 | +15% |
| Balanced Forces | 2 | Normal |
| Sea Power | 4 | -15% |

Transports carry cannons and bombard the coast. Armies also cross the sea by them.

### Diplomacy

**Foreign Policy**: Adjusts the [government's tendencies](government.md#government-tendencies).

| Option | Effect |
|---|---|
| Pacifism | The gap between wars is doubled, and only much weaker rivals are challenged |
| Realpolitik | Follows the government's tendencies |
| Expansionism | The gap between wars is halved, and somewhat stronger rivals are challenged |

**Vassal Policy**: The overlord's policy toward all its vassals.

| Option | Effect |
|---|---|
| Charter of Autonomy | Vassals' opinion of the overlord +15, and Integration -1 each year |
| Feudal Contract | Normal |
| Forced Assimilation | Each year 5% of the residents in each vassal's cities whose culture or religion differs from the overlord's are assimilated. A vassal whose own culture or religion differs from the overlord's has its opinion of the overlord -30 |

**Vassal Tribute**: The share of a vassal's income paid to its overlord.

| Option | Paid to overlord | Vassal's opinion of the overlord |
|---|---|---|
| Let Them Recover | None | +20 |
| Customary Tribute | 25% | Unchanged |
| Heavier Tribute | 50% | -60 |
| Bleed Them Dry | 75% | -100 |

**Fealty**: The vassal's own attitude toward its overlord.

| Option | Effect |
|---|---|
| Seek Autonomy | Integration -1 each year, and the motive for an Independence Plot is stronger |
| Honor the Contract | Normal |
| Loyal Subject | Integration +1 each year, and the motive for an Independence Plot is weaker |

## Regime Names

The small line under the kingdom name in the parliament window is the kingdom's regime name. It is decided by the current mix of policies, and hovering over it shows the policies that produced it. For example, a kingdom with Hereditary Office, Feudalism, Tax Farming, and Noble Retinues is a "Feudal Fragmentation"; one with Direct Rule, Central Collection, and Uniformity is an "Absolute Monarchy"; an empire with Uniformity, Direct Rule, and Shared Revenue or Central Collection is a "Celestial Empire"; and a vassal that seeks autonomy is a "Dominion" if it is a monarchy and an "Autonomous State" if it is a republic. Parliament's own name and the ruler's title also change with the regime: for example the Estates-General, the Privy Council, the Senate, and the Great Council, and Khan, Hegemon, and Princeps Senatus.

## Voting and Proposals

- Each kingdom holds a parliament every 4 to 6 years and votes on one bill. The bill is drafted at the previous session. In the parliament window you can see which clan proposed it and when the vote will be held.
- **Each policy is voted on separately**: Every clan has an option it wants on each policy. If the bill moves that policy closer to what the clan wants, it votes yes; otherwise it votes no. Votes are counted by seats, and a majority passes the bill. A clan's opinion of the proposer also matters: a bill from a friendly clan may get a yes even if it is slightly unfavorable.
- **Circumstances**: The option a clan wants depends on its stance and also on its circumstances. Clans with many ports prefer sea power, and inland clans prefer land power. Clans governing foreign residents prefer Local Customs. Moving troops and money to the center favors the royal house and hurts local clans. Warlike or hotheaded chiefs prefer expansion, and in wartime all clans prefer more arms. The overlord's clan prefers more Vassal Tribute.
- **Blocs**: Clans in a [bloc](blocs.md) vote together.
- **Proposals**: The more discontented a clan is and the more seats it has, the more likely it is to be the proposer. The proposer puts forward the change it wants most, whether or not it can pass. When two camps are at odds, bills are often voted down and policies change slowly. If no clan wants a change, there is no bill that session.
- **The royal house's concern**: The ruling clan estimates whether a bill would cause unrest in the provinces. When the kingdom is at its Prime and the provinces are calm, the royal house is inclined to take power. In Decline the provinces are already unstable, and it will not take too much.

When a bill passes, the world log records "kingdom adopts policy". If "Policy notices" is off in the settings, the entry is only written to world history and does not pop up at the top left.

## Clan Opinion

Clans in the same parliament have opinions of each other, and each direction is counted separately. Opinion comes from:

- **Politics**: Clans with similar stances get along, and opposed ones do not.
- **Best friends and marriage**: The two chiefs are best friends, or a chief's partner belongs to the other clan.
- **Neighbors**: The two clans hold cities that border each other.
- **Favors**: When a clan votes yes though it would not have otherwise, the proposer records a favor toward it. When a bill benefits a clan, that clan records a favor toward the proposer.
- **Grudges**: When a clan's no vote sinks the proposer's bill, the proposer records a grudge against it. When a bill harms a clan, that clan records a grudge against the proposer.

Favors fade in about ten years and grudges in about twenty-five. After a chief is replaced, two thirds of favors and grudges remain. The royal house is also a clan and records favors and grudges: a royal house that keeps curbing the provinces falls out with the local clans.

In the clan window, the row above Overview lists the clans it gets along with (left) and the ones it is at odds with (right) in its parliament. Hover over a banner to see both sides' opinions and the breakdown.

## Local Discontent

Policies affect the loyalty of the cities held by each clan (the capital and the royal house's cities excluded). The breakdowns appear in the vanilla city window.

- **Favored policies and Opposed policies**: When every policy is at the option a clan wants, its cities' loyalty rises. The further the policies are from what it wants, the lower the loyalty. Forcing through a policy that most clans oppose makes the opposing clans' cities unhappy at the same time.
- **Curbed privileges**: When troops and money are more centralized than a clan accepts, its cities' loyalty falls. If both Army and Taxation are at their most central, local cities rebel easily.

Falling city loyalty also affects the [dynastic cycle](cycle.md).

## What the Player Can Do

- **Propose a bill**: Click a different option on any policy to replace the current bill with it. Click the option already in force to withdraw the bill on that policy.
- **Forecast**: Hover over a policy option to see seats for and against and whether it would pass.
- **Vote now**: Hold the vote immediately instead of waiting for the next session.
- **Decree**: Put the bill into effect without a vote. The cities of the clans that oppose it become unhappy.
- **Withdraw**: Remove the current bill, whoever proposed it.
- **Write into the charter**: Expand a policy and click the lock at the right end of the selected option. A "Charter" mark appears after the policy name, and parliament no longer proposes changing that policy, though clans can still be discontented with it. Clicking the lock on the option of your own bill decrees it at once and writes it into the charter. Picking another option removes the policy from the charter.

The left of the parliament window has the seat semicircle, the clan list, and a chart marking each clan's stance. The royal house's dot wears a crown, and the cadets follow the royal house in the list. For how blocs are shown, see [Blocs](blocs.md). The right side shows the policies and the current bill.

If "Parliament and policies" is turned off in the settings, policies stop taking effect, city tribute reverts to the vanilla tribute traits, vassals pay no tribute, and clans no longer become discontented over policy. Republics still elect their consuls.
