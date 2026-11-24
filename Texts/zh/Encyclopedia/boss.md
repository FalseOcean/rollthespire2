# 首领与幕变体

> 机制基准：Slay the Spire 2 Beta 0.111.0

RT2 可以筛选第一幕变体、各幕首领，以及 A10 最后一幕的有序双首领组合。这些内容在开局生成，不必到达首领房后才能知道。

## 幕变体与发现记录

| 位置 | 变体 |
| --- | --- |
| 第一幕 | {{act:OVERGROWTH}} / {{act:UNDERDOCKS}} |
| 第二幕 | {{act:HIVE}} |
| 第三幕 | {{act:GLORY}} |

0.111.0 中只有第一幕有随机变体。不同变体有各自的敌人、事件、首领和其他世界内容。

游戏先取得已解锁的第一幕候选。全解锁且两者都已发现时，{{act:OVERGROWTH}} 与 {{act:UNDERDOCKS}} 各有 1/2 的概率。

单人游戏中，已经解锁、但尚未发现的非默认幕会优先出现。因此刚解锁 {{act:UNDERDOCKS}} 后，游戏可能直接选择它；记录为已发现后，再恢复正常随机。开局设置若已指定第一幕，则使用指定结果。

## 各变体的首领池

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



正常情况下，每幕从对应池的三个首领中选择一个，各有 1/3 的概率。{{encounter:WATERFALL_GIANT_BOSS}} 属于 {{act:UNDERDOCKS}}，不能与 {{act:OVERGROWTH}} 搭配。

首领抽取与事件、普通遭遇、精英等内容共享持续推进的世界随机状态。变体改变时，候选池以及前面的世界生成步骤也会改变。RT2 会先确定实际变体，再按该变体的世界生成恢复首领身份。

## A10 的双首领

A10 最后一幕先正常选择第一个首领，再从剩余两个中选择第二个，二者不会重复。

例如在 {{act:GLORY}} 中，指定 {{encounter:QUEEN_BOSS}} → {{encounter:AEONGLASS_BOSS}} 的基础概率为 **1/3 × 1/2 = 1/6**。顺序有意义；RT2 可以分别指定第一、第二首领，也可以把首领条件与幕变体一起筛选。
