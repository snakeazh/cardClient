# Card.Battle

对局纯计算。源码与 Unity 客户端共享（ET 式）：本目录即客户端 `Assets/Shared/Battle`，服务端 `Server/src/Card.Battle`（`netstandard2.1`）用 `<Compile Include>` 链入同一份源文件，两端各自编译，不走 DLL。

允许：牌、评牌、出伤、`CombatBonuses`、`RelicCombat`、摊牌引擎。  
不允许：HTTP、主档、体力、商店 IO、UnityEngine、EF、Redis。

Unity 的 `App.Game.Card` / `RunState` 不进本库，由客户端 `SharedBattleBridge` 转成这里的类型。

## 共享源码约束

两端编译器不同（Unity 是 C# 9，服务端是 latest），这里的代码必须：

- 只用 C# 9 语法：块式命名空间，不用文件级命名空间 / 集合表达式 / `required` / 原始字符串。
- 每个文件显式 `using`，不依赖 `ImplicitUsings`；需要可空注解时文件头写 `#nullable enable`。
- 不碰 Unity 没有的 BCL（如 `System.Text.Json`）。

## 规则

1. **只放两端必须同一份的东西**  
   同一输入会算出同一个数，或对端说同一句话，才进共享库。Contracts 放 DTO / 配表行；这里放计算。出伤百分比、改牌型、商店/回血、BOSS/燧石不进 `RelicCombat`。

2. **纯函数，输入决定输出**  
   `HandEvaluator.Evaluate`、`RelicCombat.Evaluate`、`CombatDamage.Resolve` 不读静态 Unity 表、不读磁盘、不读时钟。随机由调用方传入，或写在 `CombatDamageInput` 的强制命中上。PVE 叠层、未亮出牌是快照字段；PVP 不填就是 0。函数里不要写 `if (pvp)`。

3. **配表只走 `IGameTables`**  
   用 `TryGetRelic` / `TryGetRelicEntry`。不用 Unity 的 `RelicEntryConfig.Get`，也不在这里读 JSON。加载是边界：服务端 `FileGameConfigLoader`，客户端 `UnityGameConfigLoader`。

4. **调用方保证入参，被调用方直接用**  
   不要 `?.`、`??` 把 null 吞成 0 / 空串 / 空列表。不要 `EnsureXxx`、不要函数开头 `if (x == null) return`。找不到配表用 `TryGet` 跳过。列表在构造时给空。

5. **不依赖运行时**  
   禁止 `UnityEngine`、ASP.NET、EF、Redis。

6. **PVE / PVP 各自算，客户端不上报伤害数字**  
   共享的是函数。PVE 只在客户端调；PVP 只在服务端调。

7. **新圣物加成先配表，再考虑加代码**  
   已有通道（倍率/加攻）× 已有计数（花色/牌型/点数）只改 `RelicEntryConfig`。新算法才在这里加分发，并加黄金用例。

8. **口径用测试锁**  
   公式变更落在 `tests/Card.Domain.Tests`（例如金花 `30 × (2.5+2) = 135`）。不要靠客户端录包当唯一依据。

9. **生成文件不手改**  
   `Contracts/Config` 由导表生成，下次导出会覆盖手改。
