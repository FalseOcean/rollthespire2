# 佩尔：三个候选池与不均匀权重

> 适用版本：**Slay the Spire 2 Beta 0.111.0**

## 需求

佩尔会提供三个先古遗物选项。

这三个位置分别从自己的候选池中产生：

> **第一位置：固定三选一**
> **第二位置：根据牌组状态变化，并且存在特殊权重**
> **第三位置：取决于玩家当前是否已经拥有事件宠物**

因此佩尔真正需要注意的并不是三个选项本身，而是：

> **第二、第三个候选池在这一局究竟长什么样。**

---

# 第一位置：固定三选一

第一位置始终是：

- {{relic:PAELS_FLESH}}
- {{relic:PAELS_HORN}}
- {{relic:PAELS_TEARS}}

三件遗物没有额外出现条件，概率完全相同：

> **各 1/3。**

这是佩尔最简单的一个位置。

---

# 第二位置：带权重的候选池

第二位置可能出现四件遗物：

- {{relic:PAELS_WING}}
- {{relic:PAELS_CLAW}}
- {{relic:PAELS_TOOTH}}
- {{relic:PAELS_GROWTH}}

但它们并不是简单的四选一。

**{{relic:PAELS_WING}} 永远存在。**

另外两件遗物需要满足对应的牌组条件。

### {{relic:PAELS_CLAW}}

当前牌组中至少需要有：

> **3 张可以被 Goopy 附魔的防御牌。**

满足以后，{{relic:PAELS_CLAW}} 才会进入候选池。

### {{relic:PAELS_TOOTH}}

当前牌组中至少需要有：

> **5 张可以正常被移除的牌。**

满足以后，{{relic:PAELS_TOOTH}} 才会进入候选池。

---

## 为什么 {{relic:PAELS_GROWTH}} 的概率更低？

佩尔的第二池有一个很特别的权重规则。

{{relic:PAELS_WING}}，以及当前满足条件的 {{relic:PAELS_CLAW}} / {{relic:PAELS_TOOTH}}，都会拥有 **2 份权重**。

而：

> **{{relic:PAELS_GROWTH}} 永远只有 1 份权重。**

所以第二池可以简单记成：

> **普通有效候选 ×2 + {{relic:PAELS_GROWTH}} ×1**

最终会形成：

| 当前条件 | 第二位置各遗物的概率 |
| --- | --- |
| {{relic:PAELS_CLAW}}、{{relic:PAELS_TOOTH}} 都不可用 | {{relic:PAELS_WING}} **2/3** · {{relic:PAELS_CLAW}} 0 · {{relic:PAELS_TOOTH}} 0 · {{relic:PAELS_GROWTH}} **1/3** |
| 只有 {{relic:PAELS_CLAW}} 可用 | {{relic:PAELS_WING}} **2/5** · {{relic:PAELS_CLAW}} **2/5** · {{relic:PAELS_TOOTH}} 0 · {{relic:PAELS_GROWTH}} **1/5** |
| 只有 {{relic:PAELS_TOOTH}} 可用 | {{relic:PAELS_WING}} **2/5** · {{relic:PAELS_CLAW}} 0 · {{relic:PAELS_TOOTH}} **2/5** · {{relic:PAELS_GROWTH}} **1/5** |
| {{relic:PAELS_CLAW}}、{{relic:PAELS_TOOTH}} 都可用 | {{relic:PAELS_WING}} **2/7** · {{relic:PAELS_CLAW}} **2/7** · {{relic:PAELS_TOOTH}} **2/7** · {{relic:PAELS_GROWTH}} **1/7** |

所以 {{relic:PAELS_GROWTH}} 并不是因为自身有什么特殊“稀有度”，而是它在这个候选池中的**权重只有其他有效选项的一半**。

---

# 第三位置：事件宠物

第三位置基础有两件遗物：

- {{relic:PAELS_EYE}}
- {{relic:PAELS_BLOOD}}

如果玩家当前还**没有事件宠物（事件宠物）**，则会额外加入：

- {{relic:PAELS_LEGION}}

因此：

### 没有事件宠物

> {{relic:PAELS_EYE}}：**1/3**
> {{relic:PAELS_BLOOD}}：**1/3**
> {{relic:PAELS_LEGION}}：**1/3**

### 已经拥有事件宠物

{{relic:PAELS_LEGION}} 不再进入候选池，只剩：

