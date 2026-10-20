# 巨量转化回传

买量归因：从巨量广告点击进入小游戏的玩家，每次完成广告观看后由**服务端**向巨量回传一次"付费"转化，供 oCPM/ROI 优化。与游戏内广告变现（激励视频 SDK）是两条独立链路，互不影响。

```
巨量广告点击 → 监测链接生成 callback → 小游戏启动 query 携带 callback
→ 客户端登录时上报（LoginRequest.AdCallback/AdOs）→ 服务端存入玩家档
→ 玩家完成广告观看（领体力/广告商店）→ 服务端 GET 回传 track/activate
```

## 触发点

| 业务 | 位置 | 回传 scene（仅日志用） |
|------|------|------------------------|
| 看广告补满体力 | `PlayerMetaService.RefillEnergyByAdAsync` | `energy_refill_ad` |
| 广告商店领体力/金币 | `PlayerMetaService.ClaimAdShopAsync` | `adshop_stamina` / `adshop_gold` |

发奖成功后才回传；回传 fire-and-forget，失败只记日志，不影响发奖与响应。

## 配置（`Card.Server/appsettings.json` 的 `OceanEngine` 段）

| 字段 | 说明 |
|------|------|
| `Enabled` | 总开关，false 时不回传也不请求 token（默认 false） |
| `AppId` / `Secret` | Marketing API 应用凭证（找运营/投放申请） |
| `PayAmountFen` | 单次广告观看的回传金额（**单位分**）。平台没有"单次广告价格"API，由运营按 eCPM 折算配置；`<=0` 不回传 |
| `ConversionUrl` | 回传地址，默认老接口 `https://ad.toutiao.com/track/activate/`；新版接口按运营文档替换 |
| `AccessTokenUrl` / `RefreshTokenUrl` | Marketing API OAuth 端点，一般不用改 |
| `AttachAccessToken` | 回传是否附带 access_token。新版接口需要（默认 true）；用老 track/activate 改 false |

## 回传格式

按运营文档《API 接口二付费金额回传》：

```
GET {ConversionUrl}?callback={callback}&os={0|1}&event_type=2&props=urlencode({"pay_amount":N})
```

- `event_type=2` 固定为付费事件；`pay_amount` 单位分。
- `os`：0=Android，1=iOS；未知（-1）不回传。
- `AttachAccessToken=true` 时附加 `&access_token=...`。

跳过条件（记 Debug/Warn 日志）：未启用、单价未配置、玩家无 callback、OS 未知、开启 token 但未授权。

## Token 管理（Marketing API OAuth2）

实现：`Card.Infrastructure/Ads/OceanEngineTokenProvider.cs`（Redis 持久化）。

- access_token 有效期 24h，存 `ocean:access_token`（TTL 提前 5 分钟过期）。
- refresh_token 有效期 30 天，存 `ocean:refresh_token`（29 天 TTL）。**每次刷新会同时下发新的 refresh_token 且旧值作废**，实现已自动保存新值。
- access 过期时自动用 refresh 换新；refresh 被拒（过期/作废）则清键并记日志，需要重新授权。
- 并发刷新由进程内信号量串行化。

### 首次授权

1. 在巨量开放平台完成开发者注册、创建应用，拿到 APPID/Secret 并填入配置。
2. 拼授权 URL 让投放账户完成授权，回调拿到 `auth_code`（**10 分钟有效、只能用一次**）。
3. 调试用授权端点换 token（debug 端点仅在 Development 环境挂载）：

```bash
curl -X POST http://localhost:5254/v1/debug/oceanengine/authorize \
  -H "Content-Type: application/json" \
  -d '{"authCode":"上一步拿到的 auth_code"}'
```

返回 `{"success":true}` 即完成。生产环境可用本地 Development 实例连同一 Redis 完成授权，或运维直接调 token 接口后写入 Redis 键。

## 归因采集（客户端）

- `Card/Assets/App/Ad/AdAttribution.cs`：冷启动首次访问时读 `WX.GetLaunchOptionsSync().query["callback"]`，`Application.platform` 折算 os；编辑器无采集。
- 登录时自动附带（`GameApiClient.LoginWeChatAsync`）：有 callback 才带 `AdCallback`，`AdOs` 始终带上。
- 服务端 `AuthService` 按"callback 非空才覆盖"写入玩家档（最新点击优先）。
- 抖音分支待切回抖音分支后补同款采集（`TT.GetLaunchOptionsSync`）。

## 验证

- `OceanEngine:Enabled=false`：无任何外部请求。
- 单测：`tests/Card.Domain.Tests/AdConversionTests.cs` 覆盖 URL 拼接（props encode / token 附加）与归因覆盖规则。
- 联调：微信开发者工具模拟启动 query 带 callback → 登录后玩家档可见 `AdAttribution`；看广告领奖后日志出现 `OceanEngine conversion reported (scene)`。
