# Rise and Fall of Dynasties

Large kingdoms go through a dynastic cycle: **Renewal → Prime → Decline → Collapse**. At Prime, vassals are loyal, cities are calm, and armies fight well. In Decline, people grow restless and rebellions break out. Finally a Great Rebellion tears the kingdom apart. A dynasty that survives the Great Rebellion can limp on, or be overthrown by a general, after which a new dynasty starts from the beginning.

## Who Has a Cycle

A kingdom has a cycle if it has 6 or more cities or has vassals of its own. Vassals do not have a cycle. The current phase is shown as a kingdom trait and in the kingdom window subtitle, for example "Empire · Prime".

You can turn the dynastic cycle off in the settings.

## Mandate of Heaven

The cycle is driven by the **Mandate of Heaven**, a number from 0 to 100. Hover over the cycle trait to see the Mandate and its components.

Each year the Mandate moves slowly toward the "deserved Mandate". The deserved Mandate has two parts:

**Governance**, which normally hovers around zero:

| Item | What improves it |
|---|---|
| Loyal cities | Many cities with loyalty that is not negative |
| Ruler | A ruler with high stewardship and intelligence. Very poor while the throne is empty |
| Vassals | High Integration among vassals |
| Livelihood | Few homeless people |
| Overextended | A penalty when the overlord has only a few cities of its own but a crowd of vassals |

**Aging**: Every dynasty builds up **Decay**, and well-governed ones build it more slowly. Decay has almost no effect early on and costs more and more late in the dynasty's life. So even the best dynasty declines eventually, it just lasts longer. Each time a dynasty survives a Great Rebellion it also keeps some **Old wounds**, and the next Decline comes sooner.

Wars and cities change the Mandate directly:

| Event | Mandate |
|---|---|
| Win a war | +5 (+2 if it ended only with reparations) |
| Lose a war | -10 (-4 if it ended only with reparations) |
| Gain a city | +1 |
| Lose a city | -2 |
| Capital captured | a further -15 |
| Failed Restoration | -10 |

A white peace changes nothing.

## The Four Phases

```
Renewal ──≥ 70──▶ Prime ──< 50──▶ Decline ──< 20──▶ Great Rebellion ──▶ Collapse
   ▲                ▲                │  ▲                                  │
   │                └─ Restoration ──┘  └─── Rebellion ends (pacified) ────┤
   └───────────────── General seizes power (new dynasty) ──────────────────┘
```

Numbers in the diagram are Mandate of Heaven values.

- **Renewal**: Starts when a kingdom first grows to 6 cities or first gets a vassal, with a Mandate of 60. At 70 it enters Prime. If it falls below 40 it goes straight to Decline.
- **Prime**: The kingdom enters Decline when the Mandate falls below 50.
- **Decline**: When the Mandate returns to 70, the dynasty is restored and goes back to Prime. If it falls below 20, or the capital is lost, a Great Rebellion breaks out.
- **Collapse**: The phase after a Great Rebellion. When the rebel wars and wars of independence are over, if the kingdom is still large enough, it counts as **pacified**. It returns to Decline with a Mandate of 45. If the kingdom has become small, the cycle ends. During Collapse a general may seize power. After this **change of dynasty** the kingdom returns to Renewal and its Decay and Old wounds are wiped clean. See [Governments](government.md#coup-in-times-of-collapse).

At the first check after loading a save, kingdoms that are already large count as Prime. Each kingdom's Decay is randomized so they do not all decline at the same time.

## Effects of Each Phase

| | Renewal | Prime | Decline | Collapse |
|---|---|---|---|---|
| Vassal opinion of overlord | Some bonus | Large bonus | Penalty, larger as the Mandate drops | Same as Decline |
| City loyalty | n/a | Bonus | Penalty, larger as the Mandate drops | Same as Decline |
| Vassal Integration | Slowly rises | Slowly rises | Falls, faster as the Mandate drops | Falls even faster |
| Vassals plotting independence | Normal | Normal | No longer held back by the power gap | Same as Decline |
| Rebel cities granted as vassals | Half | Most | A few | Never |
| Pressuring neighbors into submission | Allowed | Allowed | Not allowed | Not allowed |
| Government events | n/a | A republic may crown an emperor | n/a | A general may seize power |

The start of Decline is hardly noticeable. When the Mandate falls to around 30, the cities with the lowest loyalty start to rebel on their own, and scattered revolts break out in many places before the Great Rebellion. In the vanilla breakdown windows these modifiers are called "Golden age", "Waning fortunes", and "Overlord's fortunes".

**Army damage** also follows the Mandate. At a Mandate of 100, all units in the kingdom deal +10% damage. At 50 it is normal. At 0 it is -25%. Armies in late Decline clearly cannot hit hard.

The vanilla "New Conquest" loyalty bonus (a ten-year loyalty bonus across the kingdom after taking a city) is removed. The benefit of winning is now reflected in the Mandate.

## Restoration

When a declining kingdom's Mandate returns to 70, it is restored. The king (the consul in a republic) can also start a "Restoration" plot. It needs level 5 and 50 gold, and usually takes a year or two. The lower the Mandate, the more a governing-minded ruler wants to try.

When the plot completes, success or failure is rolled. The default is a 30% chance of success, higher with a ruler of high stewardship and intelligence:

- Success: Decay is greatly reduced, the Mandate returns to at least 70, and the kingdom returns to Prime.
- Failure: Mandate -10, the same as losing a war. The ruler can try again later.

Level 5 rulers are rare, so whether a declining dynasty is restored mostly depends on whether it produces a great ruler.

## Great Rebellion

A Great Rebellion is triggered when the Mandate falls below 20, when the capital is lost during Decline, or when you use the **Great Rebellion** power:

1. Several of the lowest-loyalty cities that are not next to each other rise up at once. The bigger the kingdom, the more places rise, up to 6. The kingdom must have at least 3 cities.
2. The rebels spread outward from each place and take 60% of the original kingdom's cities other than the capital.
3. Each vassal rolls according to its Integration to decide whether to rebel. Newly surrendered vassals almost always rebel. Provinces rarely do.

The kingdom then enters Collapse, and a notice appears at the top of the screen.

## Small Overlords

After a Great Rebellion an overlord often has only a few cities left but keeps its vassals. An overlord with fewer than 6 cities cannot keep its vassals down: the Mandate takes a penalty ("Overextended"), vassal opinion drops ("Weak overlord"), and Integration slips each year. An overlord with only three or four cities will lose its vassals one by one over a few decades. Once the overlord grows back to 6 cities, this pressure disappears.

## Pushing the Cycle by Hand

- The **Mandate of Heaven** power advances a kingdom one phase: no cycle → Renewal → Prime → Decline → Great Rebellion.
- In the trait editor, swap in the trait of another phase. The kingdom enters that phase from that year.
