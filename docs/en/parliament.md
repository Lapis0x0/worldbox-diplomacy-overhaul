# Parliament and Policies

Every kingdom has a parliament, monarchies included. Clans (vanilla clans) get seats in proportion to the cities they hold, and vote according to their own stance. Together they decide the kingdom's taxation, army organization, land grants, and foreign policy. Parliament is the second tab on the left of the kingdom window. When a kingdom is selected on the map, there is also a parliament button at the bottom right.

## Seats

- Each city gives its lord's clan a share of seats. The bigger the city, the more seats (a city of 100 people gives about 20). The capital's seats belong to the royal house, or to the consul's clan in a republic.
- In a one-city kingdom, the royal house holds all the seats. As the kingdom expands and gains more cities, the local clans together can outvote the royal house.
- **Cadets**: In a monarchy, when members of the royal house become lords of outlying cities, those cities' seats form a separate bloc, the cadets. Royal cities next to or near the capital still count with the royal house. Only those out on the frontier are cadets. The cadets stand by the dynasty and never vote to abolish the monarchy, but they hold provincial cities, do not want to hand over their troops and money, and want land grants most of all. Royal Kin First hands cities to the royal house, and so grows the cadets.
- Children join the clan of the parent who passes on the succession. A person still counts as part of a clan if anyone in the last three generations of ancestors was a king or city lord. If no one has held office for over a hundred years, that branch has faded.

## Stances and Factions

Each clan has a position on two axes:

- **Conservative ↔ Radical**: Old clans and chiefs who are older, Content, or Honest lean conservative. New clans and young, ambitious chiefs lean radical.
- **Central ↔ Local**: Clans that live near the capital and clans with Paranoid chiefs lean central. Clans far from the capital that want to stand on their own lean local.

The chief's traits matter a great deal, but a clan's stance builds up over generations: a new chief only pulls the clan part of the way toward his own views.

The two axes make four factions. Clans in the same faction share a color:

| | Central | Local |
|---|---|---|
| Conservative | Royalists (blue): old clans at court | Aristocrats (teal): old clans in the provinces |
| Radical | Reformists (red): new powers at court | Republicans (orange): new powers in the provinces |

## Policies

Policies are on three pages: Domestic, Military, and Diplomacy. The options of each policy lie along one axis, with the conservative or local end on one side and the radical or central end on the other. Vassal Policy and Vassal Tribute exist only for kingdoms with vassals. Fealty exists only for vassals.

### Domestic

**Government**: Monarchy or Republic. Changing to Republic removes the current ruler and parliament elects a consul. Changing to Monarchy makes the current consul's clan the royal house. This policy needs three quarters of the seats.

**Appointments**: Who takes over a city when its lord's seat is vacant.

| Option | Who takes over |
|---|---|
| Hereditary Office | Someone from the previous lord's clan |
| Royal Kin First | Someone from the royal house first (the vanilla behavior). In a republic this is called Seniority |
| Meritocracy | The ablest person in the kingdom, regardless of birth |

The lord's clan decides who holds the city's seats, so Appointments reshapes parliament. Hereditary Office lets local clans keep their cities. Royal Kin First hands more and more cities to the royal house, which grows the cadets. Meritocracy keeps promoting new clans.

**Land Grants**:

| Option | Effect |
|---|---|
| Feudalism | Rebel cities and conquered cities are more often granted as vassals, and royal kin are granted land more often |
| Limited Grants | Only land that cannot be governed is granted away |
| Direct Rule | Land is rarely granted, and provinces are more often taken back under direct rule |

**Taxation**: How much of their income city lords send to the kingdom. See [Treasury and Pay](treasury.md).

| Option | Sent up |
|---|---|
| Tax Farming | 20% |
| Shared Revenue | 50% |
| Central Collection | 70% |

Elven kingdoms start with Tax Farming, sheep kingdoms start with Central Collection, and all others start with Shared Revenue. This policy replaces vanilla's Low Tribute and High Tribute traits.

**Cultural Policy**:

| Option | Effect |
|---|---|
| Local Customs | Cities whose culture, language, religion, or capital differ lose half as much loyalty |
| Natural Course | As in vanilla |
| Uniformity | These loyalty penalties are larger, but each year some residents switch to the kingdom's culture, language, and religion |

### Military

**Army**: How many of each city's troops are placed in the central [Guard](armies.md). The rest are the city lord's own local army. The capital's troops are always all in the Guard.

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

Transports carry cannons and bombard the coast. They are also how armies cross the sea.

