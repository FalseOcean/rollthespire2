# 欧洛巴斯：分支候选、{{relic:SEA_GLASS}} 与条件选项

> 适用版本：**Slay the Spire 2 Beta 0.111.0**

## 需求

欧洛巴斯会提供三个先古遗物位置。

它的三个位置各有自己的规则：

- **第一位置**：{{relic:ELECTRIC_SHRYMP}}、{{relic:GLASS_EYE}}，以及 {{relic:SEA_GLASS}} / {{relic:PRISMATIC_GEM}} 中的一件
- **第二位置**：固定四选一
- **第三位置**：{{relic:TOUCH_OF_OROBAS}} 与 {{relic:ARCHAIC_TOOTH}}，根据玩家当前状态决定谁有资格出现

其中第一和第三位置都不是简单的固定候选池，这也是欧洛巴斯最值得了解的地方。

---

# 第一位置：{{relic:SEA_GLASS}} 与 {{relic:PRISMATIC_GEM}}

第一位置始终有两个基础候选：

- {{relic:ELECTRIC_SHRYMP}}
- {{relic:GLASS_EYE}}

除此之外，游戏还会先决定这一轮加入哪个特殊候选：

> **1/3：{{relic:PRISMATIC_GEM}}**
> **2/3：{{relic:SEA_GLASS}}**

然后再从这三个候选中选择最终的第一项。

所以四件遗物虽然都会出现在第一位置，却并不是简单的四选一。

最终概率为：

| 遗物 | 出现概率 |
| --- | --- |
| {{relic:ELECTRIC_SHRYMP}} | **1/3 ≈ 33.33%** |
| {{relic:GLASS_EYE}} | **1/3 ≈ 33.33%** |
| {{relic:SEA_GLASS}} | **2/9 ≈ 22.22%** |
| {{relic:PRISMATIC_GEM}} | **1/9 ≈ 11.11%** |

{{relic:SEA_GLASS}} 因此正好是 {{relic:PRISMATIC_GEM}} 的两倍常见。

---

## {{relic:SEA_GLASS}} 还会指定一个角色

{{relic:SEA_GLASS}} 不只有“出现或不出现”这一层结果。

欧洛巴斯还会从玩家已经解锁的**其他角色**中随机选定一个目标。

如果最终第一位置生成 {{relic:SEA_GLASS}}，它就会对应这个已经选好的角色。

在默认全解锁的原版环境下，目前一共有 5 名角色。排除玩家自己以后，还有：

> **4 个可能目标**

因此，一个指定角色的 {{relic:SEA_GLASS}} 概率为：

> **2/9 × 1/4 = 1/18 ≈ 5.56%**

所以：

> “我要 {{relic:SEA_GLASS}}”

和

> “我要指向某个指定角色的 {{relic:SEA_GLASS}}”

是两个明显不同的筛选条件。

{{relic:SEA_GLASS}} 自己还有后续效果，可以在单独条目中继续介绍。

---

# 第二位置：固定四选一

第二位置最简单，候选始终是：

- {{relic:ALCHEMICAL_COFFER}}
- {{relic:DRIFTWOOD}}
- {{relic:RADIANT_PEARL}}
- {{relic:SAND_CASTLE}}

四件遗物等概率出现：

> **各 25%**

这里没有额外的出现条件。

---

# 第三位置：取决于你当前还拥有什么

第三位置只有两个可能的先古遗物：

- {{relic:TOUCH_OF_OROBAS}}
- {{relic:ARCHAIC_TOOTH}}

但它们并不是永远都能出现。

游戏会根据玩家抵达欧洛巴斯时的实际状态，先判断两件遗物有没有可作用的目标。

---

## {{relic:TOUCH_OF_OROBAS}}

{{relic:TOUCH_OF_OROBAS}} 会强化玩家的角色初始遗物。

0.111.0 原版五名角色分别对应：

| 初始遗物 | 强化后 |
| --- | --- |
| {{relic:BURNING_BLOOD}} | {{relic:BLACK_BLOOD}} |
| {{relic:RING_OF_THE_SNAKE}} | {{relic:RING_OF_THE_DRAKE}} |
| {{relic:DIVINE_RIGHT}} | {{relic:DIVINE_DESTINY}} |
| {{relic:BOUND_PHYLACTERY}} | {{relic:PHYLACTERY_UNBOUND}} |
| {{relic:CRACKED_CORE}} | {{relic:INFUSED_CORE}} |

因此，只要玩家来到欧洛巴斯时仍然保留着对应的初始遗物，{{relic:TOUCH_OF_OROBAS}} 就有资格出现。

如果初始遗物已经因为此前的游戏过程消失，{{relic:TOUCH_OF_OROBAS}}也会随之离开候选池。

这里看的不是“你是什么角色”，而是：

> **你现在是否真的还拥有可以被 {{relic:TOUCH_OF_OROBAS}}强化的初始遗物。**

---

## {{relic:ARCHAIC_TOOTH}}

{{relic:ARCHAIC_TOOTH}} 则关注角色的一张代表性初始牌：

| 初始牌 | 转变结果 |
| --- | --- |
| {{card:BASH}} | {{card:BREAK}} |
| {{card:NEUTRALIZE}} | {{card:SUPPRESS}} |
| {{card:UNLEASH}} | {{card:PROTECTOR}} |
| {{card:FALLING_STAR}} | {{card:METEOR_SHOWER}} |
| {{card:DUALCAST}} | {{card:QUADCAST}} |

如果这张牌仍然在当前牌组中，{{relic:ARCHAIC_TOOTH}} 就有资格出现。

如果它已经被删除或变化掉，{{relic:ARCHAIC_TOOTH}} 也就没有可以作用的目标，因此不会进入候选池。

所以这里同样看的是：

> **抵达欧洛巴斯时的真实牌组。**

而不是玩家开局时曾经拥有什么。

---

# 第三位置最终会怎样？

如果 {{relic:TOUCH_OF_OROBAS}}和{{relic:ARCHAIC_TOOTH}} 都满足条件：

> 两件遗物各 **50%**。

如果只有其中一件满足条件：

> 那一件会成为第三位置的唯一先古遗物。

如果两件都不满足：

> 第三个位置不会再补入其他先古遗物，而会显示一个锁定选项。

所以第三位置的随机本身很简单。

真正重要的是：

> **先确定当前状态允许哪些遗物出现。**

---

# RT2 怎么理解欧洛巴斯？

欧洛巴斯的筛选可以分成两个层次。

首先，玩家需要告诉 RT2 与第三位置有关的当前状态：

> {{relic:TOUCH_OF_OROBAS}} 是否仍有合法目标；
> {{relic:ARCHAIC_TOOTH}} 是否仍有合法目标。

这些信息用来还原这一局真实的第三候选池。

然后玩家再指定自己真正希望看到的先古遗物。

对于 {{relic:SEA_GLASS}}，还可以继续要求：

> **它必须指向某个指定角色。**

因此欧洛巴斯最值得记住的其实只有三个特点：

> **第一位置里，{{relic:SEA_GLASS}} 和 {{relic:PRISMATIC_GEM}} 会先竞争“特殊候选”的位置。**

> **{{relic:SEA_GLASS}} 自己还带有一个随机的其他角色目标。**

> **第三位置取决于玩家当前是否仍保留对应的初始遗物和初始牌。**

相比固定候选池的先古，欧洛巴斯更依赖玩家此时真实的游戏状态，但它的规则本身仍然很清晰。
