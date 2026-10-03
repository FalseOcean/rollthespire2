# 涅奥：开局如何改变后续战斗奖励？

> 机制基准：Slay the Spire 2 Beta 0.111.0

第一场战斗从**涅奥结束后的奖励状态**开始。开局既可能推进奖励随机状态（Rewards），也可能改变后面的奖励规则。

## 使用奖励随机状态的开局效果

- {{relic-icon:ARCANE_SCROLL}} 生成卡牌；{{relic-icon:HEFTY_TABLET}} 生成稀有牌奖励；{{relic-icon:KALEIDOSCOPE}} 生成其他角色的牌。
- {{relic-icon:LOST_COFFER}} 生成卡牌和药水；{{relic-icon:LEAD_PAPERWEIGHT}}、{{relic-icon:MASSIVE_SCROLL}}、{{relic-icon:SCROLL_BOXES}} 也会生成开局卡牌。
- {{relic-icon:SMALL_CAPSULE}}、{{relic-icon:LARGE_CAPSULE}} 用奖励随机状态决定所抽遗物的稀有度。

这些效果在第一场战斗前就会推进奖励随机状态。同一个种子选择 {{relic:KALEIDOSCOPE}}，可能得到与不消耗奖励随机数的开局不同的第一场奖励。

随机效果不一定使用奖励随机状态：{{relic-icon:LEAFY_POULTICE}} 的变牌主要用变牌随机状态（Transformations），{{relic-icon:NEW_LEAF}} 用其他效果共用的随机状态（Niche），{{relic-icon:PHIAL_HOLSTER}} 的药水用战斗药水生成随机状态（Combat Potion Generation）。执行这些随机过程本身不会推进奖励随机状态。

## 不消耗奖励随机数也能改变规则

{{relic-icon:SILKEN_TRESS}} 会影响接下来的第一组卡牌奖励。有 {{relic:SILKEN_TRESS}} 和没有它，即使奖励随机状态位置相同，奖励也可能不同。通过扭蛋实际获得的奖励遗物也可能增加卡牌奖励、强制药水掉落或改变内容，见 [[combat-rarity-relics]]。

因此后续奖励需要同时知道随机状态和已持有的奖励效果，而不只是随机数消耗次数。

## 查询怎样选择开局？

明确要求获得 {{relic:KALEIDOSCOPE}} 并筛第一场奖励时，RT2 先生成这个开局的卡牌，再从留下的奖励随机状态继续。{{relic-icon:NEOWS_BONES}} 中明确指定的领取顺序也会传到后续。开局条件和战斗条件必须由同一条历史满足。

没有指定涅奥时，RT2 使用不额外扰动奖励的普通开局前提，并要求真实选项中存在可确认符合这个前提的开局。它不会自动遍历所有特殊开局来替玩家争取目标奖励，也不会把已知会消耗奖励随机数或影响尚不明确的开局当作中性选择。

连续战斗的后续前提见 [[combat]]；各涅奥效果的具体结果见 [[neow]]。
