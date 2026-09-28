# 特兹卡塔拉：三个独立的遗物位置

> 适用版本：**Slay the Spire 2 Beta 0.111.0**

## 需求

特兹卡塔拉会提供三个先古遗物。

它的规则很简单：三个位置分别从自己的候选池中选择一个，因此玩家可以很直观地筛选某个位置想要的遗物。

真正需要额外注意的，只有第一个位置。

---

## 第一个位置

这里通常有三个候选：

- {{relic:VERY_HOT_COCOA}}
- {{relic:YUMMY_COOKIE}}
- {{relic:NUTRITIOUS_SOUP}}

但 {{relic:NUTRITIOUS_SOUP}} 有一个条件：

> **玩家当前牌组中仍然要有基础的打击类卡牌。**

如果这个条件成立，三件遗物各有约 **1/3** 的机会出现。

如果已经把这类初始打击删除或变化掉，{{relic:NUTRITIOUS_SOUP}} 就不会进入候选池，此时只剩：

- {{relic:VERY_HOT_COCOA}}
- {{relic:YUMMY_COOKIE}}

两者各占 **1/2**。

所以这个条件不仅决定 {{relic:NUTRITIOUS_SOUP}} 自己能不能出现，也会改变另外两个遗物的概率。

---

## 第二个位置

第二个位置固定从三件遗物中选择：

- {{relic:BIIIG_HUG}}
- {{relic:STORYBOOK}}
- {{relic:TOASTY_MITTENS}}

没有额外出现条件，三者等概率。

---

## 第三个位置

第三个位置固定从四件遗物中选择：

- {{relic:GOLDEN_COMPASS}}
- {{relic:PUMPKIN_CANDLE}}
- {{relic:TOY_BOX}}
- {{relic:SEAL_OF_GOLD}}

同样没有额外出现条件，四者等概率。

---

## 为什么容易预测

特兹卡塔拉没有复杂的权重，也没有像 {{relic:SEA_GLASS}} 那样的内部随机结果。

只要先确定：

> {{relic:NUTRITIOUS_SOUP}} 当前有没有资格进入第一池，

剩下的事情就是从三个独立的候选池中各选一次。

因此它属于非常直接的先古筛选：

> **牌组状态只会改变第一个候选池，不会改变后两个位置的规则。**

RT2 会按照玩家提供的当前状态建立正确的第一池，再重放这三个位置的结果。
