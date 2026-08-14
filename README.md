# Card Client

Unity 卡牌客户端。

| 文档 | 内容 |
|------|------|
| [`Card/Assets/Framework/框架使用文档.md`](Card/Assets/Framework/框架使用文档.md) | DI、资源、UI、绑定、对话框、列表 |
| [`Config/配置表使用文档.md`](Config/配置表使用文档.md) | Excel 导出 / 配置表加载 |
| [`Card/Assets/App/Level/关卡模块使用文档.md`](Card/Assets/App/Level/关卡模块使用文档.md) | 关卡查询、通关进度 |
| 本文 | **AppServicesHost**、**存档**、**背包**、**图集**、**关卡** |

---

## 1. AppServicesHost

所有需要跨场景存活的服务，都挂在一个 `DontDestroyOnLoad` 节点上，**不要绑在 `AppBootstrap` 上**。

```
AppBootstrap（启动场景，可销毁）
  └─ AppServices.Create()
       └─ AppServicesHost（常驻节点）
            └─ ServiceContainer
                 ├─ ISaveService
                 ├─ IBagService
                 ├─ IResourceService
                 ├─ IAtlasService
                 ├─ ILevelService
                 ├─ ILevelProgressService
                 └─ ...
```

| 类型 | 路径 | 职责 |
|------|------|------|
| `AppBootstrap` | `Card/Assets/App/Bootstrap/AppBootstrap.cs` | 启动装配：建 Host、初始化资源/配置/UI |
| `AppServices` | `Card/Assets/App/Bootstrap/AppServices.cs` | 静态入口：`Create()` / `Resolve<T>()` / `Container` |
| `AppServicesHost` | `Card/Assets/App/Bootstrap/AppServicesHost.cs` | 常驻节点：持有容器、注册服务、切后台/退出时自动落盘 |

### 解析服务

任意场景、任意脚本（Host 创建之后）：

```csharp
var bag = AppServices.Resolve<IBagService>();
var atlas = AppServices.Resolve<IAtlasService>();
var level = AppServices.Resolve<ILevelService>();
var progress = AppServices.Resolve<ILevelProgressService>();
```

或构造函数注入：DI 会从 `ServiceContainer` 解析依赖。

启动前请先判断 `AppServices.IsReady`。

### 注册新服务

在 `AppBootstrap` 里用 Host 注册，不要把服务字段挂到 Bootstrap 上：

```csharp
_services.Register(new FooService());
_services.Register<IFooService>(foo);
```

实现了 `ISaveFlushable` 的实例会被自动跟踪，切后台 / 失焦 / 退出 / 销毁时调用 `Save()`。

---

## 2. 存档（ISaveService）

平台无关的键值存档。业务只依赖接口；复杂对象自行 `JsonUtility` 序列化后 `SetString`。

| 文件 | 职责 |
|------|------|
| `Card/Assets/Framework/Save/Runtime/ISaveService.cs` | 统一 KV 接口 |
| `Card/Assets/Framework/Save/Runtime/ISaveFlushable.cs` | 可落盘服务：`Save()` |
| `Card/Assets/Framework/Save/Runtime/PlayerPrefsSaveService.cs` | 单机实现（`PlayerPrefs`） |
| `Card/Assets/Framework/Save/Runtime/WeChatSaveService.cs` | 微信小游戏桩（暂未实现） |
| `Card/Assets/Framework/Save/Runtime/SaveFramework.cs` | 工厂：`Create()` 选实现 |

### 接口

```csharp
bool HasKey(string key);
string GetString(string key, string defaultValue = "");
void SetString(string key, string value);
int GetInt(string key, int defaultValue = 0);
void SetInt(string key, int value);
float GetFloat(string key, float defaultValue = 0f);
void SetFloat(string key, float value);
void DeleteKey(string key);
void DeleteAll();
void Save(); // 真正刷盘
```

`SetXxx` 只改内存缓存；`Save()` 才写磁盘。`PlayerPrefsSaveService` 对应 `PlayerPrefs.Save()`。

### 平台切换

`SaveFramework.Create()`：

- 默认：`PlayerPrefsSaveService`
- 定义 `WECHAT_MINIGAME`：`WeChatSaveService`（当前调用会抛 `NotImplementedException`）

### 新增可落盘服务

1. 实现 `ISaveFlushable`（内部用脏标记，无改动则 `Save()` 直接返回）
2. `AppServicesHost.Register(...)` 注册
3. 切后台 / 退出会自动 `FlushAll()`；关键节点也可主动 `Save()`

