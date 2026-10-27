# 第三幕先古：总候选池与固定位置

第三幕本地先古{{event:NONUPEIPE}}、{{event:TANX}}和{{event:VAKUU}}采用两种结构：前两位从总池洗牌取三件，瓦库从三个固定池各取一件。下列候选与概率以 0.111.0 原版为基础，通用环境见 [[reading-guide]]。

## {{event:NONUPEIPE}}：九件基础候选

- {{relic:BLESSED_ANTLER}}
- {{relic:BRILLIANT_SCARF}}
- {{relic:DELICATE_FROND}}
- {{relic:DIAMOND_DIADEM}}
- {{relic:FUR_COAT}}
- {{relic:GLITTER}}
- {{relic:JEWELRY_BOX}}
- {{relic:LOOMING_FRUIT}}
- {{relic:SIGNET_RING}}

游戏打乱候选池并取前三件，每件进入选项的概率为 **3/9 = 1/3，约 33.33%**。

当前牌组至少有 **4 张可附魔“迅速”的牌**时，加入{{relic:BEAUTIFUL_BRACELET}}。十件候选仍只取三件，因此包括新遗物在内，每件的出现率都变为 **3/10 = 30%**。资格改变整个池的概率，没有增加第四个选项。

## {{event:TANX}}：同样洗牌取三件

九件基础候选为：

- {{relic:CLAWS}}
- {{relic:CROSSBOW}}
- {{relic:IRON_CLUB}}
- {{relic:MEAT_CLEAVER}}
- {{relic:SAI}}
- {{relic:SPIKED_GAUNTLETS}}
- {{relic:TANXS_WHISTLE}}
- {{relic:THROWING_AXE}}
- {{relic:WAR_HAMMER}}

各自出现率为 **1/3**。当前牌组至少有 **3 张可附魔“本能”的牌**时，加入{{relic:TRI_BOOMERANG}}；十件中仍取三件，每件出现率变为 **3/10 = 30%**。

## {{event:VAKUU}}：三个固定位置

| 位置 | 候选遗物 | 各自概率 |
| --- | --- | --- |
| 第一 | {{relic:BLOOD_SOAKED_ROSE}}、{{relic:WHISPERING_EARRING}}、{{relic:FIDDLE}} | 1/3 |
| 第二 | {{relic:PRESERVED_FOG}}、{{relic:SERE_TALON}}、{{relic:DISTINGUISHED_CAPE}} | 1/3 |
| 第三 | {{relic:CHOICES_PARADOX}}、{{relic:MUSIC_BOX}}、{{relic:LORDS_PARASOL}}、{{relic:JEWELED_MASK}} | 1/4 |

瓦库没有额外资格条件。每个小池会先洗牌，再取第一件；这与直接抽一件的概率相同，但具体 Seed 的随机消耗不同，RT2 按实际洗牌顺序重放。

## 筛选要填写哪些前提

| 先古 | 生成方式 | 额外资格 |
| --- | --- | --- |
| {{event:NONUPEIPE}} | 9 或 10 件候选，洗牌取 3 | ≥4 张可附魔“迅速”的牌时加入{{relic:BEAUTIFUL_BRACELET}} |
| {{event:TANX}} | 9 或 10 件候选，洗牌取 3 | ≥3 张可附魔“本能”的牌时加入{{relic:TRI_BOOMERANG}} |
| {{event:VAKUU}} | 3 / 3 / 4 三个固定池各取 1 | 无 |

RT2 对前两位需要到达先古时的附魔资格前提：计数的是可以实际附魔的牌，不能只按牌型或名称判断。筛选瓦库“同时提供”时，同一位置的多个遗物不能共存。目标与前提见 [[ancient]]，共同随机原理见 [[why-predictable]]。
