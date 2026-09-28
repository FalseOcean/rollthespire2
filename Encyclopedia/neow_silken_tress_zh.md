# {{relic:SILKEN_TRESS}}：下一组卡牌奖励

## 需求

{{relic-icon:SILKEN_TRESS}}是涅奥的诅咒选项之一。获得它以后，玩家通常会在第一场普通战斗的第一组卡牌奖励上看到 **华彩**。

因此最直观的需求是：

> 我希望第一场战斗出现一张指定的华彩卡牌。

但“第一场战斗”其实并不是{{relic:SILKEN_TRESS}}真正绑定的对象。

---

## 为什么可以预测

{{relic:SILKEN_TRESS}}的效果作用于：

> **获得它之后出现的下一组卡牌奖励。**

通常情况下，开局之后最先出现的卡牌奖励就是第一场普通战斗，因此效果自然落在那里。

但部分涅奥遗物本身也会立即生成卡牌奖励。

例如 **{{relic:KALEIDOSCOPE}}**和**{{relic:LOST_COFFER}}**。

如果通过{{relic:NEOWS_BONES}}等方式先获得{{relic:SILKEN_TRESS}}，再获得这些会生成卡牌奖励的遗物，那么它们产生的开局卡牌奖励就可能先吃到{{relic:SILKEN_TRESS}}的效果。

因此：

> {{relic:SILKEN_TRESS}} → {{relic:KALEIDOSCOPE}}

和：

> {{relic:KALEIDOSCOPE}} → {{relic:SILKEN_TRESS}}

并不是完全相同的开局历史。

这里真正重要的是**哪一组卡牌奖励最先发生**。

---

## RT2 目前能做到什么

RT2 会按照实际的开局领取顺序重放这些效果，而不是简单假定{{relic:SILKEN_TRESS}}永远作用于第一场小怪。

因此在原版环境下，**开局直接获得指定的华彩卡牌并非不可达**。

例如通过合适的{{relic:NEOWS_BONES}}组合和拾取顺序，可以让{{relic:SILKEN_TRESS}}先于{{relic:KALEIDOSCOPE}}或{{relic:LOST_COFFER}}生效，再让它们产生的卡牌奖励获得华彩。

这也是{{relic:NEOWS_BONES}}拾取顺序有时真正重要的原因之一：

> **顺序改变的不只是“先拿哪个遗物”，还可能改变哪个后续结果接住一个尚未消耗的效果。**
