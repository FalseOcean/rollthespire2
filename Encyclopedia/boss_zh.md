# 首领与幕变体

> 适用版本：**Slay the Spire 2 Beta 0.111.0**

## 需求

玩家可能希望筛选：

> 第一幕进入指定变体；
> 某一幕出现指定首领；
> A10 最后一幕的两个首领满足指定组合。

这些结果在一局开始时就已经由 Seed 决定，不需要真正打到首领房才能知道。

---

# 幕变体

STS2 的同一幕位置可以拥有不同的幕变体。

在 0.111.0 中，当前真正存在随机变体的是 **第一幕**：

| 位置 | 变体 |
| --- | --- |
| 第一幕 | {{act:OVERGROWTH}} / {{act:UNDERDOCKS}} |
| 第二幕 | {{act:HIVE}} |
| 第三幕 | {{act:GLORY}} |

不同变体拥有自己的：

> 敌人、事件、首领和其他世界内容。

因此选择 {{act:OVERGROWTH}} 还是 {{act:UNDERDOCKS}}，并不只是地图外观不同，而是直接换了一套幕内容。

---

# 第一幕变体怎样决定？

游戏会先取得当前已经解锁的第一幕候选。

正常情况下，如果 {{act:OVERGROWTH}} 和 {{act:UNDERDOCKS}} 都可以参与随机，就从两个候选中选择一个。

因此在默认全解锁、两者都已经发现的情况下：

> **{{act:OVERGROWTH}}：1/2**
> **{{act:UNDERDOCKS}}：1/2**

## 新解锁的变体可能优先出现

单人游戏中，如果某个已经解锁的非默认幕：

> **还没有被玩家发现过**

游戏会优先选择它，而不是进行普通随机。

所以刚刚解锁 {{act:UNDERDOCKS}} 时，它可能被直接安排出来。

等它已经进入发现记录以后，才恢复正常的第一幕随机选择。

如果玩家在开局设置中直接指定第一幕，则以玩家指定的结果为准，不再把它视为一个需要筛选的随机结果。

---

# 首领属于对应的幕变体

每一幕都有自己的首领池。

### {{act:OVERGROWTH}}

- {{encounter:VANTOM_BOSS}}
- {{encounter:CEREMONIAL_BEAST_BOSS}}
- {{encounter:THE_KIN_BOSS}}

### {{act:UNDERDOCKS}}

- {{encounter:WATERFALL_GIANT_BOSS}}
- {{encounter:SOUL_FYSH_BOSS}}
- {{encounter:LAGAVULIN_MATRIARCH_BOSS}}

### {{act:HIVE}}

- {{encounter:THE_INSATIABLE_BOSS}}
- {{encounter:KNOWLEDGE_DEMON_BOSS}}
- {{encounter:KAISER_CRAB_BOSS}}

### {{act:GLORY}}

- {{encounter:QUEEN_BOSS}}
- {{encounter:TEST_SUBJECT_BOSS}}
- {{encounter:AEONGLASS_BOSS}}

正常情况下，会从当前这一幕的三个首领中选择一个：

> **各 1/3。**

因此第一幕的首领身份天然依赖第一幕变体。

例如：

> {{encounter:WATERFALL_GIANT_BOSS}} 属于 {{act:UNDERDOCKS}}；

所以：

> {{act:OVERGROWTH}} + {{encounter:WATERFALL_GIANT_BOSS}}

并不是“特别稀有”，而是一个不可能的组合。

---

# 首领的随机不是独立于世界生成的

从玩家角度，可以把首领理解成：

> 当前这一幕的三个首领中随机选一个。

但游戏生成世界时，首领与事件、普通遭遇、精英等内容共享同一段持续推进的世界随机状态。

因此变体发生变化以后，不只是首领候选池改变：

> 前面世界生成的内容和随机消费也会改变。

所以首领身份应该理解为：

> **当前这一幕变体对应的完整世界生成结果的一部分。**

不过这些生成都发生在开局阶段，因此首领仍然可以很早被预测。

---

# A10 的双首领

在 **A10** 下，最后一幕会出现第二个首领。

规则很直接：

> 先正常选择第一个首领；

然后：

> 从剩余两个首领中选择第二个。

因此第二个首领不会和第一个重复。

例如 {{act:GLORY}}：

> 第一首领：3 选 1
> 第二首领：剩余 2 选 1

如果要求一个具体的有序组合，例如：

> {{encounter:QUEEN_BOSS}} → {{encounter:AEONGLASS_BOSS}}

基础概率就是：

> **1/3 × 1/2 = 1/6**

这里顺序有意义，因为游戏明确区分第一首领和第二首领。

---

# 我们是如何做的？

RT2 会先确定实际的幕变体，再从这个变体对应的世界生成中恢复首领身份。

因此可以分别筛选：

> 指定幕变体；

> 指定首领；

或者同时要求：

> **指定变体 + 指定首领。**

A10 时，还可以继续指定最后一幕的第一和第二首领。

所以这两项放在一起的原因很简单：

> **幕变体决定使用哪套首领池，首领再从这套世界内容中产生。**
