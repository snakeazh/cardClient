# 巨量转化回传

买量归因：从巨量广告点击进入小游戏的玩家，每次完成广告观看后由**服务端**向巨量回传一次"付费"转化（`active_pay`），供 oCPM/ROI 优化。与游戏内广告变现（激励视频 SDK）是两条独立链路，互不影响。

采用官方**新版事件管理接口 + clickid 归因链路**（微信小程序回传 2.0）：

```
巨量广告点击 → 小游戏启动 query 携带 clickid
→ 客户端登录时上报（LoginRequest.AdCallback/AdOs）→ 服务端存入玩家档
→ 玩家完成广告观看（领体力/广告商店）→ 服务端 POST JSON 回传 active_pay
```

官方文档：

- 微信小程序回传 2.0（clickid 链路，主依据）：https://event-manager.oceanengine.com/docs/8650/5bltdv1c
- 转化回传 API 对接说明（字段/错误码）：https://event-manager.oceanengine.com/docs/8650/app_api_docs
- 字节小程序 API 回传说明：https://event-manager.oceanengine.com/docs/8650/api_docs

## 触发点

| 业务 | 位置 | 回传 scene（仅日志用） |
|------|------|------------------------|
| 看广告补满体力 | `PlayerMetaService.RefillEnergyByAdAsync` | `energy_refill_ad` |
| 广告商店领体力/金币 | `PlayerMetaService.ClaimAdShopAsync` | `adshop_stamina` / `adshop_gold` |

发奖成功后才回传；回传 fire-and-forget，失败只记日志，不影响发奖与响应。

## 配置（`Card.Server/appsettings.json` 的 `OceanEngine` 段）

| 字段 | 说明 |
|------|------|
| `Enabled` | 总开关，false 时不回传（默认 false） |
| `PayAmountFen` | 单次广告观看的回传金额（**单位分**，int）。平台没有"单次广告价格"API，由运营按 eCPM 折算配置；`<=0` 不回传 |
| `ConversionUrl` | 回传地址，默认新版接口 `https://analytics.oceanengine.com/api/v2/conversion` |

新版接口无需 Marketing API 授权（无 access_token/refresh_token 流程）；安全签名（x-signature）为巨量侧可选功能，默认未开启，若运营开启需另行实现。

## 回传格式

按《三方（微信）小程序转化回传技术对接指南2.0》：

```
POST {ConversionUrl}
Content-Type: application/json

{
  "event_type": "active_pay",
  "context": {
    "ad": { "callback": "<启动 query 下发的 clickid>" },
    "device": { "platform": "android" | "ios" }   // OS 已知时才附带
  },
  "properties": { "pay_amount": N },              // 单位分，int
  "timestamp": 1604888786102                       // 毫秒
}
```

- 响应 `{"code":0,"message":"成功"}` 为成功；常见失败码：102 event_type 无效、103 callback 无效、127 重复请求已去重。
- 跳过条件（记 Debug 日志）：未启用、单价未配置、玩家无 clickid。

## 归因采集（客户端）

- `Card/Assets/App/Ad/AdAttribution.cs`：冷启动首次访问时读 `WX.GetLaunchOptionsSync().query`，**`clickid` 优先、`callback` 兜底**（兼容老监测链接）；`Application.platform` 折算 os；编辑器无采集。
- 登录时自动附带（`GameApiClient.LoginWeChatAsync`）：有 clickid 才带 `AdCallback`，`AdOs` 始终带上。
- 服务端 `AuthService` 按"callback 非空才覆盖"写入玩家档（最新点击优先）。
- 抖音分支待切回抖音分支后补同款采集（`TT.GetLaunchOptionsSync`）。

## 验证

- `OceanEngine:Enabled=false`：无任何外部请求。
- 单测：`tests/Card.Domain.Tests/AdConversionTests.cs` 覆盖回传请求体结构（event_type / callback / pay_amount / platform 映射）与归因覆盖规则。
- 联调：巨量后台「事件管理 → 联调工具」扫码进入广告 → 看广告领奖 → 后台可见 `active_pay` 上报成功；服务端日志出现 `OceanEngine conversion reported (scene)`。

## 注意事项

- 若投放使用「每次付费 / 付费 ROI」出价，官方要求除 `active_pay` 外同时回传 `active`（激活）事件；当前实现只回传付费，激活回传为后续独立需求。
- 每次广告调起下发的 clickid 均不相同，服务端按"最新点击优先"覆盖存档，与官方归因口径一致。
