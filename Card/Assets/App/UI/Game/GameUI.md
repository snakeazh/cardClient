# GameUI 使用文档

局内主 HUD。发牌后直接看牌，只显示开战和技能；比牌与攻击由 `GameSession` 驱动。

脚本：`Assets/App/UI/Game/GameUIView.cs`  
视图模型：`Assets/App/UI/Game/GameTableViewModel.cs`  
色板：[`ThemeColors.md`](../../ThemeColors.md)  
牌桌世界：[`GameBoardController.md`](GameBoardController.md)  
规则：[`GameLogic.md`](../../Game/GameLogic.md) · 状态机：[`GameSession.md`](../../Game/GameSession.md) · 遗物：[`RelicMechanics.md`](../../Game/RelicMechanics.md) · 关卡机制：[`BossMechanics.md`](../../Game/BossMechanics.md)

`GameTableController` 是旧场景绑法，见 [`GameTableController.md`](GameTableController.md)。

---

## 结构

```
GameUI
  PlayerItem              ← 玩家卡（card），显示 HeroDamage / Hp；点击弹出 ItemTip（名称 + 描述）
  player1/2/3             ← 敌人卡槽（player1 中心；运行时克隆 PlayerItem 并切到 enemycard）；平时点击弹出 ItemTip
  horBtns                 ← 开战 / 取消 / 下一局（发牌动画结束才显示）
  horBtns2                ← 搓牌 / 透视 / 替换，文案 (剩余/上限)
  horEquipBtns2           ← yiwuBtn 遗物列表；ruleBtn 点出 WinTip 赔率表（一直可点）
  cardinfoItem            ← 亮牌后玩家牌型图标
  cardtypeNum             ← 玩家倍率数字
  cardinfoEnemyItem       ← 亮牌后敌人牌型图标（预制体自摆位置）
  cardinfoEnemyNum        ← 敌人倍率数字（嵌套 cardinfoItem 后改名；WinTip 模板仍叫 cardtypeEnemyNum）
  roundInfo               ← 第几轮
  roundbuffGrid           ← 关卡机制格子；下面按条目克隆 roundbuff
  roundbuff               ← 机制图标模板（蓝书），无机制时格子隐藏
  mask / hptextdi         ← 攻击演出用；扣血数字在 hptextdi 上弹出
```

发牌期间 `ShowTableButtons=false`，`horBtns`、`horBtns2` 隐藏，发完再亮。`horEquipBtns2` 不跟发牌隐藏。`roundInfo` 局内一直显示第几轮。有关卡机制时 `roundbuffGrid` 显示，按 `BossMechanics.ResolveAll` 克隆 `roundbuff`（一机制一图标）；点击弹出该条 `ItemTip`：`title` 为机制名，`tipContext` 为 `Desc`。

`ItemTip` 预制体绑定 `title` / `tipContext` / `use`。遗物、关卡机制、玩家、敌人共用：`title` 填名称，`tipContext` 填具体内容。玩家读 `HeroConfig.Name` + `Desc`；敌人读 `MonsterConfig.Name`，BOSS 再带机制描述，普通怪没有词条时显示当前生命/攻击。透视点选敌人时仍走 `AttackEnemyAtSlot`，不弹 tip。

`cardinfoItem` / `cardinfoEnemyItem` 只在亮牌/比牌/攻击/本手结束时显示。透视未亮牌不显示。

世界牌在 `GameHud`：`mineNode` 玩家 5 张，`otherNode` 只画 `GameSession.DisplayedEnemy` 的 5 张（三人仍各有一手数据）。

| 节点 | 用途 |
|------|------|
| `cardinfoItem` | 玩家牌型图标，读 `Altas/CardType`。按 `HandScoreConfig.Level` 开特效：≥3 启动 `UiFlowLight`；5 显示 `PokerHandStraightFlush01`；6 显示 `PokerHandBomb01`；其余不扫光、不播粒子 |
| `cardtypeNum` | 玩家倍率数字，读 `Altas/cardTypeValue`。预制体自挂 `HorizontalLayoutGroup` |
| `cardinfoEnemyItem` | 敌人牌型图标，同样读 `Altas/CardType`，特效规则与 `cardinfoItem` 相同 |
| `cardinfoEnemyNum` | 敌人倍率数字，同样读 `Altas/cardTypeValue`。绑定优先 `cardinfoEnemyNum`，兼容 `cardtypeEnemyNum` |

`horEquipBtns2` 里的 `yiwuBtn` 打开 [`RemainListPop`](../Popup/RemainListPopView.cs)：列出本局已持有遗物，空列表显示 `nohave`。点击弹窗内卡片弹出 `ItemTip`（`title` 为遗物名，`tipContext` 为描述），可消耗遗物可在 tip 上使用。HUD 上不再排 `equip1`～`equip3`。结算遗物跳动没有槽位 Animator 时只改数字。

