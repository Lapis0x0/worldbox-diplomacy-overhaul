# Commanding Armies

In vanilla, armies have no orders: a city chooses an attack target, and after each task the captain picks the next one at random. The mod lets the player give orders to any army of any kingdom. The army carries out the order until it is canceled or becomes invalid. After an order is canceled, local armies return to the vanilla AI, and the Guard returns to the kingdom's command.

## Three Orders

The order depends on what is clicked.

| What is clicked | Order |
|---|---|
| A foreign city | **Attack** |
| A city of the army's own kingdom or a friendly one (overlord, vassal, ally) | **Garrison** |
| Open ground outside a city | **Camp** |
| The city the army is garrisoning, or the place it is camping | **Cancel the order** |

**Attack**

- If the two kingdoms are not at war, war is declared first. A vassal cannot declare war on other kingdoms; if a vassal attacks its own overlord, the vassal relationship ends at once.
- The army does not wait for its full strength. It sets off as soon as the order is given, and soldiers drop what they are doing and follow the captain.
- The order is canceled automatically when the target city is taken or stops being an enemy.
- After loading a save or reloading the game, attack orders are lost and the army returns to its previous behavior.

**Garrison**

- The army moves to the city and waits near the center. When enemies enter the city, the army fights them.
- A garrisoning army is not sent elsewhere, and its soldiers do not wander off.
- Garrison orders are kept in saves. The order is canceled automatically when the city is lost or the kingdom is no longer friendly.

**Camp**: The same as garrison, but in the open. The order is canceled automatically if the spot becomes water or mountain.

## How to Give Orders

### Box Selection

This works without holding a power.

- **Zoomed out**: Drag a box with the left mouse button to select the armies whose banners are inside it. If the box contains armies of several kingdoms, only the kingdom with the most armies is selected.
- **Zoomed in**: Select soldiers with the vanilla box selection, which selects the armies they belong to.
- Clicking a single army banner also selects that army.
- With armies selected, **right-click** a city or open ground to give an order, following the table above. If all selected armies are garrisoned at the place clicked, the right-click cancels all of their orders.
- **Esc** clears the selection. With no army selected, right-click works as in vanilla.

### Powers

- **Command Army**: Click an army (its banner or a soldier nearby), then click the target.
- **Kingdom Offensive**: Click a kingdom (by clicking one of its cities), then click the target city. If the target is a foreign city, all of that kingdom's armies attack it. If it is a city of that kingdom or a friend, all of its armies garrison there.
- **Hand Back Command**: Click a kingdom, and all its armies that follow the player's orders are released back to the AI. While this power is held, the map shows where those armies are heading.

## Map Indicators

- Once armies are selected, an arrow is drawn from the army to the mouse. The city under the mouse is colored by the order: red for attack, green for garrison, white for cancel.
- Armies that follow the player's orders show their headcount on the banner: red when attacking, green when garrisoning or camping. The army's tooltip and window have an extra "Orders" row.

## Crossing the Sea

When the target is on another continent, the army calls ships automatically. Ships leave from a port near the army that can reach the sea; if no transport is free nearby, one is built at the port. A kingdom with no port cannot cross the sea, and a message says that no transport is available.

## Reinforcements

In vanilla, new recruits join an army only when the captain is back in the army's home city. Now recruits go to join the army whenever the captain is in any city of the kingdom and on the same continent as the recruits.
