# Rise and Fall of Dynasties

The dynastic cycle is the four phases that large kingdoms go through: **Renewal, Prime, Decline, Collapse**. At Prime, vassals are loyal, cities are calm, and armies fight well. In Decline, people grow restless and rebellions break out. Finally a Great Rebellion splits the kingdom. A dynasty that survives the Great Rebellion returns to Decline, or it can be overthrown by a general, after which a new dynasty starts from the beginning.

## Who Has a Cycle

The cycle applies to a kingdom with 6 or more cities, or with vassals of its own. Vassals do not have a cycle. The current phase is shown as a kingdom trait and in the kingdom window subtitle, for example "Empire · Prime". The dynastic cycle can be turned off in the settings.

## Mandate of Heaven

The **Mandate of Heaven** is the number that drives the cycle, from 0 to 100. Hover over the cycle trait to see the Mandate and its components.

Each year the Mandate moves toward the "deserved Mandate", which has two parts.

**Governance** normally hovers around zero.

| Item | What improves it |
|---|---|
| Loyal cities | Many cities with loyalty that is not negative |
| Ruler | A ruler with high stewardship and intelligence. Very low while the throne is empty |
| Vassals | High Integration among vassals |
| Livelihood | Few homeless people |
| Overextended | A penalty when an overlord has only a few cities of its own but many vassals |

**Aging**: Every dynasty builds up **Decay**, more slowly the better it is governed. Decay has almost no effect early on and costs more and more late in the dynasty's life, so even the best dynasty declines eventually, only later. Each time a dynasty survives a Great Rebellion it also keeps some **Old wounds**, which bring the next Decline sooner.

Wars and cities change the Mandate directly.

| Event | Mandate |
|---|---|
| Win a war | +5 (+2 if peace talks reached only reparations) |
| Lose a war | -10 (-4 if peace talks reached only reparations) |
| Gain a city | +1 |
| Lose a city | -2 |
| Capital captured | a further -15 |
| Failed Restoration | -10 |

A white peace does not change the Mandate.

## The Four Phases

```
Renewal ──≥ 70──▶ Prime ──< 50──▶ Decline ──< 20──▶ Great Rebellion ──▶ Collapse
   ▲                ▲                │  ▲                                  │
   │                └─ Restoration ──┘  └─── Rebellion ends (pacified) ────┤
   └───────────────── General seizes power (new dynasty) ──────────────────┘
```

The numbers in the diagram are Mandate values.

- **Renewal**: Starts when a kingdom reaches 6 cities or gets vassals, with a Mandate of 60. At 70 it enters Prime. Below 40 it goes straight to Decline.
- **Prime**: The kingdom enters Decline when the Mandate falls below 50.
- **Decline**: When the Mandate returns to 70, the dynasty is restored and goes back to Prime. If it falls below 20, or the capital is lost, a Great Rebellion breaks out.
- **Collapse**: The phase after a Great Rebellion. When the rebel wars and wars of independence are over and the kingdom is still large enough, it counts as **pacified** and returns to Decline with a Mandate of 45. If the kingdom has become small, the cycle ends. During Collapse a general may seize power. After this **change of dynasty** the kingdom returns to Renewal, and its Decay and Old wounds are cleared. See [Governments and Rulers](government.md#coup-in-times-of-collapse).

## Effects of Each Phase

| | Renewal | Prime | Decline | Collapse |
|---|---|---|---|---|
| Vassal opinion of overlord | Small rise | Large rise | Falls, more as the Mandate drops | Same as Decline |
| City loyalty | n/a | Rises | Falls, more as the Mandate drops | Same as Decline |
| Vassal Integration | Rises slowly | Rises slowly | Falls, faster as the Mandate drops | Falls faster |
| Vassals plotting independence | Normal | Normal | No longer limited by the power gap | Same as Decline |
| Rebel cities granted as vassals | Half | Most | A few | Never |
| Demanding tribute from neighbors | Allowed | Allowed | Not allowed | Not allowed |
| Government events | n/a | A republic may crown an emperor | n/a | A general may seize power |

In the vanilla breakdown windows these modifiers are called "Golden age", "Waning fortunes", and "Overlord's fortunes". The start of Decline has little effect. When the Mandate falls to around 30, the cities with the lowest loyalty start to rebel on their own, and scattered revolts break out before the Great Rebellion.

**Army damage** also follows the Mandate: at a Mandate of 100, all units in the kingdom deal +10% damage, at 50 it is normal, and at 0 it is -25%.

Vanilla's ten-year "New Conquest" loyalty bonus after taking a city is removed. The benefit of winning is now reflected in the Mandate.

## Restoration

When a declining kingdom's Mandate returns to 70, the dynasty is restored. The king (the consul in a republic) can also start a "Restoration" plot, which needs level 5 and 50 gold and usually takes a year or two. The lower the Mandate, the more a ruler good at governing wants to start it.

When the plot completes, success or failure is decided. The default chance of success is 30%, higher with a ruler of high stewardship and intelligence.

- Success: Decay is greatly reduced, the Mandate returns to at least 70, and the kingdom returns to Prime.
- Failure: Mandate -10, and the plot can be attempted again later.

## Great Rebellion

A Great Rebellion breaks out when the Mandate falls below 20, when the capital is lost during Decline, or when the **Great Rebellion** power is used.

1. Several of the lowest-loyalty cities that are not next to each other rise up at once. The bigger the kingdom, the more cities rise, up to 6. The kingdom must have at least 3 cities.
2. The rebels spread outward and take about 60% of the original kingdom's cities other than the capital.
3. Each vassal decides according to its Integration whether to rebel. Newly surrendered vassals almost always rebel, and provinces rarely do.

The kingdom then enters Collapse, and a notice appears at the top of the screen. With the Rebellions world law turned off, no Great Rebellion breaks out on its own, and a dynasty that hits bottom stays in Decline. The **Great Rebellion** power still works.

## Small Overlords

After a Great Rebellion an overlord often has only a few cities left but keeps its vassals. An overlord with fewer than 6 cities cannot hold its vassals: the Mandate drops ("Overextended"), vassal opinion drops ("Weak overlord"), and Integration falls each year. An overlord with only three or four cities loses its vassals within a few decades. Once the overlord grows back to 6 cities, these effects end.

## Pushing the Cycle by Hand

- The **Mandate of Heaven** power moves a kingdom to the next phase: no cycle, Renewal, Prime, Decline, Great Rebellion.
- In the trait editor, swap in the trait of another phase. The kingdom enters that phase from that year.
