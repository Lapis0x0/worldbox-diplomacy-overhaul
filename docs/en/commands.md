# Commanding Armies

Vanilla armies have no orders. A city picks an attack target, and the captain picks the next thing at random after each task: wandering, waiting, chatting. Now you can give orders to any army of any kingdom, and the army keeps carrying them out until you cancel them or they become invalid. After an order is canceled, local armies go back to vanilla AI and the Guard goes back to the kingdom's command.

## Three Orders

Which order you give depends on what you click:

| What you click | Order |
|---|---|
| A foreign city | **Attack** |
| A city of your own kingdom or a friendly one (overlord, vassal, ally) | **Garrison** |
| Open ground outside a city | **Camp** |
| The city the army is garrisoning, or the place it is camping | **Cancel the order** |

**Attack**

- If the two kingdoms are not at war, war is declared first. A vassal cannot declare war on others. If a vassal attacks its own overlord, the vassal relationship ends at once.
- The army does not wait to fill up. It sets off as soon as the order is given, and soldiers drop what they are doing and follow the captain.
- When the target city is taken or stops being an enemy, the order is canceled automatically.
- After loading a save or reloading the game, attack orders are lost and the army goes back to its previous behavior.

**Garrison**

- The army moves to the city and waits near the center. If enemies enter the city it goes out to fight.
- A garrisoning army is not sent elsewhere, and its soldiers do not wander off.
- Garrison orders are kept in saves. If the city is lost, or the kingdom is no longer friendly, the order is canceled automatically.

**Camp**: Like garrison, but in the open. The order is canceled automatically if the spot turns into water or mountain.

## How to Give Orders

### Box selection

Works without holding a power:

- **Zoomed out**: Drag a box with the left mouse button around the army banners. If the box covers armies of several kingdoms, only the kingdom with the most armies in it is selected.
- **Zoomed in**: Select soldiers with the vanilla box selection. This selects the armies they belong to.
- Clicking a single army banner also selects that army.
- With armies selected, **right-click** a city or open ground to give an order, following the table above. If all selected armies are garrisoned at the place you click, the right-click cancels all of them.
- **Esc** clears the selection. With no army selected, right-click works as in vanilla.

### Powers

- **Command Army**: Click an army (its banner or a soldier nearby), then click the target.
- **Kingdom Offensive**: Click a kingdom (by clicking one of its cities), then click the target city. A foreign city sends all of that kingdom's armies to attack. A city of the kingdom or a friend makes all of them garrison there.
- **Hand Back Command**: Click a kingdom, and all its armies that follow your orders are released back to the AI. While holding this power, the map shows where those armies are heading.

## Map Indicators

- Once armies are selected, an arrow is drawn from the army to the mouse. The city under the mouse is colored by the order: red for attack, green for garrison, white for cancel.
- Armies following your orders show their headcount on the banner: red when attacking, green when garrisoning or camping. The army's tooltip and window get an extra "Orders" row.

## Crossing the Sea

When the target is on another continent, the army calls ships by itself. Ships leave from a port near the army that can reach the sea. If no transport is free nearby, one is built at the port. A kingdom with no port at all cannot cross the sea, and you get a message that no transport is available.

## Reinforcements

In vanilla, new recruits join the army only when the captain is back in the home city, so armies stationed away cannot be reinforced. Now recruits walk to the army by themselves whenever the captain is in any city of the kingdom and on the same continent as the recruits.
