# 第三幕先古：总池与固定位置

> 适用版本：**Slay the Spire 2 Beta 0.111.0**

第三幕的三位普通先古分别是：

- {{event:NONUPEIPE}}
- {{event:TANX}}
- {{event:VAKUU}}

相比第二幕，它们的遗物生成规则要简单很多。

{{event:NONUPEIPE}} 和 {{event:TANX}} 都是：

> **建立一个总候选池 → 打乱 → 取前三个**

{{event:VAKUU}} 则是：

> **三个固定候选池 → 每个池各取一个**

因此这里最值得了解的，只是两个条件遗物会怎样改变候选池。

---

# {{event:NONUPEIPE}}

{{event:NONUPEIPE}} 默认拥有 9 个候选遗物：

- {{relic:BLESSED_ANTLER}}
- {{relic:BRILLIANT_SCARF}}
- {{relic:DELICATE_FROND}}
- {{relic:DIAMOND_DIADEM}}
- {{relic:FUR_COAT}}
- {{relic:GLITTER}}
- {{relic:JEWELRY_BOX}}
- {{relic:LOOMING_FRUIT}}
- {{relic:SIGNET_RING}}

游戏会把这个候选池随机打乱，然后取最前面的三个作为本次提供的选项。

因此，在没有其他条件影响时，每件基础遗物进入三个选项的概率都是：

> **3 / 9 = 1/3**

也就是约 **33.33%**。

---

## {{relic:BEAUTIFUL_BRACELET}}

{{event:NONUPEIPE}} 还有一个条件遗物：

- {{relic:BEAUTIFUL_BRACELET}}

如果当前牌组中至少有：

> **4 张可以接受 Swift 附魔的牌**

{{relic:BEAUTIFUL_BRACELET}} 就会加入候选池。

此时整个池子从 9 件变成 10 件，游戏仍然只是打乱以后取前三个。

因此：

> {{relic:BEAUTIFUL_BRACELET}} 的出现概率为 **3/10 = 30%**

与此同时，原本九件基础遗物的出现概率也会从：

> **1/3**

下降到：

> **3/10**

所以这个条件并不只影响 {{relic:BEAUTIFUL_BRACELET}} 自己。

它会改变整个 {{event:NONUPEIPE}}的候选池大小。

---

# {{event:TANX}}

{{event:TANX}} 的结构几乎相同。

默认候选池也是 9 件遗物：

- {{relic:CLAWS}}
- {{relic:CROSSBOW}}
- {{relic:IRON_CLUB}}
- {{relic:MEAT_CLEAVER}}
- {{relic:SAI}}
- {{relic:SPIKED_GAUNTLETS}}
- {{relic:TANXS_WHISTLE}}
- {{relic:THROWING_AXE}}
- {{relic:WAR_HAMMER}}

同样是：

> **打乱整个池子，然后取前三个。**

因此默认情况下，每件基础遗物进入本次提供的选项的概率也是：

> **1/3**

---

## {{relic:TRI_BOOMERANG}}

{{event:TANX}} 的条件遗物是：

- {{relic:TRI_BOOMERANG}}

如果当前牌组中至少有：

> **3 张可以接受 Instinct 附魔的牌**

{{relic:TRI_BOOMERANG}} 就会加入候选池。

这时：

> 9 件基础遗物 + {{relic:TRI_BOOMERANG}} = 10 件

然后仍然取其中三个。

所以条件成立以后，每一件候选遗物的出现概率都变成：

> **3/10 = 30%**

这和 {{event:NONUPEIPE}} 的 {{relic:BEAUTIFUL_BRACELET}} 是同一种结构：

> **条件成立不是额外生成一个第四选项，而是把新遗物加入原来的候选池。**

---

# {{event:VAKUU}}

{{event:VAKUU}} 和前两位不同。

它没有一个大的总候选池，而是拥有三个固定位置。

## 第一位置

三选一：

- {{relic:BLOOD_SOAKED_ROSE}}
- {{relic:WHISPERING_EARRING}}
- {{relic:FIDDLE}}

每件概率：

> **1/3**

---

## 第二位置

同样三选一：

- {{relic:PRESERVED_FOG}}
- {{relic:SERE_TALON}}
- {{relic:DISTINGUISHED_CAPE}}

每件概率：

> **1/3**

---

## 第三位置

四选一：

- {{relic:CHOICES_PARADOX}}
- {{relic:MUSIC_BOX}}
- {{relic:LORDS_PARASOL}}
- {{relic:JEWELED_MASK}}

每件概率：

> **1/4**

{{event:VAKUU}} 在当前 0.111.0 原版中没有额外的资格条件。

因此，只要确定遇到的是 {{event:VAKUU}}，它的三个位置就可以非常直接地预测。

---

# 三位先古的区别

第三幕的规则可以很简单地记住：

| 先古 | 生成方式 | 特殊条件 |
| --- | --- | --- |
| {{event:NONUPEIPE}} | 9 个基础候选，洗牌取 3 | ≥4 张可接受 Swift 的牌时加入 {{relic:BEAUTIFUL_BRACELET}} |
| {{event:TANX}} | 9 个基础候选，洗牌取 3 | ≥3 张可接受 Instinct 的牌时加入 {{relic:TRI_BOOMERANG}} |
| {{event:VAKUU}} | 3 / 3 / 4 三个固定池各取 1 | 无 |

因此 {{event:NONUPEIPE}} 和 {{event:TANX}} 真正需要玩家补充的信息只有：

> **对应的附魔条件当前是否成立。**

{{event:VAKUU}} 则连这一步都没有。

从筛选角度看，它们都属于非常直接的先古：

> **先确定正确的候选池，再从 Seed 重放最终提供的选项。**
