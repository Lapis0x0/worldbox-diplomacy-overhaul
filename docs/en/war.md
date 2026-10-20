# War and Peace Talks

In vanilla, a war ends in an unconditional ceasefire or when one side is destroyed. Now, when the two sides have fought long enough, they sit down to negotiate and sign a treaty based on the result: a white peace, reparations, ceding vassals, or submission.

## When Kingdoms Go to War

After a war, a kingdom waits a while before fighting again, and weighs the strength of rivals when choosing one. Both depend on the government and on parliament's Foreign Policy, see [Government Temperament](government.md#government-temperament) and [Parliament and Policies](parliament.md#diplomacy). When choosing a rival, the strength of each side is counted for the whole bloc: overlord, vassals, and allies all count.

A vassal never starts wars itself. It fights with its overlord, see [Vassals](vassals.md#wars-are-shared).

## War Score

Each war has a war score, shown in the war window and in tooltips, for example "Attackers ahead by 35". When the gap is small it shows "Stalemate". The war score looks only at the two main kingdoms:

- The share of cities lost since the war began. The side that lost more is at a disadvantage.
- If the capital at the start of the war is now in the other side's hands, that is a big loss.
- The side with more troops gets a small advantage.

## When They Talk

After 3 years of war, the kings of both sides can start "Peace Talks":

- **The side ahead**: Wants to talk once the war score reaches what it expects. Warlike kings expect more and insist on fighting until the other side submits. The longer the war and the more of its own people have died, the less it expects.
- **The side behind**: The further behind it is and the longer the war has gone on, the more it wants to talk.

When the talks complete, the outcome depends on the war score at that moment. If the gap is below what the leading side expects, the leading side refuses and nothing happens. They can talk again later. In a war that is a tie, both sides' expectations slowly drop until it ends in a white peace.

The vanilla "peace negotiation" plot no longer appears. Turn peace talks off in the settings to restore vanilla. Total War has no peace talks.

## Treaties

| War score gap | Treaty |
|---|---|
| Below 20 | White peace |
| 20 to 50 | Reparations |
| 50 to 80 | Larger reparations, and vassals ceded |
| 80 or more | Larger reparations, submission, and vassals ceded |

- **Reparations**: The loser hands over part of the gold in its treasury and in every city's stockpile (20%). All of it goes into the winner's treasury.
- **Ceding vassals**: Each of the loser's vassals is checked in turn. Those that border the winner may switch to the winner. The better the winner's king is at diplomacy and the more ambitious, the easier it is to take them over. A winner in Decline cannot take them.
- **Submission**: The loser becomes a vassal of the winner and starts Resentful. Vassals the winner did not take over become independent.

If the winner is itself a vassal, the ceded vassals and the submitting loser go to the winner's overlord.

## After the War

- **Mandate**: At the reparations level, the winner gets +2 and the loser -4. At the ceded vassals and submission levels, the winner gets +5 and the loser -10. A white peace changes nothing.
- **Humiliating defeat**: A loser that only paid reparations or ceded vassals has a lower opinion of the winner. It fades over ten years.
- The treaty is recorded in the world log. The war window shows the terms and the reparations breakdown.

## Wars of Independence

A war of independence started by a vassal also goes through peace talks. If the talks reach the submission level for the overlord, the vassal becomes a vassal again. Every other outcome makes the vassal independent, with reparations paid as in the table.

## Sieges

This applies to all armies. In vanilla, as long as a single defender is left in a city, siege progress is stuck at 5%. The attacking captain wanders around after entering the city, and a large city may never fall. Now:

- After entering the city, the attacking captain goes straight for the nearest defending warrior, city lord, or king. When no one is left, the captain goes to the city center.
- When the attackers in the city outnumber the defenders 5 to 1, the defenders cannot hold. Siege progress rises as normal, and the city falls when it is full.
