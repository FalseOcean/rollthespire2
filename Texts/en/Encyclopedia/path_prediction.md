# Route Forecast: Rewards and Choices Along Your Line

Route forecast is enabled by default on the in-run map. Use the existing drawing tools to mark your intended route without a separate activation step. Node summaries show key encounters, events, and rewards. Hover over a node for its rewards and choice controls, then hover over an event option to inspect results from that predicted node's state. **Hovering previews; clicking adopts a choice.**

## The Current Act, Starting from Live Facts

Prediction covers the **Act you have actually entered**. In Acts 2 and 3, you can still draw the current route and compare Ancient options. Each Act starts from its readable live deck, relics, and RNG state; the map does not preview the next Act from the previous one. Gaps in a drawn route are never filled automatically.

As you travel, new live facts replace outcomes that have already happened while compatible future route selections and inputs remain. The tool remembers choices for individual event instances within the same run. Returning after a redraw restores matching branches and targets; stale inputs retain their meaning for adjustment instead of being reassigned to a different event or card. **Remembering a choice does not replay past actions.** A new run has separate history. History currently does not persist across application restarts.

## Defaults That Keep the Route Moving

- **Gold and relics:** Collect ordinary rewards when eligible. Keep the established order: relics that benefit later pickups, such as {{relic:BOWLER_HAT}}; ordinary relics; gold; then exceptional relics such as {{relic:WHETSTONE}} and {{relic:WAR_PAINT}}. Exceptional relics are still collected. Potion drops remain visible; the route does not automatically plan ongoing potion pickups, replacements, or use.
- **Card rewards:** Skip ordinary optional card rewards by default while keeping their offers visible. Adopt a card or answer a mandatory choice through the node controls. Displaying a card does not automatically collect it.
- **Shops:** Show stock without adding purchases. Authored purchases, removals, and existing automatic purchase effects still follow their inputs and actual effects.
- **Rest Sites:** Default to one Smith action without choosing a card. Its target stays unresolved while independent rewards continue. Optionally select a target in Prediction inputs to refine the result. If no card can upgrade or Smith is unavailable, continue without substituting Rest or Dig. Authored choices take precedence.

The forecast focuses on valuable route information without requiring a complete future deck. The **Prediction inputs** panel retains combat assumptions, shopping, Rest Site actions, and event details. There is no More or Diagnostics entrance. Main-panel choices focus on rewards; specific Smith targets remain optional inputs. Map choices edit the prediction plan.

## Events: Keep Important Choices, Omit Routine Steps

Branches with different public RNG consumption or important rewards retain their choices. Gold changes alone, ordinary upgrade/enchantment details, and low-value loops do not require step-by-step input. Unknown gold leaves independent rewards available; an event condition or shop purchase that actually depends on it asks for the source choice there. Content proven independent of the unresolved balance remains viewable. Deterministic confirmation pages after an adopted business choice continue automatically. **An omitted branch is not a chosen Leave option**, and does not mean zero consumption: proven equal consumption still advances the real stream while unresolved gold and card changes stay unknown. Prediction inputs retain optional detailed choices.

**Fake Merchant has a separate default: no purchases and no combat.** Holding a Foul Potion does not automatically throw it. Choose a purchase or combat explicitly at that node when needed.

Events ask for current or maximum HP when it changes a random pool, draw, or later random process; fixed damage and healing do not require an extra HP declaration. Gold balances still matter for later events and shopping. Unclaimed optional card offers default to Skip, so {{relic:LUCKY_FYSH}} does not grant speculative gold. An explicit card receipt applies its real gain. Mandatory or direct card additions retain their actual effects.

Events that need potion information accept an **event-local potion hypothesis**. Candidates can include currently held potions and potions that might have been obtained earlier. Several available candidates do not establish that you hold them together on arrival. Enter the relevant capacity, order, or empty slots. These inputs do not become a potion inventory plan for the whole route.

**Endless Conveyor** combines empty potion slots and missing HP in one confirmation, then displays the adopted values. Edited numbers require a new confirmation; results continue using the last adopted premise. This input does not require a full current/max HP pair. Card changes such as upgrades also name the affected cards beside their artwork.

Actual Ancient offers can be adopted as route premises before entering their node. Orobas's Sea Glass and Neow's optional single-card selections default to taking no cards; {{relic:CLAWS}} defaults to no targets. Independent rewards continue, while Prediction inputs retain the choices. Skipping optional cards does not omit compulsory effects of the gift. Health effects unrelated to random generation are displayed without requiring HP input. Actual HP, death and revival consumption remain unconfirmed, with dependent later information marked unresolved. Required child choices appear on the current panel; transform targets and card-pool character details stay below their own gift.

{{relic:PAELS_TOOTH}} does not require specific storage targets before continuing. A proven stored count advances its post-combat draws; removed and returned card identities remain unresolved and can be supplied in Prediction inputs. If the count is also unknown, only dependent results become unresolved while the route continues.

{{relic:SCROLL_BOXES}} can continue with its bundle unspecified. The two bundles' common random consumption and fixed acquisition effects continue, while their differences remain unresolved. Candidates stay at the source node for selection, replacement or clearing. Unspecified does not mean skipping acquisition; full path simulation still requires the actual choice.

After adopting an event branch or Ancient gift, direct cards, relics, replacements and known potion generation remain in the result panel. Transactions identify known relic costs and gold changes. Saved optional targets show their selected state; unconfirmed multi-card drafts do not change route premises.

## Uncertainty Affects Its Actual Dependents

Unknown deck targets, Niche state, upgrades, or enchantments may leave dependent card changes unresolved. Independent rewards continue. When an actual result needs input, its explanation identifies the source and affected scope. Optional precision inputs do not become a route-wide requirement merely because they are empty.

Independent rewards, relic bags, and other results continue when they do not read the unknown state. Later nodes that do depend on it show the uncertainty at the relevant point. New live deck and RNG facts from actual play provide a fresh starting boundary. This route convention does not disable current-node operation prediction.

## Scope

Route forecast supports single-player runs with or without Mods. It follows vanilla rules and omits Mod-added relic behavior and additional effects. It does not enumerate every future sequence; full path simulation and current-node operation prediction retain their own input and simulation capabilities. For seed-filter route counts, see [[map-routes|Map routes]]; for event eligibility, see [[event-appearance|Event appearance]].
