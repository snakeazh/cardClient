using System;
using System.Text;
using System.Threading.Tasks;
using CardShare.Contracts;
using Framework.Log;
using Framework.Save;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;
using UnityEngine.Networking;

namespace App.Net
{
    /// <summary>
    /// 局外 HTTP 客户端。登录后带 Bearer；401 时刷新一次 token。
    /// </summary>
    public sealed class GameApiClient
    {
        public const string AccessTokenKey = "game.api.access.v1";
        public const string RefreshTokenKey = "game.api.refresh.v1";
        public const string PendingSettleKey = "game.api.pending.settle.v1";

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly ISaveService _save;
        private string _accessToken = string.Empty;
        private string _refreshToken = string.Empty;
        private bool _refreshing;

        public GameApiClient(ISaveService save)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _accessToken = _save.GetString(AccessTokenKey, string.Empty) ?? string.Empty;
            _refreshToken = _save.GetString(RefreshTokenKey, string.Empty) ?? string.Empty;
        }

        public bool HasSession => !string.IsNullOrEmpty(_accessToken);

        public bool HasRefreshToken => !string.IsNullOrEmpty(_refreshToken);

        public async Task<LoginResponse> ConnectAsync(string deviceCode, string nickName = "")
        {
            if (HasRefreshToken)
            {
                try
                {
                    return await RefreshSessionAsync();
                }
                catch (GameApiException ex)
                {
                    if (string.Equals(ex.Code, "connection_error", StringComparison.Ordinal))
                    {
                        throw;
                    }

                    ClearTokens();
                }
            }

            return await LoginGuestAsync(deviceCode, nickName);
        }

        public Task<PveRunResponse> GetActiveRunAsync()
            => SendAsync<PveRunResponse>("GET", "/v1/pve/run/active", null);

        public void SavePendingSettle(PveSettleRequest request)
        {
            if (request == null)
            {
                return;
            }

            _save.SetString(PendingSettleKey, JsonConvert.SerializeObject(request, JsonSettings));
            _save.Save();
        }

