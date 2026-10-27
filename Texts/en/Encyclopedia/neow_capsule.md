# Capsules: Contents, Relic Bags, and Pickup Order

{{relic-icon:SMALL_CAPSULE}} offers one relic reward. {{relic-icon:LARGE_CAPSULE}} directly obtains two relics in sequence, then adds one Basic Strike and one Basic Defend for the character. Either can come directly from Neow or through {{relic:NEOWS_BONES}}.

## Generating the Contents

The player's **Rewards RNG** rolls relic rarity, then the game pulls a legal relic from the front of the current bag for that rarity. Normal odds are **50% Common, 33% Uncommon, and 17% Rare**; exhausted bags or ineligible candidates require the bag's continuation rules. See [[why-predictable]] for the shared random principle.

Opening bags have just been created and can be recovered from the seed. Capsules nevertheless consume their relics. The Relics page shows the **complete initial bags before Capsule pickups and other effects**; initial positions are not later combat or chest pickup ordinals. See [[relics]] for bag rules.

## Order Inside Bones

Suppose Bones gives **{{relic:KALEIDOSCOPE}} + a Capsule**. Taking Kaleidoscope first consumes Rewards RNG while generating cards, so the Capsule rolls rarity from the advanced state. The opposite order generates Capsule relics first. Their contents can differ.

Order matters when the earlier effect changes random state, the relic bag, or the deck that the later effect reads. An unrelated random draw is not automatically a Capsule offset.

## Pickup Assumptions and Later Effects

RT2 filters specified relics in direct and Bones Capsules using the relevant pickup order. **Small Capsule's reward can be skipped in the game; these content filters assume it is taken.** Large Capsule obtains both relics directly, adding its Basic cards after both relics' effects resolve.

Capsule relics can immediately upgrade cards or alter other state. The current immediate-effect model supports only {{relic-icon:WHETSTONE}} and {{relic-icon:WAR_PAINT}}. When the preceding deck state can be established, single-player calculation includes their automatic effects when drawn; multiplayer currently includes those effects only when declared in the conditions. Modeled effects of held relics still affect later rewards. This is the current implementation coverage, rather than a player commitment that other immediate effects do not exist. The game still executes real effects, and necessary consequences of a declared pickup cannot be waived by this boundary. See [[neow-bones-curse]] for the upgrade effects and Bones' curse.
