# 欧洛巴斯：分支候选、{{relic:SEA_GLASS}}与条件选项

想找愚者的缠结玻璃，要先抽到{{relic:SEA_GLASS}}，角色目标还得正好是愚者。欧洛巴斯的三个位置各有一套规则。以下是标准模式的概率，游玩环境见 [[reading-guide]]。

## 第一位置：先决定特殊候选，再三选一

{{relic:ELECTRIC_SHRYMP}}和{{relic:GLASS_EYE}}始终参与。第三件候选有 **1/3** 概率为{{relic:PRISMATIC_GEM}}，**2/3** 为{{relic:SEA_GLASS}}，然后从三件中等概率抽一件。

| 遗物 | 最终提供概率 |
| --- | --- |
| {{relic:ELECTRIC_SHRYMP}} | 1/3，约 33.33% |
| {{relic:GLASS_EYE}} | 1/3，约 33.33% |
| {{relic:SEA_GLASS}} | 2/9，约 22.22% |
| {{relic:PRISMATIC_GEM}} | 1/9，约 11.11% |

这四件都可能见到，其中{{relic:SEA_GLASS}}是{{relic:PRISMATIC_GEM}}的两倍常见。

### {{relic:SEA_GLASS}}还随机指定角色

游戏先从已解锁的其他角色中抽一个。第一位置若抽中{{relic:SEA_GLASS}}，它就用这个角色的牌池。六角色全解锁时，排除当前角色后有五个目标，**出现海玻璃且恰好指向某个指定角色**的概率是 **2/9 × 1/5 = 2/45，约 4.44%**。用其他角色玩且已解锁愚者时，可以在 RT2 的海玻璃筛选里指定愚者，找的就是缠结玻璃。解锁不全时，按实际可抽的角色数计算。

## 第二位置：固定四选一

{{relic:ALCHEMICAL_COFFER}}、{{relic:DRIFTWOOD}}、{{relic:RADIANT_PEARL}}、{{relic:SAND_CASTLE}}各 **25%**。

## 第三位置：初始遗物和专属牌还在吗

{{relic:TOUCH_OF_OROBAS}}要求仍持有以下任一初始遗物。

| 初始遗物 | 强化遗物 |
| --- | --- |
| {{relic:BURNING_BLOOD}} | {{relic:BLACK_BLOOD}} |
| {{relic:RING_OF_THE_SNAKE}} | {{relic:RING_OF_THE_DRAKE}} |
| {{relic:DIVINE_RIGHT}} | {{relic:DIVINE_DESTINY}} |
| {{relic:BOUND_PHYLACTERY}} | {{relic:PHYLACTERY_UNBOUND}} |
| {{relic:CRACKED_CORE}} | {{relic:INFUSED_CORE}} |
| {{relic:SONG_BOW}} | {{relic:STORYTELLER}} |

{{relic:ARCHAIC_TOOTH}}则看牌组里还有没有对应的专属初始牌。

| 初始卡牌 | 替换结果 |
| --- | --- |
| {{card:BASH}} | {{card:BREAK}} |
| {{card:NEUTRALIZE}} | {{card:SUPPRESS}} |
| {{card:UNLEASH}} | {{card:PROTECTOR}} |
| {{card:FALLING_STAR}} | {{card:METEOR_SHOWER}} |
| {{card:DUALCAST}} | {{card:QUADCAST}} |
| {{card:FOOLISH_SHOT}} | {{card:DIVINE_SHOT}} |

两个目标都在时，两件遗物各有 **50%** 的概率。只留着一个目标就必定看到对应选项，两个都没了，第三位置便会锁定。例如，愚者失去了歌曲之弓但仍握有愚蠢射击，就会看到古老牙齿，可以把这张牌换成神力射击。

筛选第三位置时，按到达欧洛巴斯时的遗物和牌组填写。资格前提见 [[ancient]]，随机原理见 [[why-predictable]]。
