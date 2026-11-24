# Crystal Sphere: In-run Board and Reward Guidance

RT2 has offered in-run Crystal Sphere prediction since 1.3.3. The current assistant reads **the local player's Crystal Sphere scene** in solo or multiplayer runs, finds compatible rewards and provides manual reveal and reward guidance. It does not plan a complete future route or coordinate teammates' actions.

## Read the scene and choose reveals

Enable the display under “In-run tools button” in Settings, then use the Bones icon at the top right during Crystal Sphere. Before starting the board, compare the **paid three-reveal** and **debt six-reveal** branches. Once started, continue from the current board and its **remaining reveals**.

“Avoid board curses” constrains curses uncovered on the board. It does not remove the entry curse from the debt branch.

## Choose compatible rewards

Click a discovered reward to add a target without waiting for computation to finish. Further options are checked against the current selection: individually obtainable rewards may not be obtainable together. Normal and upgraded cards are displayed and verified as separate targets, so check the variant you select.

Card targets use the original rewards generated from your state when you open the assistant. This includes eggs you already own, natural upgrades, and enchantments when exact prediction is enabled. {{relic:WHETSTONE}}, {{relic:WAR_PAINT}} or eggs obtained during this event do not create additional upgraded targets. Their actual effects still apply, and a later upgrade alone does not invalidate a target that matched the original reward.

“Exact enchantments” is initially off: targets use card identity and upgrade level without restricting enchantments. Enable it to distinguish **unenchanted cards** from **specific enchantments and amounts**. It is independent of the Driftwood rerolls switch. Changing it clears targets and restarts computation.

You can pause and continue computing. Pausing retains current results but does not make them complete. **Not yet found and excluded are different states**: an absent target or a pending combination is not proof of impossibility. A message that the current candidates are resolved concerns compatible rewards, rather than every reveal plan.

Click a gray candidate to focus its query. Changing targets reuses valid work; closing and reopening the panel retains the selection and existing plans while the same event scene remains unchanged.

With no rewards selected, the assistant seeks the most gold and lets you start board guidance directly. With targets selected, gold optimization preserves them. Plans distinguish their current gold from a proven maximum: only reaching a sound upper bound or exhausting the search earns that claim. Pausing retains the best plan found. Completing reward candidates does not complete gold optimization.

## Follow the plan manually

After choosing targets and starting guidance, select the corresponding reveal-count branch in the vanilla event. Switch tools and click the marked cells yourself, revealing in the indicated order. Guidance displays the next step and cell markers; it does not click or collect rewards for you.

When you own {{relic:DRIFTWOOD}}, “Driftwood rerolls” is initially enabled; you can turn this option off. After revealing, reroll the indicated reward groups and use Skip after each group to return to the reward list. Once rerolls are complete, collect the planned rewards. Not every group needs a reroll. Plans do not arrange collecting a relic first to change later card targets.

When you own {{relic:PAELS_WING}}, the assistant uses your current sacrifice count to find sacrifice relics obtainable alongside your selected cards. Relics available through sacrifice show a small Pael's Wing icon at the top left. Each sacrifice consumes an entire card reward group, and every two sacrifices produce a relic. You cannot both take a card and sacrifice the same group. Follow the indicated order of taking rewards and sacrificing groups; do not collect other rewards early. Joint Driftwood and Pael's Wing solving is currently unsupported, and the vanilla limit on reward alternative buttons still applies.

Entering the selected three- or six-reveal board normally preserves the plan. Collecting an ordinary relic does not stop guidance solely because inventory changed. If actual cards, upgrades, event RNG or actions differ from the plan, guidance still stops; read the scene again and solve anew.

## Display options and scope

Active guidance displays the current step. The **translucent mask is a separate option, initially off**; enabling it does not automatically start guidance. Your choices for “Avoid board curses”, “Driftwood rerolls”, “Exact enchantments” and “Translucent mask” are saved and reused in later events.

Ordinary mode predicts from the local player's captured state. When an exact enchantment target depends on shared random state, changes to that state invalidate its guidance and require a fresh capture; teammates' actions can matter in multiplayer. Unknown mod content is tried using ordinary properties; special effects may cause differences. Computation or interface problems can be reported directly. See [[faq|FAQ]] for result questions and [[reading-guide|Reading and scope guide]] for environment details.