**Armament**: How much spare money the treasury spends each year on standard equipment for the Guard. See [Armament](armies.md#armament).

| Option | Effect |
|---|---|
| Self-Equipped | No equipment is issued. The Guard forges its own, as in vanilla |
| Replacements Only | Missing gear is issued. No upgrades |
| Regular Funding | Missing gear is issued. Once most of the Guard is equipped, the material goes up one tier |
| Armament First | As above, and all spare money is used to buy equipment |

### Diplomacy

**Foreign Policy**:

| Option | Effect |
|---|---|
| Pacifism | The wait between wars is doubled, and only much weaker rivals are chosen |
| Realpolitik | Follows the tendency of the [government](government.md#government-temperament) |
| Expansionism | The wait between wars is halved, and the kingdom dares to attack somewhat stronger rivals |

**Vassal Policy**: The overlord's attitude toward all its vassals.

| Option | Effect |
|---|---|
| Charter of Autonomy | Vassals are happier, but assimilate more slowly |
| Feudal Contract | Normal |
| Forced Assimilation | Each year part of each vassal's population is converted to the overlord's culture and religion. Vassals with a different culture or religion are very unhappy |

**Vassal Tribute**: How much of its income a vassal pays to its overlord.

| Option | Vassal income sent to overlord | Vassal's attitude |
|---|---|---|
| Let Them Recover | None | Happier |
| Customary Tribute | 25% | Normal |
| Heavier Tribute | 50% | Unhappy |
| Bleed Them Dry | 75% | Very unhappy |

**Fealty**: The vassal's own attitude toward its overlord.

| Option | Effect |
|---|---|
| Seek Autonomy | Assimilates slowly and wants independence more |
| Honor the Contract | Normal |
| Loyal Subject | Assimilates quickly and wants independence less |

## How Parliament Works

- Each kingdom holds a parliament every 4 to 6 years, and votes on one bill. The bill is drafted at the previous session. In the parliament window you can see which clan proposed it and how many years until the vote.
- **Each policy is voted on by itself**: Every clan has an option it wants on each policy. If the bill moves that policy closer to what the clan wants, it votes yes. Otherwise it votes no. Votes are counted by seats, and a majority passes the bill. The other policies do not affect this vote.
- **Circumstances, not just stance**: Which option a clan wants depends on its stance, and also on its circumstances and interests:
  - Clans with many ports want sea power, inland clans want land power.
  - Clans that govern foreign residents fear unrest and want Local Customs.
  - When troops and money move to the center, the royal house gains and local clans lose.
  - Warlike or hotheaded chiefs want expansion and more arms, peaceful chiefs want fewer wars. In wartime everyone wants more arms.
  - The overlord's clans want more Vassal Tribute, but not when vassals are strong or already discontented.
- **Drafting**: The more discontented a clan is and the more seats it has, the more likely it is to be the proposer. The proposer picks the policy that suits it best and is most likely to pass. If nobody wants a change, there is no bill this time.
- **The royal house's worries**: The ruling clan estimates whether a bill would push the provinces into revolt. At Prime the provinces are calm and the royal house is comfortable taking power. In Decline the provinces are already shaky and it dares not push too hard.

When a bill passes, the world log records "kingdom adopts policy".

## Local Discontent

Policies affect the loyalty of cities held by each clan (the capital and the royal house's cities are exempt). The breakdowns appear in the vanilla city loyalty window:

- **Favored policies / Opposed policies**: If every policy is where a clan wants it, its cities are more loyal. The further the policies are from what it wants, the less loyal its cities. Forcing through a policy that most clans oppose makes the opposing clans' cities unhappy together.
- **Curbed privileges**: When troops and money are more centralized than a clan wants, its cities are unhappy. Large clans with greedy chiefs care the most, and so do the cadets. If both Army and Taxation are at their most central, local cities rebel easily.

Falling city loyalty also drags down the [dynastic cycle](cycle.md).

## What the Player Can Do

- **Propose a bill**: Click a different option on any policy to replace the current bill with it. Click the option already in force to withdraw the bill on that policy.
- **See the forecast**: Hover over a policy option to see how parliament would vote: seats for, seats against, and whether it would pass.
- **Vote now**: Hold the vote immediately instead of waiting for the next session.
- **Decree**: Skip the vote and put the bill into effect. Cities of the clans that oppose it become unhappy.
- **Withdraw**: Remove the current bill, whoever proposed it.
- **Lock a policy**: Expand a policy and click the lock at the right end of the selected option; it turns gold. Parliament will no longer propose changing that policy, though clans can still resent it. Clicking the lock on the option of your own bill decrees it at once and locks it. Picking another option unlocks it.

If you don't want parliament, turn off "Parliament and policies" in the settings. Policies stop having any effect, city tribute follows the vanilla tribute traits, vassals pay no tribute, and clans no longer resent policies. Republics still elect their consuls.

The left of the parliament window has the seat semicircle, the clan list, and a chart marking each clan's stance. The royal house's dot wears a crown, and the cadets follow the royal house in the list. Click a clan to see its chief (for the cadets, the leading prince) and its cities. The right side shows the policies and the current bill.