---

## 3. 背包（IBagService）

按 `ItemConfig.Id` 存数量，内存改动 + 脏标记落盘。

| 文件 | 职责 |
|------|------|
| `Card/Assets/App/Bag/IBagService.cs` | 背包接口（继承 `ISaveFlushable`） |
| `Card/Assets/App/Bag/BagService.cs` | 实现 |
| `Card/Assets/App/Bag/BagModels.cs` | `BagEntry` / `BagSaveData` |

存档 key：`bag.v1`（整包 JSON 一条写入 `ISaveService`）。

启动时在配置表加载之后 `Load()`，再 `Register` 到 Host。

### 接口

```csharp
bool IsDirty { get; }
int GetCount(int itemId);
bool Has(int itemId, int amount = 1);
void Add(int itemId, int amount);          // amount > 0
bool TryRemove(int itemId, int amount);    // 不足返回 false
IReadOnlyList<BagEntry> GetAll();
void Clear();
void Load();
void Save(); // 仅 dirty 时刷盘
```

### 用法

```csharp
var bag = AppServices.Resolve<IBagService>();
bag.Add(1010001, 100);
if (bag.TryRemove(1010001, 10))
{
    // ...
}
bag.Save(); // 结算等关键点可主动落盘
```

`Add` / `TryRemove` / `Clear` 只改内存并标脏，**不立即 IO**。落盘时机：

1. 业务主动 `Save()`（推荐：结算、购买完成）
2. `AppServicesHost` 自动：暂停、失焦、退出、销毁

`Save()` 无脏数据会直接返回，可频繁调用。

未知 `ItemConfig` Id 会打 Warning，仍会写入背包。

---

## 4. 图集（IAtlasService）

启动时预加载 `SpriteAtlas`，运行时按图集 key + 精灵名取图，避免首次抽卡卡顿。

| 文件 | 职责 |
|------|------|
| `Card/Assets/App/Atlas/IAtlasService.cs` | 图集接口 |
| `Card/Assets/App/Atlas/AtlasService.cs` | 实现：预加载、缓存 `GetSprite` |
| `Card/Assets/Res/Altas/Card.spriteatlasv2` | 扑克牌图集（含 `101`…`413`、`CardBack`） |

资源 key：`ResResourcePaths.CardAtlas` = `"Altas/Card"`（相对 `Assets/Res/`）。

启动时在 `ResourceFramework.InitializeAsync` 之后 `PreloadAsync()`，再 `Register` 到 Host，并 `CardSpriteLibrary.Bind`。

### 接口

```csharp
await atlas.PreloadAsync();
var sprite = atlas.GetSprite(ResResourcePaths.CardAtlas, "101");
atlas.TryGetSprite(ResResourcePaths.CardAtlas, "CardBack", out var back);
```

新增启动预加载图集：把资源放到 `Assets/Res/Altas/`，在 `ResResourcePaths` 加 key，再写入 `AtlasService.StartupAtlasKeys`。

牌面读取：`CardSpriteLibrary.GetFace(card)` / `CardSpriteLibrary.Back`。

---

## 5. 关卡（ILevelService / ILevelProgressService）

详见 [`Card/Assets/App/Level/关卡模块使用文档.md`](Card/Assets/App/Level/关卡模块使用文档.md)。

把 `LevelConfig` / `MonsterGroupConfig` / `MonsterConfig` 解析成关卡快照，并按关卡 Id 记录已通关难度。启动时在配置表加载之后注册。

**尚未接入 `GameSession`**：对局仍用 `GameBalance` 硬编码关卡人数与血量。

```
LevelConfig.MonsterGroup[]  →  MonsterGroupConfig
MonsterGroupConfig.MonsterId + MonsterLevel  →  MonsterConfig
```

索引：`_levels[难度][关卡]`、`_monsters[怪物Id][等级]`、`_levelsById[LevelConfig.Id]`。  
单局只打当前难度；`TryGetNext` 为 false 表示该难度打完。

```csharp
var level = AppServices.Resolve<ILevelService>();
var snapshot = level.Get(level.DefaultDifficulty, 1);
var byId = level.GetById(1010);

var progress = AppServices.Resolve<ILevelProgressService>();
progress.MarkCleared(1010); // 最后一关才写入该难度
if (progress.IsCleared(1)) { /* 难度 1 已通关 */ }
progress.Save();
```
