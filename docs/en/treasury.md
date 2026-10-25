# Treasury, Pay and Public Works

In vanilla, money that reaches a city lord or king only ever comes in and never goes out, and finished buildings do very little. Now every kingdom has a treasury, cities pay for their own troops with their own money, markets, temples and other buildings actually matter, and the kingdom can fund extra buildings in its cities. Money has uses at both the city level and the central level.

## How Money Flows

- **Residents → city lord**: As in vanilla, residents give part of their income to the city lord.
- **City lord → kingdom**: City lords send money up according to parliament's [Taxation](parliament.md#domestic) policy: Tax Farming 25%, Shared Revenue 50%, Central Collection 75%. A small part of what is sent goes to the king personally, and the rest goes into the treasury. Half of what the city lord keeps goes into the city stockpile. Every year, each city lord hands in the taxes gathered over the year.
- **Vassal → overlord**: Part of the vassal treasury's income goes to the overlord, according to the overlord's [Vassal Tribute](parliament.md#diplomacy).

The treasury holds only gold. Wood, stone, and food stay in the city stockpiles.

With a kingdom selected, the gold slot in the bottom info bar shows the treasury. In the kingdom window and its tooltip, the line under the leader's wealth is also the treasury.

## Paying the Troops

Local troops are paid by their city from its stockpile. The [Guard](armies.md) is paid by the treasury, at 0.5 gold per soldier per year.

- **Recruiting depends on money first**: A city recruits local troops only if its stockpile can feed all its local troops for a year. The treasury refills the Guard only if it can pay the whole Guard for a year. So army size follows income. Under Central Collection, cities keep little money and cannot fill their local armies, and troops and money are concentrated at the center.
- **Unpaid troops**: Whatever cannot be paid at the yearly pay is counted as unpaid. While pay is owed, no new recruits are taken. Each year a quarter of the unpaid soldiers are discharged, and the ones who stay lose morale. Their damage drops year by year, by up to a quarter.
- **Back pay**: Once pay is cleared, damage recovers year by year.

Unpaid local troops are shown in the city window and city tooltip. Unpaid Guard is shown in the kingdom window and kingdom tooltip.

## Where Money Goes

| Whose money | What it is spent on |
|---|---|
| City stockpile | Building houses and upgrading buildings (vanilla), local troops' pay and [armament](armies.md#armament) |
| Treasury | The Guard's pay and armament, public works, plots started by the king, reparations after a lost war |
| King's personal wealth | His or her own equipment (vanilla) |

- **Reserve**: The treasury first sets aside a reserve: two years of Guard pay in peacetime, doubled during a war, and half again when bordering a much stronger neighbor. Armament and public works only spend money beyond the reserve. How much each may spend at most is set by parliament's [Fiscal Priority](parliament.md#military); whatever is not spent stays in the treasury.
- **Plots**: When a king starts a plot, the money comes from the treasury. Whether the plot is affordable also depends on the treasury.
- **Reparations**: After a lost war, the treasury and every city stockpile hand over gold at the same ratio. It goes to the winner's treasury. See [War and Peace Talks](war.md#treaties).

## Buildings

Several vanilla buildings now have real effects. Extra buildings of the same kind stack, but each adds less than the one before.

| Building | Effect |
|---|---|
| Market | Residents earn more, so the city sends up more tax |
| Temple | The city is more loyal; under Uniformity its people assimilate faster |
| Barracks | The city can field more soldiers |
| Watchtower | The higher the kingdom's armament tier, the more arrows each volley |
| Statue | A statue in the capital raises the king's renown every year |
| Windmill | Farms grow around every windmill (vanilla only uses the first) |

Turning off "Building effects" in the settings restores vanilla behavior and stops public works.

## Public Works

The kingdom pays for an extra building in one of its cities: that city may build one more of that kind, the treasury pays, and construction starts right away at a site chosen as in vanilla. If the city has no room, nothing is built and nothing is spent. Mature cities are often packed wall to wall, so public works mostly go to cities that are still growing. Each city can get at most two extra of each kind, and larger cities can take more projects.

The kingdom builds where it is needed most: markets in high-tax cities, temples where loyalty is low, watchtowers on the front and along a strong neighbor's border, barracks during war or under threat, windmills where food is short, and statues in the capital when rich. Once a project is chosen, the kingdom saves up if it cannot pay yet.

The clan holding the city owes the royal family a favor. The city window shows a "State-Commissioned" line listing the buildings the kingdom has funded there.

## Treasury Page

In the kingdom window's tab bar, above the Interesting People tab, is the treasury page:

- **Gold**: how much the treasury holds, and last year's surplus.
- **Flow chart**: where last year's gold came from and where it went. Sources on the left (the top-paying cities, the other cities, vassal tribute, reparations) flow into the bar in the middle. The top of the bar is the share taken by the king (the consul in a republic); the rest is the treasury, which flows right into guard pay, arms, public works, plots, tribute to an overlord and reparations paid. A surplus shows up on the right as "Saved"; a deficit shows up on the left as "From Reserves". The wider the band, the more gold.
- **Ledger**: the treasury's big events: reparations, plots, the guard going unpaid and being paid again, tax system changes, vassals joining or leaving, public works.

The books close once a year, so a new world or a newly installed mod shows last year's flows only after the first year ends.
