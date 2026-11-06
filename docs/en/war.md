# War and Peace Talks

In vanilla, a war ends in one side's destruction or an unconditional ceasefire. The mod lets the two sides negotiate once the situation is clear, and sign a treaty according to the war score: a white peace, reparations, ceding vassals, or submission.

## Going to War

After a war ends, a kingdom must wait a while before fighting again, and it compares strength when choosing a rival. Both depend on the government and on parliament's Foreign Policy; see [Government Tendencies](government.md#government-tendencies) and [Foreign Policy](parliament.md#diplomacy). Strength is counted for the whole camp: overlord, vassals, and allies all count.

A vassal never starts wars on its own. It fights alongside its overlord; see [Vassals](vassals.md#wars-are-shared).

## War Score

Each war has a war score, shown in the war window and tooltips, for example "Attackers ahead by 35". When the gap is small it shows "Stalemate". The war score compares only the two main kingdoms and depends on three factors.

- The share of cities lost since the war began. The side that lost more is at a disadvantage.
- If the capital at the start of the war is now in the other side's hands, the disadvantage grows sharply.
- The side with more troops has a slight advantage.

## Peace Talks

Peace Talks is a plot. After 3 years of war, the kings of both main sides can start it, at a cost of 5 gold.

- **The side ahead**: Starts when the war score reaches what it expects. Warlike kings expect more. The longer the war and the more of its own people have died, the less it expects.
- **The side behind**: The further behind it is and the longer the war has gone on, the more likely it is to start.

When the talks complete, the outcome depends on the war score at that moment. If the gap is below what the side ahead expects, it is treated as a refusal and nothing happens; the talks can be started again later. In a tied war, both sides' expectations slowly drop until it ends in a white peace.

Total War has no peace talks. The vanilla "peace negotiation" plot is replaced by Peace Talks, and turning off peace talks in the settings restores it. The vanilla Friendship power is still an unconditional peace.

## Treaties

| War score gap | Treaty |
|---|---|
| Below 20 | White peace |
| 20 to 50 | Reparations |
| 50 to 80 | Reparations (slightly higher share), vassals ceded |
| 80 or more | Reparations (slightly higher share), submission, vassals ceded |

- **Reparations**: The loser hands over 20% of the gold in its treasury and in every city's stockpile. All of it goes into the winner's treasury.
- **Ceding vassals**: Each of the loser's vassals is checked in turn. Those bordering the winner's camp may switch to the winner. The better the winner's king is at diplomacy and the more ambitious, the easier it is to take them over. A winner in Decline or Collapse cannot take them.
- **Submission**: The loser becomes a vassal of the winner, with Integration 0. Vassals of the loser that the winner did not take over become independent.

If the winner is itself a vassal, the ceded vassals and the submitting loser go to the winner's overlord.

## After the War

- **Mandate**: At the reparations level, the winner gets +2 and the loser -4. At the ceded vassals and submission levels, the winner gets +5 and the loser -10. A white peace does not change the Mandate.
- **Humiliating defeat**: A loser that only paid reparations has its opinion of the winner reduced by 20, and one that ceded vassals by 40. This fades over ten years. Submission has no such penalty.
- The treaty is recorded in the world log, and the war window shows the terms and the reparations breakdown.

## Wars of Independence

A war of independence started by a vassal also ends through peace talks. If the talks reach the submission level for the overlord, the vassal becomes a vassal again. Otherwise the vassal becomes independent, with reparations paid according to the treaty.

## Sieges

Siege rules apply to all armies. In vanilla, as long as a single defender is left in a city, siege progress stays at 5%, and a large city may never fall. Now:

- After entering the city, the attacking captain goes straight for the nearest defending warrior, city lord, or king. When no defender is left, the captain goes to the city center.
- When the attackers in the city outnumber the defenders 5 to 1, the defenders cannot hold, siege progress rises as normal, and the city falls when it is full.
