# 佩尔：三个候选池与不均匀权重

佩尔从三个位置各提供一件遗物。第一个选项从固定池中抽取，第二个受牌组影响并带权重，第三个取决于是否已有事件宠物。下列池和概率以 0.111.0 原版为基础，环境说明见 [[reading-guide]]。

## 第一位置：固定三选一

{{relic:PAELS_FLESH}}、{{relic:PAELS_HORN}}、{{relic:PAELS_TEARS}}始终参与，各 **1/3**。

## 第二位置：出现条件与权重

{{relic:PAELS_WING}}和{{relic:PAELS_GROWTH}}始终参与。另外两件需要满足这些条件才会出现：

- {{relic:PAELS_CLAW}}：当前牌组至少有 **3 张可附魔“黏糊”的防御牌**。仅有防御标签还不够，牌也必须符合附魔规则。
- {{relic:PAELS_TOOTH}}：当前牌组至少有 **5 张可正常移除的牌**。

{{relic:PAELS_WING}}与符合条件的{{relic:PAELS_CLAW}}、{{relic:PAELS_TOOTH}}各有 **2 份权重**，{{relic:PAELS_GROWTH}}只有 **1 份**。因此这不是四件等概率抽取：

| 两件条件遗物是否可用 | {{relic:PAELS_WING}} | {{relic:PAELS_CLAW}} | {{relic:PAELS_TOOTH}} | {{relic:PAELS_GROWTH}} |
| --- | --- | --- | --- | --- |
| 两件都不可用 | 2/3 | 0 | 0 | 1/3 |
| 只有{{relic:PAELS_CLAW}}可用 | 2/5 | 2/5 | 0 | 1/5 |
| 只有{{relic:PAELS_TOOTH}}可用 | 2/5 | 0 | 2/5 | 1/5 |
| 两件都可用 | 2/7 | 2/7 | 2/7 | 1/7 |

即使只想筛选{{relic:PAELS_WING}}，也应填写另外两件的真实条件：它们参与候选池，会把{{relic:PAELS_WING}}的概率从 **2/3** 改为 **2/7**。

## 第三位置：有没有事件宠物

没有事件宠物时，{{relic:PAELS_EYE}}、{{relic:PAELS_BLOOD}}、{{relic:PAELS_LEGION}}各 **1/3**。已有事件宠物时，{{relic:PAELS_LEGION}}不再参与，{{relic:PAELS_EYE}}与{{relic:PAELS_BLOOD}}各 **1/2**。

第一幕密林的{{event:BYRDONIS_NEST}}可以给予{{card:BYRDONIS_EGG}}。**蛋仍在牌组里就已算事件宠物，无须孵化**，因此到佩尔时{{relic:PAELS_LEGION}}已不可用。休息点孵化后获得的{{relic:BYRDPIP}}也是事件宠物，这项状态仍然成立。

## 填写的是当前前提

RT2 的目标是希望提供哪些遗物；{{relic:PAELS_CLAW}}、{{relic:PAELS_TOOTH}}、{{relic:PAELS_LEGION}}的出现条件是到达佩尔时的前提。勾选“可用”并不要求抽中那件遗物，而是决定实际候选池。即使不筛选{{relic:PAELS_LEGION}}，持有蛋也必须计入这一前提。目标和前提的区别见 [[ancient]]，共同随机原理见 [[why-predictable]]。
