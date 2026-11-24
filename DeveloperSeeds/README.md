# 开发者推荐种子

本目录的每个 `.json` 会作为内置资源发布，显示在 **种子库 → 种子收藏 → 开发者推荐**。它们是现成种子及其游玩配置，预设仍负责保存筛选条件。

推荐只读，玩家可以复制、按配置预测，或另存为自己的收藏。另存会复制完整单人/多人上下文，并把当前语言的标题、介绍与游玩说明保存为个人标题/备注；后续更新推荐不会覆盖个人修改。推荐文案不代表一次新的 Exact 校验。

## 添加与维护

1. 在对应游戏版本中，配置好单人/整桌角色、进阶、解锁条件及开局选择，再从种子分析保存收藏。收藏文件位于 RT2 数据目录的 `state/seed_library/`。
2. 将该收藏 JSON 复制到本目录，使用稳定文件名；运行时条目 ID 为 `developer:<文件名>`。保留完整 `Context`，不要补造 witness、运行池或缺失的解锁资料。推荐用途的 `QueryKeys` / `QueryAssociations` 留空。
3. 把推荐标题、介绍、游玩说明填入 `Texts/zh/SeedLibrary/seeds.json` 与 `Texts/en/SeedLibrary/seeds.json`，每种语言各一份文件，以 `developer:<文件名>` 为键，每个条目为包含小写 `title` / `description` / `instructions` 字段的对象；没有游玩说明时可省略 `instructions`。中文条目另用 `note` 字段原文保留原收藏的独立回退备注（可与介绍不同），不删改或合并已有文字；英文条目不补造新 `note`。缺少一种翻译时在对应对象字段填写空字符串，显示时使用另一种。发布的数据 JSON 不保留 `Title` / `Note`，`DeveloperDetails` 仅保留 `Icons`；图标使用已有 `SearchPresetVisualIconRef` 格式，当前展示最多三个遗物图标。内置 provider 按稳定 ID 回填同一持久化 DTO 后再读取；个人收藏的 JSON 格式不变。
4. 先在当前版本核对上下文导入与实际结果，再发布。打不开旧配置时仍可复制种子；不能静默改成默认角色或另一个涅奥选项。单人收藏用 `Selection` 保存实际开局；多人可使用每座的 `OpeningPremise.OpeningRoute` 与实际 `Selection`。

`two-ironclads-stoke.json` 收录 Owner 于 2026-10-03 提供的 `R93J1P8CQA34`：双铁甲、A10、树叶药膏与首个问号变形灵林谷。游玩结果及顺序说明来自 Owner；上下文按项目当前多人默认的全解锁、游戏 0.111.0 保存。解锁数据沿用同版本原生 UI 检查通过 `UnlockState.all.ToSerializable()` 捕获的完整配置，并在推荐检查中与当前游戏全解锁定义对照。事件描述限定于此种子，不将其提升为所有多人事件的通用承诺。