> {{relic:PAELS_EYE}}：**1/2**
> {{relic:PAELS_BLOOD}}：**1/2**

所以“有没有事件宠物”会直接改变佩尔的第三个位置。

---

## 第一幕就可能已经拥有事件宠物

这里的“事件宠物”并不只指已经跟在玩家身边的宠物。

在第一幕的**密林**中，有一个 **{{event:BYRDONIS_NEST}}** 事件。

玩家可以选择带走：

> **{{card:BYRDONIS_EGG}}（多尼斯异鸟的蛋）**

这张蛋会直接加入牌组。

从游戏规则上说，**只要 {{card:BYRDONIS_EGG}} 仍然在牌组中，玩家就已经被视为拥有事件宠物**。

也就是说，即使蛋还没有真正孵化：

> 密林中拿到多尼斯异鸟的蛋
> → 来到第二幕遇见佩尔
> → {{relic:PAELS_LEGION}} 就不会进入第三候选池

因此这时第三位置只剩：

> {{relic:PAELS_EYE}}
> {{relic:PAELS_BLOOD}}

两者各 **1/2**。

---

## 蛋孵化以后仍然算事件宠物

{{card:BYRDONIS_EGG}} 还会在休息点提供孵化选项。

孵化以后，玩家会获得 **{{relic:BYRDPIP}}**。

{{relic:BYRDPIP}} 本身就是一个事件宠物，因此状态不会因为蛋孵化而消失：

> **蛋还在牌组里：算事件宠物**
> **蛋已经孵化成 {{relic:BYRDPIP}}：仍然算事件宠物**

所以对佩尔来说，真正的问题不是：

> “宠物有没有已经孵出来？”

而是：

> **玩家当前是否已经拥有任何事件宠物来源。**

多尼斯异鸟的蛋正是原版中一个很容易在第一幕就触发这个状态的例子。

---

# 为什么这些条件值得填写准确？

佩尔很适合说明一个容易忽略的问题：

> **一个遗物即使不是你的筛选目标，它有没有资格出现，也可能改变你真正想要的遗物。**

例如玩家只想寻找 {{relic:PAELS_WING}}。

如果 {{relic:PAELS_CLAW}} 和 {{relic:PAELS_TOOTH}} 都不能出现，{{relic:PAELS_WING}} 的概率是：

> **2/3**

如果两者都可以出现，{{relic:PAELS_WING}} 就变成：

> **2/7**

同样，如果玩家并不关心 {{relic:PAELS_LEGION}}，但第一幕已经拿到了 {{card:BYRDONIS_EGG}}，那么第三池仍然会从：

> {{relic:PAELS_EYE}} / {{relic:PAELS_BLOOD}} / {{relic:PAELS_LEGION}}

变成：

> {{relic:PAELS_EYE}} / {{relic:PAELS_BLOOD}}

所以这些资格条件描述的是**真实候选池**，而不只是玩家想不想筛某件遗物。

---

# RT2 怎么理解佩尔？

RT2 会把这些信息分成两类：

**目标**

> 你希望佩尔实际提供哪些遗物。

**前提**

> {{relic:PAELS_CLAW}} 当前有没有资格出现；
> {{relic:PAELS_TOOTH}} 当前有没有资格出现；
> {{relic:PAELS_LEGION}} 当前有没有资格出现。

这些前提不是要求 Seed 必须命中对应遗物。

它们只是告诉 RT2：

> **这一局真正参与随机的候选池是什么。**

如果玩家知道自己第一幕已经拿到 {{card:BYRDONIS_EGG}}，或者已经通过其他方式拥有事件宠物，那么就应该把 {{relic:PAELS_LEGION}} 视为不可用。

一旦这些候选池确定下来，佩尔的三个位置本身都非常直接。

---

佩尔最值得记住的其实只有两点：

> **第二位置不是等概率池：{{relic:PAELS_WING}}、{{relic:PAELS_CLAW}}、{{relic:PAELS_TOOTH}} 各有 2 份权重，{{relic:PAELS_GROWTH}} 只有 1 份。**

> **第三位置是否包含 {{relic:PAELS_LEGION}}，取决于玩家当前有没有事件宠物；第一幕密林获得的 {{card:BYRDONIS_EGG}} 就已经算一个事件宠物。**

所以佩尔的复杂性不在于随机过程很深，而在于：

**玩家此前经历过什么，会先改变这一次随机究竟从哪些东西里抽。**
