# Crystal Sphere: In-run Board and Reward Guidance

RT2 has offered in-run Crystal Sphere prediction since 1.3.3. The current assistant reads **the local player's Crystal Sphere scene** in solo or multiplayer runs, finds compatible rewards and provides manual reveal and reward guidance. It does not plan a complete future route or coordinate teammates' actions.

## Read the scene and choose reveals

Enable the in-run prediction shortcut in Settings, then use the Bones icon at the top right during Crystal Sphere. Before starting the board, compare the **paid three-reveal** and **debt six-reveal** branches. Once started, continue from the current board and its **remaining reveals**.

“Avoid board curses” constrains curses uncovered on the board. It does not remove the entry curse from the debt branch.

## Choose compatible rewards

Click a discovered reward to add a target without waiting for computation to finish. Further options are checked against the current selection: individually obtainable rewards may not be obtainable together. Normal and upgraded cards are displayed and verified as separate targets, so check the variant you select.

“Include enchantments” is off by default: targets use card identity and upgrade level without restricting enchantments. Enable it to distinguish **unenchanted cards** from **specific enchantments and amounts**. It is independent of the Driftwood switch. Changing it clears targets and restarts computation; reopening the panel in the same event retains the setting.

You can pause and continue computing. Pausing retains current results but does not make them complete. **Not yet found and excluded are different states**: an absent target or a pending combination is not proof of impossibility. A message that the current candidates are resolved concerns compatible rewards, rather than every reveal plan.

Click a gray candidate to focus its query. Changing targets reuses valid work; closing and reopening the panel retains the selection and existing plans while the same event scene remains unchanged.

Existing plans show their gold result, and computation may improve a plan, but it does not guarantee globally maximum gold.

## Follow the plan manually

After choosing targets and starting guidance, select the corresponding reveal-count branch in the vanilla event. Switch tools and click the marked cells yourself, revealing in the indicated order. Guidance displays the next step and cell markers; it does not click or collect rewards for you.

When you own {{relic:DRIFTWOOD}}, prediction includes its permitted reward rerolls by default; you can turn this option off. After revealing, reroll the indicated reward groups and use Skip after each group to return to the reward list. Once rerolls are complete, collect the planned rewards. Not every group needs a reroll. Plans do not arrange collecting a relic first to modify later card rewards.

Entering the selected three- or six-reveal board normally preserves the plan. Collecting an ordinary relic does not stop guidance solely because inventory changed. If actual cards, upgrades, event RNG or actions differ from the plan, guidance still stops; read the scene again and solve anew.

## Display options and scope

Active guidance displays the current step. The **translucent mask is a separate option, off by default**; enabling it does not automatically start guidance.

Ordinary mode predicts from the local player's captured state. When an exact enchantment target depends on shared random state, changes to that state invalidate its guidance and require a fresh capture; teammates' actions can matter in multiplayer. Unknown mod content is tried using ordinary properties; special effects may cause differences. {{relic:WHETSTONE}} and {{relic:WAR_PAINT}} pickup upgrade order is outside the current guidance promise. Computation or interface problems can be reported directly. See [[faq|FAQ]] for result questions and [[reading-guide|Reading and scope guide]] for environment details.
