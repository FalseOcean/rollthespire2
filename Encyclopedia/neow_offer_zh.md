# 涅奥选项与{{relic:NEOWS_BONES}}

普通原版开局会给出三个遗物选项：**先抽一件带代价的遗物，再从另一个候选池中选出两件。** 三个选项并不是从所有开局遗物中统一抽取。以下池和概率以 0.111.0 原版全解锁单人为基础；通用环境说明见 [[reading-guide]]。有改变涅奥选项的游戏修正时，需要使用相应规则。

## 带代价的遗物选项

这些遗物的代价各不相同，并不是都要获得诅咒。例如{{relic:LEAFY_POULTICE}}减少最大生命，{{relic:SILKEN_TRESS}}失去所有金币，{{relic:LARGE_CAPSULE}}则额外加入一张打击和一张防御。这个候选池共有十件遗物：

- {{relic:CURSED_PEARL}}
- {{relic:DOWSING_ROD}}
- {{relic:HEFTY_TABLET}}
- {{relic:LARGE_CAPSULE}}
- {{relic:LEAFY_POULTICE}}
- {{relic-icon:NEOWS_BONES}}
- {{relic:NEOWS_SACRIFICE}}
- {{relic:PRECARIOUS_SHEARS}}
- {{relic:SILKEN_TRESS}}
- {{relic:SILVER_CRUCIBLE}}

从当前合法候选中抽取一件。上述默认环境中十件都可用，指定一件出现在这组选项中的概率是 **1/10**。多人中{{relic:SILVER_CRUCIBLE}}不可用，不能照搬十件池的概率。

## 另外两个选项怎样生成

先抽出的带代价遗物会排除另一个候选池中与它冲突的遗物：

| 先抽出的遗物 | 从另一个候选池排除的遗物 |
| --- | --- |
| {{relic:CURSED_PEARL}} | {{relic:GOLDEN_PEARL}} |
| {{relic:HEFTY_TABLET}} | {{relic:ARCANE_SCROLL}} |
| {{relic:LEAFY_POULTICE}} | {{relic:NEW_LEAF}} |
| {{relic:PRECARIOUS_SHEARS}} | {{relic:PRECISE_SCISSORS}} |
| {{relic:NEOWS_SACRIFICE}} | {{relic:PHIAL_HOLSTER}}、{{relic:LOST_COFFER}} |

另外三对遗物各先二选一，被选中的一件加入这个候选池：{{relic:LAVA_ROCK}} / {{relic:SMALL_CAPSULE}}、{{relic:NUTRITIOUS_OYSTER}} / {{relic:STONE_HUMIDIFIER}}、{{relic:NEOWS_TALISMAN}} / {{relic:POMANDER}}。先抽出的遗物为{{relic:LARGE_CAPSULE}}时，第一对不会加入。

随后按模式和解锁状态移除不合法候选，再洗牌取前两件。例如{{relic:MASSIVE_SCROLL}}仅多人可用，{{relic:WINGED_BOOTS}}仅单人可用，{{relic:KALEIDOSCOPE}}要求角色卡池全部解锁，{{relic:SCROLL_BOXES}}也有自己的可用条件。因此另外两个选项的概率还取决于先抽出的带代价遗物和三对二选一结果，不能简单使用“3 ÷ 全部涅奥遗物数”。

## 骨骰的两件内部遗物

看到骨骰只表示可以选择它；内部效果在实际领取后开始。骨骰把所有当前允许的涅奥遗物作为候选，排除自身，用玩家的 **Rewards RNG** 洗牌取两件不同遗物。它不沿用顶层选项的“两件加一件”分组方式，也不沿用其中的冲突排除。

默认单人全解锁环境有 **28 件内部候选**。已领取骨骰时，指定一件出现在任一位置的概率是 **1/14**；指定两件不同遗物、不限顺序的概率是 **1/378**。再乘顶层骨骰的 **1/10**，分别约为 **1/140** 和 **1/3,780**。

## 为什么领取顺序也要指定

内部遗物依次领取并执行效果。如果前一件消耗了后一件会读取的随机状态，或改变了它需要的牌组、遗物袋、待触发效果，**甲 → 乙**和**乙 → 甲**就可能不同。RT2 可以按不限定或指定领取顺序筛选。

例如{{relic:KALEIDOSCOPE}}先生成卡牌会推进扭蛋稀有度使用的 Rewards；{{relic:SILKEN_TRESS}}先领取会影响随后生成的卡牌奖励。详情见 [[neow-capsule]]、[[neow-kaleidoscope]]和 [[neow-silken-tress]]。骨骰在两件效果之后生成的最终诅咒见 [[neow-bones-curse]]；共同随机原理见 [[why-predictable]]。
