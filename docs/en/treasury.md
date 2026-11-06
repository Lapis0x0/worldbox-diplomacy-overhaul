# Treasury, Pay and Public Works

The treasury is a kingdom's reserve of gold. In vanilla, money given to a city lord or king only goes in and never comes out, and finished buildings do very little. The mod gives every kingdom a treasury, makes cities pay for their own local troops, gives buildings real effects, and lets the kingdom pay for extra buildings in its cities.

## How Money Flows

- **Residents to city lord**: As in vanilla, residents give part of their income to the city lord.
- **City lord to kingdom**: City lords send up a share of their income according to parliament's [Taxation](parliament.md#domestic) policy: Tax Farming 25%, Shared Revenue 50%, Central Collection 75%. Of what is sent, 20% goes to the king personally and the rest goes into the treasury. Half of what the city lord keeps goes into the city stockpile. Every city lord hands in the taxes gathered over the year in the kingdom's yearly accounting month, before pay is issued.
- **Vassal to overlord**: Part of the vassal treasury's income goes to the overlord according to the overlord's [Vassal Tribute](parliament.md#diplomacy).

The treasury holds only gold; wood, stone, and food stay in the city stockpiles. With a kingdom selected, the gold slot in the bottom info bar shows the treasury balance. The line under the leader's wealth in the kingdom window and its tooltip also shows the treasury balance.

## Paying the Troops

Local troops are paid by their city from its stockpile. The [Guard](armies.md) is paid by the treasury. Each soldier is paid 0.5 gold per year.

- **Recruiting**: A city recruits local troops only if its stockpile can pay all its local troops for a year. The treasury refills the Guard only if it can pay the whole Guard for a year. Army size therefore follows income. Under Central Collection, cities keep little money, their local armies are limited, and troops and gold concentrate at the center.
- **Unpaid troops**: The part of pay that cannot be paid each year is recorded as unpaid. While pay is owed, no new recruits are taken. Each year a quarter of the unpaid soldiers are discharged, and the damage of the rest falls year by year, by up to a quarter.
- **Back pay**: Once the pay is cleared, damage recovers year by year.

Unpaid local troops are shown in the city window and city tooltip. The unpaid Guard is shown in the kingdom window and kingdom tooltip.

## Where Money Goes

| Source | Uses |
|---|---|
| City stockpile | Building houses and upgrading buildings (vanilla), local troops' pay and [armament](armies.md#armament) |
| Treasury | The Guard's pay and armament, public works, plots started by the king, reparations after a lost war |
| King's personal wealth | His or her own equipment (vanilla) |

- **Reserve**: The treasury keeps a reserve of two years of Guard pay, doubled during a war. Outside war, it is increased by half when the strongest neighboring kingdom of another camp that borders it has more than 1.5 times its strength. The two increases do not stack. Armament and public works spend only money beyond the reserve. The limit for each is set by parliament's [Fiscal Priority](parliament.md#domestic), and money not spent stays in the treasury.
- **Plots**: The cost of a plot started by a king is paid from the treasury.
- **Reparations**: After a lost war, the treasury and every city stockpile of the loser hand over gold at the same ratio, and it goes to the winner's treasury. See [War and Peace Talks](war.md#treaties).

## Buildings

With "Building effects" on in the settings, several vanilla buildings have real effects. Extra buildings of the same kind stack, but each addition has half the effect of the previous one.

| Building | Effect |
|---|---|
| Market | Residents earn more, so the tax sent up increases |
| Temple | City loyalty rises; under Uniformity, the city's residents assimilate faster |
| Barracks | The number of soldiers the city can support increases |
| Watchtower | The higher the kingdom's armament tier, the more arrows per volley |
| Statue | A statue in the capital raises the king's renown every year |
| Windmill | Farms can be placed around every windmill (vanilla uses only the first) |

With "Building effects" off, these buildings behave as in vanilla, and no public works are built.

## Public Works

Public works means the kingdom paying for an extra building in one of its cities: the treasury pays, the city may build one more building of that kind, and construction starts at once at a site chosen as in vanilla. If the city has no room, nothing is built and nothing is spent. Each city can get at most two extra buildings of each kind, and larger cities can take more in total.

The kingdom builds where it is needed most: markets in cities with high tax, temples in cities with low loyalty, watchtowers on the front and along a strong neighbor's border, barracks in wartime or under threat, windmills in cities short of food, and statues in the capital when funds are ample. Once a project is chosen, the kingdom saves up if it cannot pay yet.

When public works are built in a city held by a clan (the capital excepted), that clan records a favor of 10 toward the ruling clan. This is not recorded when parliament is turned off. The city window shows a "State-Commissioned" line listing the buildings the kingdom has funded there.

## Treasury Page

The treasury page is in the kingdom window's tab bar, above the Interesting People tab.

- **Gold**: The treasury's current gold, and last year's surplus.
- **Flow chart**: Where last year's gold came from and went. Sources are the top-paying cities, the other cities, vassal tribute, and reparations. Uses are the king's (or consul's) share, Guard pay, arms, public works, plots, tribute to an overlord, and reparations paid. The wider the band, the more gold.
- **Ledger**: The treasury's major events: reparations, plots, the Guard going unpaid and being paid again, tax policy changes, vassals joining or leaving, and public works.

The books close once a year, so a new world or a newly installed mod shows last year's flows only after the first year ends.