        public PveSettleRequest LoadPendingSettle()
        {
            if (!_save.HasKey(PendingSettleKey))
            {
                return null;
            }

            var json = _save.GetString(PendingSettleKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<PveSettleRequest>(json, JsonSettings);
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Net, "pending settle parse failed: " + ex.Message);
                return null;
            }
        }

        public void ClearPendingSettle()
        {
            _save.DeleteKey(PendingSettleKey);
            _save.Save();
        }

        public async Task<PlayerProfileDto> FlushPendingSettleAsync()
        {
            var pending = LoadPendingSettle();
            if (pending == null || string.IsNullOrEmpty(pending.RunId))
            {
                return null;
            }

            try
            {
                var resp = await SettlePveAsync(pending);
                ClearPendingSettle();
                return resp?.Profile;
            }
            catch (GameApiException ex)
            {
                if (string.Equals(ex.Code, ErrorCodes.RunNotFound, StringComparison.Ordinal) ||
                    string.Equals(ex.Code, ErrorCodes.RunAlreadySettled, StringComparison.Ordinal) ||
                    string.Equals(ex.Code, ErrorCodes.Conflict, StringComparison.Ordinal) ||
                    string.Equals(ex.Code, ErrorCodes.InvalidRequest, StringComparison.Ordinal))
                {
                    ClearPendingSettle();
                    return null;
                }

                throw;
            }
        }

        public async Task<LoginResponse> LoginGuestAsync(string deviceCode, string nickName = "")
        {
            var login = await SendAsync<LoginResponse>(
                "POST",
                "/v1/auth/login",
                new LoginRequest
                {
                    Provider = "guest",
                    Code = string.IsNullOrEmpty(deviceCode) ? "editor-device" : deviceCode,
                    UserInfo = new LoginUserInfo { NickName = nickName ?? string.Empty, AvatarUrl = string.Empty },
                    PendingSettle = LoadPendingSettle()
                },
                auth: false);
            StoreTokens(login);
            ClearPendingSettle();
            return login;
        }

        public async Task<LoginResponse> RefreshSessionAsync()
        {
            if (string.IsNullOrEmpty(_refreshToken))
            {
                throw new GameApiException(ErrorCodes.Unauthorized, "未登录");
            }

            var login = await SendAsync<LoginResponse>(
                "POST",
                "/v1/auth/refresh",
                new RefreshTokenRequest
                {
                    RefreshToken = _refreshToken,
                    PendingSettle = LoadPendingSettle()
                },
                auth: false);
            StoreTokens(login);
            ClearPendingSettle();
            return login;
        }

        public Task<PlayerProfileDto> GetProfileAsync()
            => SendAsync<PlayerProfileDto>("GET", "/v1/player/profile", null);

        public Task<PveStartResponse> StartPveAsync(int levelId, int heroId)
            => SendAsync<PveStartResponse>("POST", "/v1/pve/start", new PveStartRequest { LevelId = levelId, HeroId = heroId });

        public Task<PveSettleResponse> SettlePveAsync(PveSettleRequest request)
            => SendAsync<PveSettleResponse>("POST", "/v1/pve/settle", request);

        public Task<PveRunResponse> ReportPveProgressAsync(string runId, bool clearedStage, int score)
            => SendAsync<PveRunResponse>("POST", "/v1/pve/run/progress", new PveProgressRequest
            {
                RunId = runId ?? string.Empty,
                ClearedStage = clearedStage,
                Score = score
            });

        public Task<TalentDrawResponse> DrawTalentAsync()
            => SendAsync<TalentDrawResponse>("POST", "/v1/talent/draw", new object());

        public Task<EnergyRefillResponse> RefillEnergyByAdAsync()
            => SendAsync<EnergyRefillResponse>("POST", "/v1/energy/refill-ad", new AdProofRequest());

        public Task<AdShopClaimResponse> ClaimAdShopAsync(string kind)
            => SendAsync<AdShopClaimResponse>("POST", "/v1/adshop/claim", new AdShopClaimRequest { Kind = kind });

        public Task<PlayerProfileDto> GrantBagAsync(int itemId, int amount)
            => SendAsync<PlayerProfileDto>("POST", "/v1/bag/grant", new BagMutateRequest { ItemId = itemId, Amount = amount });

        public Task<PlayerProfileDto> ConsumeBagAsync(int itemId, int amount)
            => SendAsync<PlayerProfileDto>("POST", "/v1/bag/consume", new BagMutateRequest { ItemId = itemId, Amount = amount });

        public Task<PlayerProfileDto> CompleteGuideAsync(int groupId)
            => SendAsync<PlayerProfileDto>("POST", "/v1/guide/complete", new GuideCompleteRequest { GroupId = groupId });

        public Task<PveRunResponse> EnterShopAsync(string runId, int freeShopRefreshLeft)
            => SendAsync<PveRunResponse>("POST", "/v1/pve/shop/enter", new PveShopEnterRequest
            {
                RunId = runId,
                FreeShopRefreshLeft = freeShopRefreshLeft
            });

        public Task<PveRunResponse> BuyShopRelicAsync(string runId, int relicId)
            => SendAsync<PveRunResponse>("POST", "/v1/pve/shop/buy", new PveShopActionRequest { RunId = runId, RelicId = relicId });

        public Task<PveRunResponse> SellShopRelicAsync(string runId, int relicId)
            => SendAsync<PveRunResponse>("POST", "/v1/pve/shop/sell", new PveShopActionRequest { RunId = runId, RelicId = relicId });

        public Task<PveRunResponse> RefreshShopAsync(string runId)
            => SendAsync<PveRunResponse>("POST", "/v1/pve/shop/refresh", new PveShopActionRequest { RunId = runId });

        public Task<PveRunResponse> GrantRunGoldAsync(string runId, int amount, string reason)
            => SendAsync<PveRunResponse>("POST", "/v1/pve/run/grant-gold", new PveRunGoldRequest
            {
                RunId = runId,
                Amount = amount,
                Reason = reason ?? string.Empty
            });

        public Task<PveRunResponse> SpendRunGoldAsync(string runId, int amount, string kind)
            => SendAsync<PveRunResponse>("POST", "/v1/pve/run/spend", new PveRunSpendRequest
            {
                RunId = runId,
                Amount = amount,
                Kind = kind ?? string.Empty
            });

        public Task<PlayerProfileDto> DebugGrantGoldAsync(int amount)
            => SendAsync<PlayerProfileDto>("POST", "/v1/debug/grant-gold", new DebugGrantGoldRequest { Amount = amount });

        private void StoreTokens(LoginResponse login)
        {
            _accessToken = login?.AccessToken ?? string.Empty;
            _refreshToken = login?.RefreshToken ?? string.Empty;
            _save.SetString(AccessTokenKey, _accessToken);
            _save.SetString(RefreshTokenKey, _refreshToken);
            _save.Save();
        }

        private void ClearTokens()
        {
            _accessToken = string.Empty;
            _refreshToken = string.Empty;
            _save.SetString(AccessTokenKey, string.Empty);
            _save.SetString(RefreshTokenKey, string.Empty);
            _save.Save();
        }

        private async Task<T> SendAsync<T>(string method, string path, object body, bool auth = true)
        {
            var result = await SendOnceAsync<T>(method, path, body, auth);
            if (result.Succeeded)
            {
                return result.Value;
            }

            if (auth && result.Status == 401 && !_refreshing && !string.IsNullOrEmpty(_refreshToken))
            {
                _refreshing = true;
                try
                {
                    await RefreshSessionAsync();
                }
                catch (GameApiException)
                {
                    ClearTokens();
                    throw;
                }
                finally
                {
                    _refreshing = false;
                }

                result = await SendOnceAsync<T>(method, path, body, auth: true);
                if (result.Succeeded)
                {
                    return result.Value;
                }
            }

            throw new GameApiException(result.Code, result.Message, result.Status);
        }

        private async Task<ApiResult<T>> SendOnceAsync<T>(string method, string path, object body, bool auth)
        {
            var url = GameApiSettings.BaseUrl.TrimEnd('/') + path;
            using (var request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                if (body != null && !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    var json = JsonConvert.SerializeObject(body, JsonSettings);
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                }

                if (auth && !string.IsNullOrEmpty(_accessToken))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + _accessToken);
                }

                request.timeout = 15;
                var op = request.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }

                var text = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                var status = (int)request.responseCode;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (request.result == UnityWebRequest.Result.ConnectionError ||
                        request.result == UnityWebRequest.Result.DataProcessingError)
                    {
                        AppLog.Warn(LogChannel.Net, $"{method} {path} 连接失败: {request.error}");
                        return ApiResult<T>.Fail("connection_error", "无法连接服务器", status);
                    }

                    return ParseError<T>(text, status);
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    return ApiResult<T>.Ok(default);
                }

                try
                {
                    var value = JsonConvert.DeserializeObject<T>(text, JsonSettings);
                    return ApiResult<T>.Ok(value);
                }
                catch (Exception ex)
                {
                    AppLog.Warn(LogChannel.Net, $"{method} {path} 解析失败: {ex.Message}");
                    return ApiResult<T>.Fail("invalid_request", "服务器返回无法解析", status);
                }
            }
        }

        private static ApiResult<T> ParseError<T>(string text, int status)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    var error = JsonConvert.DeserializeObject<ApiError>(text, JsonSettings);
                    if (error != null && !string.IsNullOrEmpty(error.Code))
                    {
                        return ApiResult<T>.Fail(error.Code, error.Message, status);
                    }
                }
                catch (Exception)
                {
                    // fall through
                }
            }

            var code = status == 401 ? ErrorCodes.Unauthorized : "http_error";
            var message = status == 401 ? "登录已过期" : $"请求失败 ({status})";
            return ApiResult<T>.Fail(code, message, status);
        }

        private readonly struct ApiResult<T>
        {
            public bool Succeeded { get; }
            public T Value { get; }
            public string Code { get; }
            public string Message { get; }
            public int Status { get; }

            private ApiResult(bool ok, T value, string code, string message, int status)
            {
                Succeeded = ok;
                Value = value;
                Code = code;
                Message = message;
                Status = status;
            }

            public static ApiResult<T> Ok(T value) => new ApiResult<T>(true, value, string.Empty, string.Empty, 200);

            public static ApiResult<T> Fail(string code, string message, int status)
                => new ApiResult<T>(false, default, code, message, status);
        }
    }
}
