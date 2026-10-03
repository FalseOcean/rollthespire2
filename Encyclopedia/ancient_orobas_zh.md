# 欧洛巴斯：分支候选、{{relic:SEA_GLASS}}与条件选项

欧洛巴斯有三个遗物位置：第一池的特殊候选先由分支决定，第二池固定，第三池依赖当前持有的初始遗物与卡牌。以下概率以 0.111.0 原版全解锁为基础，环境说明见 [[reading-guide]]。

## 第一位置：先决定特殊候选，再三选一

{{relic:ELECTRIC_SHRYMP}}和{{relic:GLASS_EYE}}始终参与。第三件候选有 **1/3** 概率为{{relic:PRISMATIC_GEM}}，**2/3** 为{{relic:SEA_GLASS}}，然后从三件中等概率选一件。

| 遗物 | 最终提供概率 |
| --- | --- |
| {{relic:ELECTRIC_SHRYMP}} | 1/3，约 33.33% |
| {{relic:GLASS_EYE}} | 1/3，约 33.33% |
| {{relic:SEA_GLASS}} | 2/9，约 22.22% |
| {{relic:PRISMATIC_GEM}} | 1/9，约 11.11% |

因此总体可能出现四件，却不是四选一；{{relic:SEA_GLASS}}是{{relic:PRISMATIC_GEM}}的两倍常见。

### {{relic:SEA_GLASS}}还随机指定角色

游戏先从已解锁的其他角色中抽取一个角色；若第一位置抽中{{relic:SEA_GLASS}}，它使用该目标。默认五角色全解锁时排除当前角色，剩下四个目标，**{{relic:SEA_GLASS}}且指定角色**的概率为 **2/9 × 1/4 = 1/18，约 5.56%**。

RT2 可以要求{{relic:SEA_GLASS}}的指定角色目标。解锁集合改变时，候选角色和这项概率也会改变；选项身份、角色目标和领取后的效果是不同层次。

## 第二位置：固定四选一

{{relic:ALCHEMICAL_COFFER}}、{{relic:DRIFTWOOD}}、{{relic:RADIANT_PEARL}}、{{relic:SAND_CASTLE}}各 **25%**，没有额外出现条件。

## 第三位置：初始遗物和专属牌还在吗

{{relic:TOUCH_OF_OROBAS}}要求仍持有可强化的初始遗物。原版对应关系为：

| 初始遗物 | 强化遗物 |
| --- | --- |
| {{relic:BURNING_BLOOD}} | {{relic:BLACK_BLOOD}} |
| {{relic:RING_OF_THE_SNAKE}} | {{relic:RING_OF_THE_DRAKE}} |
| {{relic:DIVINE_RIGHT}} | {{relic:DIVINE_DESTINY}} |
| {{relic:BOUND_PHYLACTERY}} | {{relic:PHYLACTERY_UNBOUND}} |
| {{relic:CRACKED_CORE}} | {{relic:INFUSED_CORE}} |

{{relic:ARCHAIC_TOOTH}}要求牌组中仍有对应专属初始卡牌：

| 初始卡牌 | 替换结果 |
| --- | --- |
| {{card:BASH}} | {{card:BREAK}} |
| {{card:NEUTRALIZE}} | {{card:SUPPRESS}} |
| {{card:UNLEASH}} | {{card:PROTECTOR}} |
| {{card:FALLING_STAR}} | {{card:METEOR_SHOWER}} |
| {{card:DUALCAST}} | {{card:QUADCAST}} |

失去相应初始遗物，或移除、变化相应卡牌后，该选项不再有合法目标。两个选项都可用时各 **50%**；只有一个可用时必定提供它；两个都不可用时第三位置显示锁定选项，不会换成其他遗物。

RT2 使用到达欧洛巴斯时的这两项资格重建第三池。角色名称本身不能代替当前持有状态，第三位置也不能同时提供两件遗物。资格前提见 [[ancient]]，共同随机原理见 [[why-predictable]]。
