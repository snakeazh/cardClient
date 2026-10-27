# BattleShopPop 使用文档

对局内的通关商店弹窗。PVE 每关通关后买圣物；PVP 商店阶段复用同一页，额外多一排「花金币补充技能次数」。

脚本：`Assets/App/UI/Popup/BattleShopPopView.cs`
视图模型：`Assets/App/UI/Popup/BattleShopPopViewModel.cs`
预制体：`Assets/Res/UI/Popup/BattleShopPop.prefab`
资源键：`ResResourcePaths.BattleShopPop` = `UI/Popup/BattleShopPop`
屏幕 Id：`AppScreenIds.BattleShopPop`
层：`UILayer.Popup`

货架卡：[`ShopItem.md`](../../Item/ShopItem.md)
买卖详情：[`ShopDetail.md`](ShopDetail.md)
进店前的本关结算：[`BattleSettleUpPop.md`](BattleSettleUpPop.md)
PVP 商店规则：[`Server/docs/pvp-match.md`](../../../../../Server/docs/pvp-match.md)

---

## 何时弹出

| 模式 | 条件 | 入口 |
|------|------|------|
| PVE | `Phase == Shop` 且不是最后一关（先弹 `BattleSettleUpPop`） | `GameTableViewModel.TryPresentShopPopup` |
| PVP | 服务端 `shop` 阶段 + 有本人 `Shop` 快照 + 未 `Done` | 同上（`IsPvpShopOpen`） |

关闭：`NextStageBtn` → `Session.LeaveShop()`（PVE 推进关卡 / PVP 发 `shop_done`）；`ReturnToHomeBtn` → 对局面板的确认流程。
`mask` 故意不绑点击关闭，商店只能靠按钮离开。

---

## 两套头部：PVEShop / PVPShop

预制体上有两个互斥的头部组，同一时刻只激活一个：

```
BattleShopPop
├─ PVEShop   标题 / RefreshBtn / timeText          （PVE）
├─ PVPShop   同样的头部 + horBtns2 三个技能按钮      （PVP）
├─ SellBG    MaxNum「当前圣物数量 x/y」
├─ sellHor   4 格货架（sellItem 模板克隆）
└─ MineHor   已携带（mineItem 模板克隆，ScrollRect）
```

`View.OnBind` 的 `ApplyShopVariant()` 按 `Session.IsPvp` 二选一激活；`RefreshBtn / refreshNum / RefreshGooldNum / timeText` 两边同名，
所以**头部节点一律走 `FindHeader<T>(名字)` 在激活的变体里找，不要用 `UIReference` 的键**（`UIReference` 里那几个键固定指向 PVEShop）。
老预制体只有单一 `BG` 时自动回落到按名全树查找。

不要同时激活两个变体：两层头部的 RectTransform 基本重叠（PVPShop 更高，给技能行留位）。

---

## 货架 / 已携带

- 货架 4 格：`Run.ShopOfferIds`，买入价 `Session.EffectiveBuyPrice`（PVP 读快照），点开 [`ShopDetail`](ShopDetail.md) 购买；买不起价格变红。
- MineHor 拼格：`[按(圣物Id,等级)聚合的格子][空槽 × 剩余槽位][买槽 × 1]`；点已购格卖，点买槽解锁下一槽（PVP 花金币、PVE 看广告）。
- 购买成功由 `ShopDetail` 调 `HoldIncomingMine / GetIncomingMineRect / RevealIncomingMine` 播卡片飞入并揭示新格。

---

## PVP 技能补充（PVPShop/horBtns2）

三个按钮对应三个技能，点一次扣金币、**下一轮开始**该技能 +1 次：

| 节点 | 技能 | 服务端字段 | 起价 |
|------|------|-----------|------|
| `PeekGood` | 搓牌（`PvpSkillIds.Rub = 0`） | `fighter.RubLeft` | 60 |
| `TiHuanGood` | 替换（`Replace = 1`） | `fighter.ReplaceLeft` | 80 |
| `ChaKanGood` | 透视（`Peek = 2`） | `fighter.PeekLeft` | 100 |

规则与价格在 `Card/Assets/Shared/Battle/PvpSkillShop.cs`（双端同源）：

- 一次购买 = 待生效次数 +1（`fighter.PendingXxx`）。`ResetSkills` 只在**下一轮开局**把待生效并入上限，然后清零，不跨轮累积；
- 本局同一个技能每买一次，价格 +20（`BoughtXxx` 计数），与圣物槽解锁同一套递增思路；
- 单价必须高于 `GameConst.EverySkillProvideGold`（未用技能折算金币，缺省 20），否则「买入 → 不用 → 结算返金」能套利；
- 扣金走 `PvpRelicRuntime.OnGoldSpent`（注魔硬币等「消费加伤」圣物照常生效）。

协议：`battle` action = `buy_skill`，`index` = 技能 Id；服务端 `PvpMatch.ActShop` 校验商店开放与金币，非法（未知技能 / 金币不足）直接抛错。

按钮显示：

| 节点 | 内容 |
|------|------|
| `Text (TMP)` | `搓牌(4/4)`：下一轮次数 / 下一轮上限（`PvpShopSkillDto.Max`）。买一次两个数一起 +1 |
| `PriceBar/Num` | `PvpShopSkillDto.Price`，买不起变红（`ThemeColors.ShopPriceUnaffordable`） |

买不起不置灰：点了走 Toast「金币不足」，与买槽格一致。成功后 Light Toast「下一轮搓牌 4 次」。

数据链路：服务端 `PvpMatchViewProjector.ProjectShop` 投影 `PvpShopStateDto.Skills`（价 / 剩余 / 下一轮上限）→ 客户端
`GameSession.ApplyPvpShop` 镜像到 `_pvpShopSkills` → `BattleShopPopViewModel.GetPvpShopSkill*` → View 在 `ShopRevision` 上刷新。
PVE 不绑这三个按钮（`_pvpShop == null` 或非 PVP 直接跳过）。

---

## 新手引导（仅 PVE）

`FirstShop` 组：第一步挖孔 `Shop.SellOffers`（4 张卡 + 各自金币价）等 `ShopOfferClicked`，第二步等 `ShopBought`；
买成功后登记 `Shop.BoughtMine` 供「点击刚买的圣物」。PVP 不跑这组引导（`TryStartShopGuide` 直接返回），关页时若还在跑会 `Abort`。

---

## 注意

- 两套头部有同名 `UIBind` 键，`UIReference` 只认第一份（PVEShop）。新增头部节点时要么改走 `FindHeader`，要么用不重复的键。
- `sellItem` / `mineItem` 是预制体上的隐藏模板；克隆时会销毁克隆体上的 `UIBind`，避免键冲突。
- PVP 技能按钮的监听是代码手动挂的（`Button.onClick`），改预制体不要同时给它们挂持久 `onClick`。
- 机器人目前不买技能次数（`RunBotShop` 只买圣物 / 解锁槽）。