`ruleBtn` 一直显示。点击在按钮左侧弹出 `WinTip`：按 `HandScoreConfig` 从高到低克隆一行牌型图标 + 倍率（`BasicMagnification`，再加本局 `HandTypeMagBonus`）。关闭逻辑与 `ItemTip` 相同：全屏透明 Catcher，点空白关闭；再点 `ruleBtn` 也关闭。

**不要再创建或绑定 `duelHint`。** 中间提示条已去掉。  
**不要再创建或绑定 `arrow`。** 当前行动对象指示已去掉。

---

## 当前可见按钮

发牌结束、`WaitingOpen`：

| 按钮 | 文案 | 何时显示 |
|------|------|----------|
| `CompareBtn` | 开战 | `WaitingOpen`，且已选 3 张 |
| `PeekGood` / `ChaKanGood` / `TiHuanGood` | `(n/max)` | 始终在 `horBtns2`，没次数则禁用 |
| `yiwuBtn` | 遗物列表 | 一直显示，点开 `RemainListPop` |
| `ruleBtn` | 规则 | 一直显示，点开 `WinTip` 赔率表 |
| `NextRoundBtn` | 下一局 | `RoundSettle` |

`n` 为当前剩余次数，`max` 为 `GameBalance.Skill*Uses + Bonus*` 再加遗物/英雄加成。次数配置仍是搓牌 3 / 透视 1 / 替换 1。

以下节点仍在预制体里，当前流程**隐藏**：

`BlindBtn`（闷注）、`LookBtn`（看牌）、`RaiseBtn` / `RaiseHighBtn`、`AllInBtn`、`FoldBtn`、`CancelBtn`。

---

## 一局操作

```
发牌动画结束
  → 玩家 5 张手牌已翻开；otherNode 显示当前存活敌人的牌
  → 点选 3 张（选中上移），可点技能，或点「开战」
  → 用这 3 张与每名存活敌人逐个亮牌、打伤害（敌人从 5 张里自动选出最大 3 张；otherNode 切到当前对手）
  → 全灭：进商店；打完该难度出结算成功页；阵亡先出失败复活弹窗
```

技能：

| 按钮 | 效果 |
|------|------|
| PeekGood | 点按钮在右侧弹出 `ItemTip`「长按牌即可拖拽来搓牌」；有次数时长按手牌翻到背面，拖拽后松手换牌 |
| ChaKanGood | 点选敌人人物卡：otherNode 切到该敌人，5 张先全部抬起，再 `SetBackSeeThrough` 透出牌面；不能透视自己；开战时改成最大 3 张并翻面（不抬起） |
| TiHuanGood | 自己 5 张全部换成新牌 |

每手重置：搓牌 3 / 透视 1 / 替换 1（商店加成另加）。搓完或松手未达标回到开牌阶段。

人物卡攻击力 / 血量见 [`PlayerItem.md`](../../Item/PlayerItem.md)。  
通关商店货架 / 已购见 [`ShopItem.md`](../../Item/ShopItem.md)，买卖在 ShopDetail。  
攻击冲锋见 [`AttackCutscene.md`](AttackCutscene.md)。  
闯关结算见 [`BattleResultPopup.md`](../Popup/BattleResultPopup.md)。

`GameTableViewModel` 按阶段弹窗：

| 阶段 | 弹窗 | 说明 |
|------|------|------|
| `StageFail` | `BattleResultPopup` 失败 | `AgainBtn` 广告复活；`BackBtn` 放弃回主页 |
| `Shop` | `BattleSettleUpPop` 一次，然后 `BattleShopPop` | 本关积分与本关掉落金币，再买遗物 |
| `RunComplete` | `BattleResultPopup` 成功 | 只显示 BackBtn |

---

## 积分条

`roundInfo`：`第{n}轮`，n 为本关第几手（点「下一局」后递增，进下一关从 1 重计）。

`roundbuffGrid`：关卡机制格子。`LevelEntryNum` 为 0 或未抽到时隐藏；每条 `BossEntryConfig` 克隆一个 `roundbuff`，点击出该条 `Name` / `Desc`。BOSS 人物卡 tip 仍用拼接后的 `RoundBuffName` / `RoundBuffDesc`。

---

## 注意

- 局内金币栏是 `GameResource`（嵌套 `GameResourceBar`），跟局外 `MainResource` 一样 `Open` 到 `UIRoot/Resource`。进对局时 navigator 藏起 MainResource；商城/购买/结算期间 `backBtn` 隐藏，栏仍在 Resource 层。
- 开战前不要露出闷注 / 看牌 / 跟注 / 加注 / 弃牌。
- 玩家点桌上手牌选中/取消，选满 3 张才显示开战。
- 开战后不要让玩家再点选攻击目标，队列自动打当前敌人。
- 敌人人物卡平时点击弹出 `ItemTip`；透视中点敌人走透视，不用于选攻击目标。
- `player1` 是中心：只剩 1 个敌人就站中间；2 个时第一个开牌的站中间；3 个拼牌时当前对手站中间。
