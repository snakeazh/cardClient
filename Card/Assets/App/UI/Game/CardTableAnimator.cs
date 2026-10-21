using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Audio;
using App.Bootstrap;
using App.Config;
using CardShare.Contracts.Config;
using App.Game;
using App.Resources;
using DG.Tweening;
using Framework.Assets;
using Framework.Log;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 从 GameHud.dealpoint 发牌到 mineNode / otherNode。
    /// otherNode 只展示 <see cref="GameSession.DisplayedEnemy"/> 的 5 张牌。
    /// </summary>
    public sealed class CardTableAnimator
    {
        private const int CardsPerHand = GameBalance.MaxCardsPerSeat;
        private const float DealMoveDuration = 0.32f;
        private const float DealStagger = 0.08f;
        private const float DealFlipPause = 0.12f;
        private const float ShuffleStagger = 0.015f;
        private const float ShuffleAppear02 = 0.8f;
        // 洗牌堆只铺视觉需要的张数：每次出 2 张、共 20 次（40 张），
        // 比整副 52 张少 12 个同播 Animator；发牌最多 5+5=10 张，不够时 TakeDealCard 有 SpawnLooseDealCard 兜底。
        private const int PileCardCount = 40;
        private const int PileCardsPerSpawn = 2;
        private const float FlipDuration = 0.35f;
        private const float RevealFlipDuration = 0.28f;
        private const float RevealCardGap = 0.12f;
        private const float RevealSeatGap = 0.38f;
        private const float SettleClipDuration = 0.3f;
        private const float SelectLift = 0.28f;
        private const float SelectLiftDuration = 0.12f;
        private const float RubReplaceRevealDelay = 1.5f;
        /// <summary>替换手牌：旧牌回收播放速度（1 = 原速，越小越慢）。</summary>
        private const float ReplaceExitSpeed = 0.7f;
        /// <summary>替换手牌：翻面进度到多少时开始飞出（0~1，边翻边飞）。</summary>
        private const float ReplaceFlyStartRatio = 0.4f;
        /// <summary>替换手牌：旧牌抛物线飞出时长（秒）。</summary>
        private const float ReplaceFlyDuration = 0.38f;
        /// <summary>替换手牌：旧牌逐张起飞的间隔（秒）。</summary>
        private const float ReplaceFlyStagger = 0.03f;
        /// <summary>替换手牌：旧牌抛物线拱高（世界单位）。</summary>
        private const float ReplaceFlyArcHeight = 2f;
        /// <summary>替换手牌：旧牌全部飞出后，等一帧再重置到牌堆，避免和最后一帧飞行抢位置。</summary>
        private const float ReplaceResetDelay = 0.02f;
        /// <summary>替换手牌：旧牌飞到牌堆后到第一张新牌起飞的间隔（秒）。</summary>
        private const float ReplaceDeckGap = 0.08f;
        /// <summary>替换手牌：最后一张新牌落位后到翻面的缓冲（秒）。</summary>
        private const float ReplaceDealSettle = 0.04f;
        /// <summary>aini_card_deal 片段长度（秒），用于替换发牌时等待最后一张落位。</summary>
        private const float ReplaceDealClipDuration = 0.24f;

        private IResourceService _resources;
        private GameObject _prefab;
        private bool _prefabOwned;
        private AudioClip _dealSfx;
        private AudioClip _revealSfx;
        private AudioClip _rubSfx;
        private readonly CardShadowPool _shadows = new CardShadowPool();

        private Transform _hud;
        private Transform _dealPoint;
        private CardDealPoint _dealPile;
        private SeatView _player;
        private SeatView _other;
        private Sequence _dealSeq;
        private Sequence _revealSeq;
        private Sequence _rubSeq;
        private Sequence _replaceSeq;
        private Sequence _replaceDealSeq;
        private int _shownDeal = -1;
        private int _shownEnemyId = int.MinValue;
        private int _shownReveal;
        private int _shownReplace;
        private int _dealToken;
        private int _revealToken;
        private int _replaceToken;
        private int _seeThroughToken;
        private bool _dealing;
        private bool _revealing;
        private bool _replacing;
        private GameSession _session;
        private int _rubLockIndex = -1;
        private bool _rubShakeReady;
        private Vector3 _rubRestWorldPos;
        private Vector3 _rubGrabOffset;
        private int _rubSavedOrder;
        private float _rubPeakOffset;
        private float _rubDragStartTime;
        private bool _rubSuccessFxShown;
        private int _rubPlayToken;
        private bool _rubPlaying;
        private const float MaxRubDrag = 10f;
        /// <summary>松手时峰值偏移低于此值视为搓牌失败。</summary>
        public const float MinRubOffset = 2f;
        /// <summary>从开始跟手到松手的最短持续时间（秒）。</summary>
        public const float MinRubDuration = 0.5f;

        public bool IsDealing => _dealing;
        public bool IsBusy => _dealing || _revealing || _rubPlaying || _replacing;
        public bool IsRubPlaying => _rubPlaying;
        public bool IsRubPreviewActive => _rubLockIndex >= 0;
        public bool IsRubShakeReady => _rubShakeReady;
        public float RubPeakOffset => _rubPeakOffset;
        public float RubDragElapsed => _rubDragStartTime > 0f ? Time.time - _rubDragStartTime : 0f;
        public int RubLockIndex => _rubLockIndex;
        public event System.Action DealFinished;

        public bool TryGetPlayerCard(int index, out CardItem item)
        {
            item = null;
            if (_player == null || index < 0 || index >= _player.Items.Length)
            {
                return false;
            }

            if (!_player.Landed[index])
            {
                return false;
            }

            item = _player.Items[index];
            return item != null;
        }

        private sealed class SeatView
        {
            public Transform Node;
            public Transform[] Points = new Transform[CardsPerHand];
            public CardItem[] Items = new CardItem[CardsPerHand];
            public Transform[] Shadows = new Transform[CardsPerHand];
            public bool[] Landed = new bool[CardsPerHand];
            public bool IsPlayer;
        }

        public void Bind(Transform hud, IResourceService resources)
        {
            _resources = resources;
            _hud = hud;
            _shadows.Bind(hud);
            _dealPoint = FindChild(hud, "dealpoint") ?? FindChild(hud, "DealPoint");
            if (_dealPoint != null)
            {
                _dealPile = _dealPoint.GetComponent<CardDealPoint>();
                if (_dealPile == null)
                {
                    _dealPile = _dealPoint.gameObject.AddComponent<CardDealPoint>();
                }
            }
            else
            {
                _dealPoint = hud;
                _dealPile = null;
            }

            var mine = FindChild(hud, "mineNode");
            _player = BuildSeat(mine, true);
            BindEnemySlots(hud);
            HideLegacyIcons(mine);
            HideLegacyIcons(_other != null ? _other.Node : null);
            _ = PreloadDealSfxAsync();
            _ = PreloadRevealSfxAsync();
            _ = PreloadRubSfxAsync();
        }

        private async Task PreloadDealSfxAsync()
        {
            var resources = _resources;
            if (_dealSfx != null || resources == null)
            {
                return;
            }

            try
            {
                var clip = await resources.LoadAsync<AudioClip>(ResResourcePaths.SfxDeal5Cards);
                if (_resources == null)
                {
                    // await 期间已退局：立即释放，避免计数泄漏
                    resources.Release(ResResourcePaths.SfxDeal5Cards);
                    return;
                }

                _dealSfx = clip;
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Assets, "Deal SFX load failed: " + ex.Message);
            }
        }

        private async Task PreloadRevealSfxAsync()
        {
            var resources = _resources;
            if (_revealSfx != null || resources == null)
            {
                return;
            }

            try
            {
                var clip = await resources.LoadAsync<AudioClip>(ResResourcePaths.SfxRevealCards3);
                if (_resources == null)
                {
                    resources.Release(ResResourcePaths.SfxRevealCards3);
                    return;
                }

                _revealSfx = clip;
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Assets, "Reveal SFX load failed: " + ex.Message);
            }
        }

        private async Task PreloadRubSfxAsync()
        {
            var resources = _resources;
            if (_rubSfx != null || resources == null)
            {
                return;
            }

            try
            {
                var clip = await resources.LoadAsync<AudioClip>(ResResourcePaths.SfxRubCards02);
                if (_resources == null)
                {
                    resources.Release(ResResourcePaths.SfxRubCards02);
                    return;
                }

                _rubSfx = clip;
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Assets, "Rub SFX load failed: " + ex.Message);
            }
        }

        private void PlayDealSfx()
        {
            if (_dealSfx == null || !AppServices.IsReady)
            {
                return;
            }

            AppServices.Resolve<IAudioService>().PlaySfx(_dealSfx);
        }

        private void PlayRevealSfx()
        {
            if (_revealSfx == null || !AppServices.IsReady)
            {
                return;
            }

            AppServices.Resolve<IAudioService>().PlaySfx(_revealSfx);
        }

        private void PlayRubSfx()
        {
            if (_rubSfx == null || !AppServices.IsReady)
            {
                return;
            }

            AppServices.Resolve<IAudioService>().PlaySfx(_rubSfx);
        }

        public void Sync(GameSession session)
        {
            _session = session;
            if (session == null || _hud == null)
            {
                return;
            }

            ApplySeatVisibility(session);
            if (session.DealSerial <= 0)
            {
                StopHandReplace(cancelSession: true);
                ClearAllItems();
                HideDealPile();
                _shownDeal = 0;
                _shownReplace = session.ReplacePlaySerial;
                return;
            }

            if (session.DealSerial != _shownDeal)
            {
                if (session.ConsumePvpSnapDeal())
                {
                    SnapPresentHands(session);
                }
                else
                {
                    PlayDeal(session);
                }

                return;
            }

            if (_dealing || _revealing || _replacing)
            {
                if (!_dealing && !_replacing)
                {
                    SyncRubSelectFx(session);
                }

                return;
            }

            if (session.ReplacePlaySerial > 0 && session.ReplacePlaySerial != _shownReplace)
            {
                PlayHandReplace(session);
                return;
            }

            EnsureDisplayedEnemy(session);

            if (session.RevealPlaySerial > 0 && session.RevealPlaySerial != _shownReveal)
            {
                SyncRubSelectFx(session);
                SyncSelectLift(session);
                PlayReveal(session);
                return;
            }

            SyncIdleFloat();
            SyncAllFaces(session);
            SyncSelectLift(session);
            SyncSeeThrough(session);
            SyncRubSelectFx(session);
        }

        /// <summary>
        /// 空闲漂浮兜底断言：己方已落位手牌开、搓牌锁定中的关；
        /// 选中的牌开加成（幅度/摆动略微增大）。显式开关在落位/搓牌各入口已做，
        /// 这里覆盖漏网路径（如 PVP PendingRubIndex 直接变化）与选中态变化。
        /// </summary>
        private void SyncIdleFloat()
        {
            if (_player == null)
            {
                return;
            }

            var seat = _session != null ? _session.Player : null;
            for (var i = 0; i < _player.Items.Length; i++)
            {
                var item = _player.Items[i];
                if (item == null)
                {
                    continue;
                }

                var rubLocked = i == _rubLockIndex ||
                                (_session != null && _session.PendingRubIndex == i);
                var floating = _player.Landed[i] && !rubLocked;
                item.SetIdleFloat(floating);
                item.SetIdleFloatBoosted(floating && seat != null && seat.IsCardSelected(i));
            }
        }

        /// <summary>
        /// 开场 hold：清掉旧牌、不改 <c>_shownDeal</c>，之后 <see cref="Sync"/> 仍会 PlayDeal。
        /// </summary>
        public void SyncHoldingDeal(GameSession session)
        {
            _session = session;
            if (session == null || _hud == null)
            {
                return;
            }

            ApplySeatVisibility(session);
            StopHandReplace(cancelSession: true);
            ClearAllItems();
            HideDealPile();
            if (session.DealSerial <= 0)
            {
                _shownDeal = 0;
            }

            _shownReplace = session.ReplacePlaySerial;
        }

        public int HitPlayerCard(Camera camera)
        {
            if (camera == null || _player == null)
            {
                return -1;
            }

            var mouse = Input.mousePosition;
            mouse.z = Mathf.Abs(camera.transform.position.z);
            var world = camera.ScreenToWorldPoint(mouse);
            var hit = Physics2D.Raycast(world, Vector2.zero);
            if (hit.collider == null)
            {
                return -1;
            }

            var hitGo = hit.collider.gameObject;
            for (var i = 0; i < _player.Items.Length; i++)
            {
                var item = _player.Items[i];
                if (item == null || !_player.Landed[i])
                {
                    continue;
                }

                if (hitGo == item.gameObject || hitGo.transform.IsChildOf(item.transform))
                {
                    return i;
                }
            }

            return -1;
        }

        public int HitEnemySlot(Camera camera)
        {
            if (camera == null)
            {
                return -1;
            }

            var mouse = Input.mousePosition;
            mouse.z = Mathf.Abs(camera.transform.position.z);
            var world = camera.ScreenToWorldPoint(mouse);
            var hit = Physics2D.Raycast(world, Vector2.zero);
            if (hit.collider == null)
            {
                return -1;
            }

            var hitGo = hit.collider.gameObject;
            if (_other == null)
            {
                return -1;
            }

            for (var i = 0; i < _other.Items.Length; i++)
            {
                var item = _other.Items[i];
                if (item == null || !_other.Landed[i])
                {
                    continue;
                }

                if (hitGo == item.gameObject || hitGo.transform.IsChildOf(item.transform))
                {
                    return _session != null ? _session.DisplayedEnemyVisualSlot : -1;
                }
            }

            return -1;
        }

        public void TintPlayerCard(int index, Color color)
        {
            var sr = PlayerRenderer(index);
            if (sr != null)
            {
                sr.color = color;
            }
        }

        public void BeginInstantRub(int index)
        {
            PlayRubReplace(index, (Func<Task<bool>>)null, null);
        }

        /// <summary>
        /// 选中一张牌搓牌：立刻翻到背面并播 ChangeCard01，约 1.5 秒后翻回正面揭示新牌。
        /// </summary>
        public void PlayRubReplace(int index, Func<bool> applyReplace, Action onComplete)
        {
            Func<Task<bool>> asyncApply = null;
            if (applyReplace != null)
            {
                asyncApply = () => Task.FromResult(applyReplace());
            }

            PlayRubReplace(index, asyncApply, onComplete);
        }

        /// <summary>
        /// 搓牌（可异步换牌）：翻背面后立刻 <paramref name="applyReplaceAsync"/>（PVP 应等 match_update），
        /// 成功则在背面 SetCard，再揭正面；失败翻回旧牌。
        /// </summary>
        public void PlayRubReplace(int index, Func<Task<bool>> applyReplaceAsync, Action onComplete)
        {
            if (_player == null || index < 0 || index >= CardsPerHand)
            {
                onComplete?.Invoke();
                return;
            }

            var item = _player.Items[index];
            if (item == null || !_player.Landed[index])
            {
                onComplete?.Invoke();
                return;
            }

            StopRubReplace(restore: false);
            var token = ++_rubPlayToken;
            _rubPlaying = true;
            _rubLockIndex = index;
            _rubShakeReady = true;
            _rubRestWorldPos = _player.Points[index] != null
                ? _player.Points[index].position
                : item.transform.position;
            BringRubCardToFront(index);
            SetPlayerChangeSelectFx(false);
            item.SetIdleFloat(false);
            item.PlayChangeReplaceFx();
            TintPlayerCard(index, Color.white);
            PlayRubSfx();

            // 揭示等待用 scaled 时钟：倍速（Time.timeScale）下节奏同步加快；realtime 会拖满真实 1.5s。
            var revealAt = Time.time + RubReplaceRevealDelay;
            _ = RunRubReplaceAsync(index, item, token, applyReplaceAsync, revealAt, onComplete);
        }

        private async Task RunRubReplaceAsync(
            int index,
            CardItem item,
            int token,
            Func<Task<bool>> applyReplaceAsync,
            float revealAt,
            Action onComplete)
        {
            void Done()
            {
                if (token != _rubPlayToken)
                {
                    return;
                }

                item.HideChangeCardFx();
                RestoreRubCardLayer(index);
                item.SetIdleFloat(true);
                _rubLockIndex = -1;
                _rubPlaying = false;
                _rubSeq = null;
                onComplete?.Invoke();
            }

            void FlipFrontAndDone()
            {
                if (token != _rubPlayToken)
                {
                    return;
                }

                item.StopTweenAnimation();
                var flip = item.FlipTo(CardFaceState.Front, FlipDuration);
                if (flip == null)
                {
                    item.SetFace(CardFaceState.Front);
                    Done();
                    return;
                }

                _rubSeq = DOTween.Sequence()
                    .AppendInterval(FlipDuration)
                    .OnComplete(Done);
            }

            var flippedBack = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var flipBack = item.FlipTo(CardFaceState.Back, FlipDuration);
            if (flipBack != null)
            {
                flipBack.OnComplete(() => flippedBack.TrySetResult(true));
            }
            else
            {
                item.SetFace(CardFaceState.Back);
                flippedBack.TrySetResult(true);
            }

            await flippedBack.Task;
            if (token != _rubPlayToken)
            {
                return;
            }

            if (item.FaceState != CardFaceState.Back)
            {
                item.SetFace(CardFaceState.Back);
            }

            var ok = true;
            if (applyReplaceAsync != null)
            {
                try
                {
                    ok = await applyReplaceAsync();
                }
                catch (Exception ex)
                {
                    AppLog.Warn(LogChannel.UI, "rub replace apply failed: " + ex.Message);
                    ok = false;
                }
            }

            if (token != _rubPlayToken)
            {
                return;
            }

            if (!ok)
            {
                FlipFrontAndDone();
                return;
            }

            // 权威手牌已写入 Session：背面锁住时换贴图，揭开才是新牌。
            if (_session != null && _session.Player != null)
            {
                var card = ShownCard(_session.Player, index, true, _session);
                if (!item.Card.Equals(card))
                {
                    item.SetCard(card);
                }
            }

            var wait = revealAt - Time.time;
            if (wait > 0.01f)
            {
                var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _rubSeq = DOTween.Sequence()
                    .AppendInterval(wait)
                    .OnComplete(() => hold.TrySetResult(true));
                await hold.Task;
            }

            if (token != _rubPlayToken)
            {
                return;
            }

            FlipFrontAndDone();
        }

        /// <summary>长按搓牌：抬起并翻到背面，翻完后才可拖拽抖动。</summary>
        public void BeginRubPreview(int index)
        {
            if (_player == null || index < 0 || index >= CardsPerHand)
            {
                return;
            }

            var item = _player.Items[index];
            if (item == null || !_player.Landed[index] || _player.Points[index] == null)
            {
                return;
            }

            if (_rubLockIndex >= 0 && _rubLockIndex != index)
            {
                CancelRubPreview();
            }

            _rubLockIndex = index;
            _rubShakeReady = false;
            _rubRestWorldPos = _player.Points[index].position;
            var lifted = _rubRestWorldPos + SelectOffset(_player);
            SyncSlotShadow(_player, index, true);
            BringRubCardToFront(index);
            item.SetIdleFloat(false);
            item.MoveTo(lifted, SelectLiftDuration, Ease.OutQuad).OnUpdate(FollowRubShadow);
            FollowRubShadow();
            TintPlayerCard(index, Color.white);
            item.SetDragEffectVisible(true);

            var flip = item.FlipTo(CardFaceState.Back, FlipDuration);
            if (flip == null)
            {
                _rubShakeReady = item.FaceState == CardFaceState.Back;
                return;
            }

            var token = index;
            flip.OnComplete(() =>
            {
                if (_rubLockIndex == token)
                {
                    _rubShakeReady = true;
                }
            });
        }

        /// <summary>开始跟手：牌立刻移到手指位置。</summary>
        public void BeginRubDrag(Camera camera, Vector3 screenPos)
        {
            if (_rubLockIndex < 0 || !_rubShakeReady || _player == null || camera == null)
            {
                return;
            }

            var item = _player.Items[_rubLockIndex];
            if (item == null)
            {
                return;
            }

            item.StopMove();
            _rubPeakOffset = 0f;
            _rubDragStartTime = Time.time;
            _rubSuccessFxShown = false;
            _rubGrabOffset = Vector3.zero;
            DragRubCard(camera, screenPos);
        }

        /// <summary>跟手拖拽，限制在抬起点附近。</summary>
        public void DragRubCard(Camera camera, Vector3 screenPos)
        {
            if (_rubLockIndex < 0 || !_rubShakeReady || _player == null || camera == null)
            {
                return;
            }

            var item = _player.Items[_rubLockIndex];
            if (item == null)
            {
                return;
            }

            var rest = _rubRestWorldPos + SelectOffset(_player);
            var mouse = ScreenOnCardPlane(camera, screenPos, item.transform.position.z);
            var target = mouse + _rubGrabOffset;
            var offset = target - rest;
            offset.z = 0f;
            offset = Vector3.ClampMagnitude(offset, MaxRubDrag);
            item.transform.position = rest + offset;
            FollowRubShadow();
            if (offset.magnitude > _rubPeakOffset)
            {
                _rubPeakOffset = offset.magnitude;
            }

            var t = Mathf.Clamp01(offset.magnitude / MaxRubDrag);
            TintPlayerCard(_rubLockIndex, Color.Lerp(Color.white, new Color(1f, 0.85f, 0.4f), t));
            TryShowRubSuccessEffect();
        }

        private void TryShowRubSuccessEffect()
        {
            if (_rubSuccessFxShown || _rubLockIndex < 0 || _player == null)
            {
                return;
            }

            if (_rubPeakOffset < MinRubOffset || RubDragElapsed < MinRubDuration)
            {
                return;
            }

            var item = _player.Items[_rubLockIndex];
            if (item == null)
            {
                return;
            }

            _rubSuccessFxShown = true;
            item.PlayDragSuccessEffect();
        }

        private static Vector3 ScreenOnCardPlane(Camera camera, Vector3 screenPos, float cardZ)
        {
            screenPos.z = Mathf.Abs(camera.transform.position.z - cardZ);
            return camera.ScreenToWorldPoint(screenPos);
        }

        /// <summary>拖拽未达标：停在抬起的背面，不翻回正面。</summary>
        public void ResetRubShake()
        {
            if (_rubLockIndex < 0 || _player == null)
            {
                return;
            }

            var item = _player.Items[_rubLockIndex];
            if (item == null)
            {
                return;
            }

            item.transform.position = _rubRestWorldPos + SelectOffset(_player);
            FollowRubShadow();
            _rubPeakOffset = 0f;
            _rubDragStartTime = 0f;
            _rubSuccessFxShown = false;
            TintPlayerCard(_rubLockIndex, Color.white);
        }

        /// <summary>取消点选：翻回正面并落回原位。</summary>
        public void CancelRubPreview()
        {
            StopRubReplace(restore: false);
            if (_rubLockIndex < 0 || _player == null)
            {
                return;
            }

            var index = _rubLockIndex;
            _rubLockIndex = -1;
            _rubShakeReady = false;
            _rubSuccessFxShown = false;
            var item = _player.Items[index];
            if (item == null)
            {
                return;
            }

            var dest = _player.Points[index] != null ? _player.Points[index].position : _rubRestWorldPos;
            RestoreRubCardLayer(index);
            item.HideDragEffects();
            item.SetIdleFloat(true);
            item.MoveTo(dest, SelectLiftDuration, Ease.OutQuad)
                .OnUpdate(() => FollowRubShadowAt(index))
                .OnComplete(() =>
                {
                    if (_rubLockIndex != index)
                    {
                        SyncSlotShadow(_player, index, false);
                    }
                });
            item.FlipTo(CardFaceState.Front, FlipDuration);
            TintPlayerCard(index, Color.white);
        }

        /// <summary>
        /// 搓牌成功：手牌已由 Sync SetCard（仍锁在背面），翻回正面揭示新牌。
        /// </summary>
        public void CompleteRubFlip()
        {
            if (_rubLockIndex < 0 || _player == null)
            {
                return;
            }

            var index = _rubLockIndex;
            _rubLockIndex = -1;
            _rubShakeReady = false;
            var item = _player.Items[index];
            if (item == null)
            {
                return;
            }

            var selected = _session != null && _session.Player != null && _session.Player.IsCardSelected(index);
            var dest = _player.Points[index] != null ? _player.Points[index].position : _rubRestWorldPos;
            if (selected)
            {
                dest += SelectOffset(_player);
            }

            RestoreRubCardLayer(index);
            item.SetDragEffectVisible(false);
            item.SetIdleFloat(true);
            if (_rubSuccessFxShown)
            {
                item.HideDragSuccessLater();
            }
            else
            {
                item.PlayDragSuccessEffect(1.2f);
            }

            _rubSuccessFxShown = false;
            item.MoveTo(dest, SelectLiftDuration, Ease.OutQuad)
                .OnUpdate(() => FollowRubShadowAt(index))
                .OnComplete(() =>
                {
                    if (_rubLockIndex != index)
                    {
                        SyncSlotShadow(_player, index, selected);
                    }
                });
            item.FlipTo(CardFaceState.Front, FlipDuration);
            TintPlayerCard(index, Color.white);
        }

        public void CollectSelectedCards(GameSession session, SeatState seat, List<CardItem> dest)
        {
            if (dest == null)
            {
                return;
            }

            dest.Clear();
            var view = ViewOf(session, seat);
            if (view == null || seat == null)
            {
                return;
            }

            var anySelected = seat.CountSelectedCards() > 0;
            for (var i = 0; i < view.Items.Length; i++)
            {
                var item = view.Items[i];
                if (item == null || !view.Landed[i])
                {
                    continue;
                }

                if (anySelected && !seat.IsCardSelected(i))
                {
                    continue;
                }

                dest.Add(item);
            }
        }

        public void Dispose()
        {
            StopRubReplace(restore: false);
            StopHandReplace(cancelSession: true);
            _dealSeq?.Kill();
            _revealSeq?.Kill();
            _dealToken++;
            _revealToken++;
            _seeThroughToken++;
            _rubLockIndex = -1;
            _rubShakeReady = false;
            ClearAllItems();
            _shadows.Dispose();
            if (_resources != null)
            {
                // 只释放本实例通过 LoadAsync 持有的计数（缓存命中的不归这里放）
                if (_prefab != null && _prefabOwned)
                {
                    _resources.Release(ResResourcePaths.CardIcon);
                }

                if (_dealSfx != null)
                {
                    _resources.Release(ResResourcePaths.SfxDeal5Cards);
                }

                if (_revealSfx != null)
                {
                    _resources.Release(ResResourcePaths.SfxRevealCards3);
                }

                if (_rubSfx != null)
                {
                    _resources.Release(ResResourcePaths.SfxRubCards02);
                }
            }

            _prefab = null;
            _prefabOwned = false;
            _dealSfx = null;
            _revealSfx = null;
            _rubSfx = null;
            _resources = null;
        }

        /// <summary>重连瞬时落牌：座位上直接亮出当前手牌（含摊牌选中），不播放发牌飞牌。</summary>
        private void SnapPresentHands(GameSession session)
        {
            StopRubReplace(restore: false);
            StopHandReplace(cancelSession: true);
            _dealSeq?.Kill();
            _revealSeq?.Kill();
            _revealing = false;
            _dealing = false;
            _rubLockIndex = -1;
            _rubShakeReady = false;
            _shownDeal = session.DealSerial;
            _shownEnemyId = session.DisplayedEnemy != null ? session.DisplayedEnemy.Id : int.MinValue;
            _shownReveal = session.RevealPlaySerial;
            _shownReplace = session.ReplacePlaySerial;
            _dealToken++;
            _seeThroughToken++;
            ClearAllItems();
            HideDealPile();
            PlaceSeatHand(_player, session.Player, true, session);
            var displayed = session.DisplayedEnemy;
            if (displayed != null && displayed.Alive)
            {
                PlaceSeatHand(_other, displayed, false, session);
            }

            SyncIdleFloat();
            SyncSelectLift(session);
            DealFinished?.Invoke();
        }

        private void PlaceSeatHand(SeatView view, SeatState seat, bool player, GameSession session)
        {
            if (view == null || seat == null || seat.Hand == null)
            {
                return;
            }

            var count = GameBalance.CardsDealt(player, session != null ? session.Run : null);
            if (player)
            {
                LayoutPlayerHandPoints(count);
            }

            for (var i = 0; i < count && i < view.Points.Length; i++)
            {
                var point = view.Points[i];
                if (point == null)
                {
                    continue;
                }

                var card = ShownCard(seat, i, player, session);
                var item = SpawnLooseDealCard(card);
                if (item == null)
                {
                    continue;
                }

                item.gameObject.name = player
                    ? "PlayerCard" + (i + 1)
                    : "EnemyCard" + (i + 1);
                item.SetSpritesVisible(true);
                EnsureCollider(item);
                SnapToPoint(item, point);
                view.Items[i] = item;
                view.Landed[i] = true;
                ApplyFace(item, DesiredFace(session, seat, player, i), false);
                if (player)
                {
                    item.SetIdleFloat(true, i * item.IdleFloatPhaseStep);
                }
            }
        }

        private void PlayDeal(GameSession session)
        {
            StopRubReplace(restore: false);
            StopHandReplace(cancelSession: true);
            _dealSeq?.Kill();
            _revealSeq?.Kill();
            _revealing = false;
            _rubLockIndex = -1;
            _rubShakeReady = false;
            _shownDeal = session.DealSerial;
            _shownEnemyId = session.DisplayedEnemy != null ? session.DisplayedEnemy.Id : int.MinValue;
            _shownReveal = session.RevealPlaySerial;
            _shownReplace = session.ReplacePlaySerial;
            var token = ++_dealToken;
            _seeThroughToken++;
            _dealing = true;
            ClearAllItems();
            ShowDealPile();
            LayoutPlayerHandPoints(session.PlayerDealCount);

            var seats = CollectDealSeats(session);
            var seq = DOTween.Sequence();
            var delay = AppendShuffle(seq, token);
            var order = 0;
            var dealSfxPlayed = false;
            var maxRound = Math.Max(GameBalance.EnemyCardsDealt, session.PlayerDealCount);
            for (var round = 0; round < maxRound; round++)
            {
                for (var s = 0; s < seats.Count; s++)
                {
                    var view = seats[s].View;
                    var seat = seats[s].Seat;
                    if (round >= GameBalance.CardsDealt(seat.IsPlayer, session.Run))
                    {
                        continue;
                    }

                    if (view == null || round >= view.Points.Length || view.Points[round] == null)
                    {
                        continue;
                    }

                    var cardIndex = round;
                    var capturedOrder = order;
                    var playSfx = !dealSfxPlayed;
                    dealSfxPlayed = true;
                    seq.InsertCallback(delay, () =>
                    {
                        if (token != _dealToken)
                        {
                            return;
                        }

                        if (playSfx)
                        {
                            PlayDealSfx();
                        }

                        SpawnAndFly(view, seat, cardIndex, capturedOrder, token);
                    });
                    delay += DealStagger;
                    order++;
                }
            }

            seq.AppendInterval(DealMoveDuration);
            seq.AppendInterval(DealFlipPause);
            var playerCount = GameBalance.CardsDealt(true, session.Run);
            for (var i = 0; i < playerCount; i++)
            {
                var cardIndex = i;
                seq.AppendCallback(() =>
                {
                    if (token != _dealToken)
                    {
                        return;
                    }

                    FlipSeatCard(session, _player, session.Player, cardIndex);
                });
                if (i < playerCount - 1)
                {
                    seq.AppendInterval(DealStagger);
                }
            }

            seq.AppendInterval(FlipDuration);
            seq.AppendCallback(() =>
            {
                if (token != _dealToken)
                {
                    return;
                }

                HideDealPile();
            });
            seq.OnComplete(() =>
            {
                if (token != _dealToken)
                {
                    return;
                }

                _dealing = false;
                SyncAllFaces(session);
                DealFinished?.Invoke();
            });
            _dealSeq = seq;
        }

        private float AppendShuffle(Sequence seq, int token)
        {
            if (_dealPile == null)
            {
                return 0f;
            }

            var prefab = LoadPrefab();
            if (prefab == null)
            {
                AppLog.Warn(LogChannel.UI, "CardIcon prefab not found at Res/" + ResResourcePaths.CardIcon);
                return 0f;
            }

            var delay = 0f;
            var spawnCount = (PileCardCount + PileCardsPerSpawn - 1) / PileCardsPerSpawn;
            for (var s = 0; s < spawnCount; s++)
            {
                var first = s * PileCardsPerSpawn;
                seq.InsertCallback(delay, () =>
                {
                    if (token != _dealToken)
                    {
                        return;
                    }

                    for (var k = 0; k < PileCardsPerSpawn; k++)
                    {
                        var index = first + k;
                        if (index >= PileCardCount)
                        {
                            break;
                        }

                        var item = SpawnPileCard(prefab);
                        if (item == null)
                        {
                            return;
                        }

                        item.SetSpritesVisible(false);
                        _dealPile.Attach(item);
                        item.PlayShuffleAppear(index == PileCardCount - 1);
                    }
                });
                delay += ShuffleStagger;
            }

            return delay + ShuffleAppear02 - ShuffleStagger;
        }

        private void PlayReveal(GameSession session)
        {
            StopHandReplace(cancelSession: true);
            _revealSeq?.Kill();
            _shownReveal = session.RevealPlaySerial;
            var token = ++_revealToken;
            _revealing = true;
            var seq = DOTween.Sequence();
            var delay = 0.18f;
            for (var s = 0; s < session.RevealSeatIds.Count; s++)
            {
                var seatId = session.RevealSeatIds[s];
                var seat = SeatById(session, seatId);
                var view = ViewOf(session, seat);
                if (view == null || seat == null)
                {
                    continue;
                }

                if (!seat.IsPlayer)
                {
                    ClearSeatSeeThrough(view);
                }

                var count = CardCount(view, seat, session);
                for (var i = 0; i < count; i++)
                {
                    if (SkipEnemyUnselectedFlip(seat, i))
                    {
                        continue;
                    }

                    var cardIndex = i;
                    seq.InsertCallback(delay, () =>
                    {
                        if (token != _revealToken)
                        {
                            return;
                        }

                        FlipSeatCard(session, view, seat, cardIndex);
                    });
                    delay += RevealCardGap;
                }

                var capturedId = seatId;
                var capturedView = view;
                seq.InsertCallback(delay, () =>
                {
                    if (token != _revealToken)
                    {
                        return;
                    }

                    session.AnnounceSeatRevealed(capturedId);
                });
                delay += RevealSeatGap;
            }

            seq.InsertCallback(delay, () =>
            {
                if (token != _revealToken)
                {
                    return;
                }

                HighlightWinner(session);
            });
            delay += 0.08f;

            var winnerView = ViewOf(session, SeatById(session, session.RevealWinnerId));
            var winnerSeat = SeatById(session, session.RevealWinnerId);
            var settleCount = CardCount(winnerView, winnerSeat, session);
            var revealSfxPlayed = false;
            for (var i = 0; i < settleCount; i++)
            {
                if (winnerSeat != null && !winnerSeat.IsCardSelected(i))
                {
                    continue;
                }

                var cardIndex = i;
                var playSfx = !revealSfxPlayed;
                revealSfxPlayed = true;
                seq.InsertCallback(delay, () =>
                {
                    if (token != _revealToken)
                    {
                        return;
                    }

                    if (playSfx)
                    {
                        PlayRevealSfx();
                    }

                    PlaySettleCard(winnerView, cardIndex);
                });
                delay += SettleClipDuration;
            }

            delay += SettleClipDuration;
            seq.InsertCallback(delay, () =>
            {
                if (token != _revealToken)
                {
                    return;
                }

                _revealing = false;
                session.FinishRevealPlay();
            });
            _revealSeq = seq;
        }

        private void FlipSeatCard(GameSession session, SeatView view, SeatState seat, int cardIndex)
        {
            if (view == null || seat == null || cardIndex < 0 || cardIndex >= view.Items.Length)
            {
                return;
            }

            var item = view.Items[cardIndex];
            if (item == null || !view.Landed[cardIndex])
            {
                return;
            }

            var card = ShownCard(seat, cardIndex, seat.IsPlayer, session);
            if (!item.Card.Equals(card))
            {
                item.SetCard(card);
            }

            // PVP/透视后对手可能已是正面；先盖回再翻，才能播开牌动画。
            if (!seat.IsPlayer && item.FaceState == CardFaceState.Front)
            {
                item.SetFace(CardFaceState.Back);
            }

            ApplyFace(item, CardFaceState.Front, true);
        }

        private static void ClearSeatSeeThrough(SeatView view)
        {
            if (view == null)
            {
                return;
            }

            for (var i = 0; i < view.Items.Length; i++)
            {
                var item = view.Items[i];
                if (item != null && item.IsBackSeeThrough)
                {
                    item.SetBackSeeThrough(false);
                }
            }
        }

        /// <summary>敌人开牌只翻已锁定的 3 张；未选中的保持背面。</summary>
        private static bool SkipEnemyUnselectedFlip(SeatState seat, int cardIndex)
        {
            return seat != null &&
                   !seat.IsPlayer &&
                   seat.CountSelectedCards() > 0 &&
                   !seat.IsCardSelected(cardIndex);
        }

        /// <summary>透视阶段才抬敌人选中牌；开牌翻面/已亮牌时落回原位。</summary>
        private static bool EnemyUsesSelectLift(GameSession session, SeatState seat)
        {
            if (seat == null || seat.IsPlayer)
            {
                return true;
            }

            if (session == null)
            {
                return false;
            }

            return !seat.ShowCards &&
                   !session.CardsRevealed &&
                   session.Phase != GamePhase.Showdown;
        }

        private static void PlaySettleCard(SeatView view, int cardIndex)
        {
            if (view == null || cardIndex < 0 || cardIndex >= view.Items.Length)
            {
                return;
            }

            var item = view.Items[cardIndex];
            if (item != null && view.Landed[cardIndex])
            {
                item.PlaySettle();
            }
        }

        private void HighlightWinner(GameSession session)
        {
            if (session == null)
            {
                return;
            }

            var winner = SeatById(session, session.RevealWinnerId);
            var view = ViewOf(session, winner);
            TintSeat(view, winner, new Color(1f, 0.9f, 0.45f));
        }

        private static void TintSeat(SeatView view, SeatState seat, Color color)
        {
            if (view == null)
            {
                return;
            }

            var onlySelected = seat != null && seat.CountSelectedCards() > 0;
            for (var i = 0; i < view.Items.Length; i++)
            {
                var item = view.Items[i];
                if (item == null)
                {
                    continue;
                }

                if (onlySelected && !seat.IsCardSelected(i))
                {
                    continue;
                }

                var sr = item.CurrentRenderer;
                if (sr != null)
                {
                    sr.color = color;
                }
            }
        }

        private SeatView ViewOf(GameSession session, SeatState seat)
        {
            if (session == null || seat == null)
            {
                return null;
            }

            if (seat.IsPlayer)
            {
                return _player;
            }

            var displayed = session.DisplayedEnemy;
            if (displayed != null && displayed.Id == seat.Id)
            {
                return _other;
            }

            return null;
        }

        private static SeatState SeatById(GameSession session, int id)
        {
            if (session == null)
            {
                return null;
            }

            if (session.Player != null && session.Player.Id == id)
            {
                return session.Player;
            }

            for (var i = 0; i < session.Enemies.Length; i++)
            {
                if (session.Enemies[i].Id == id)
                {
                    return session.Enemies[i];
                }
            }

            return null;
        }

        private CardItem SpawnPileCard(GameObject prefab)
        {
            if (prefab == null || _dealPile == null || _dealPoint == null)
            {
                return null;
            }

            var item = CardItemPool.Rent(prefab);
            if (item == null)
            {
                return null;
            }

            item.gameObject.name = "DealCard" + (_dealPile.Count + 1);
            item.Initialize(
                default,
                CardFaceState.Back,
                _dealPile.GetWorldPosition(_dealPile.Count),
                _dealPoint.rotation,
                Vector3.one);
            return item;
        }

        private void SpawnAndFly(SeatView view, SeatState seat, int cardIndex, int order, int token)
        {
            if (view == null || view.Points[cardIndex] == null || seat == null || seat.Hand == null)
            {
                return;
            }

            var point = view.Points[cardIndex];
            var card = cardIndex < seat.Hand.Length ? seat.Hand[cardIndex] : default;
            var item = TakeDealCard(card, order);
            if (item == null)
            {
                return;
            }

            item.gameObject.name = view.IsPlayer
                ? "PlayerCard" + (cardIndex + 1)
                : point.parent.name + "_Card" + (cardIndex + 1);

            view.Items[cardIndex] = item;
            view.Landed[cardIndex] = false;

            var duration = DealMoveDuration;
            item.transform.DOScale(point.lossyScale, duration).SetEase(Ease.OutQuad);
            item.RotateTo(point.rotation * CardItem.FaceYaw(CardFaceState.Back), duration, Ease.OutCubic);
            item.MoveTo(point.position, duration, Ease.OutCubic).OnComplete(() =>
            {
                if (token != _dealToken || item == null)
                {
                    return;
                }

                SnapToPoint(item, point);
                view.Landed[cardIndex] = true;
                if (view.IsPlayer)
                {
                    // 呼吸波浪：只有己方手牌落位后开启，相位按 index 错开形成波浪；敌人牌不漂。
                    item.SetIdleFloat(true, cardIndex * item.IdleFloatPhaseStep);
                }
            });
        }

        private CardItem TakeDealCard(Card card, int order)
        {
            var item = _dealPile != null ? _dealPile.Pop() : null;
            if (item == null)
            {
                item = SpawnLooseDealCard(card);
            }

            if (item == null)
            {
                return null;
            }

            item.PlayDeal();
            _dealPile?.Peek()?.PlayDealHighlight();

            item.SetCard(card);
            item.SetFace(CardFaceState.Back);
            item.SetSortingOrder(FlySortingOrder(order));
            item.SetSpritesVisible(true);
            var sr = item.CurrentRenderer;
            if (sr != null)
            {
                sr.color = Color.white;
            }

            EnsureCollider(item);
            return item;
        }

        private int FlySortingOrder(int order)
        {
            var pileTop = _dealPile != null ? _dealPile.GetSortingOrder(_dealPile.Count) : 20;
            return pileTop + 10 + order;
        }

        private CardItem SpawnLooseDealCard(Card card)
        {
            var prefab = LoadPrefab();
            if (prefab == null)
            {
                AppLog.Warn(LogChannel.UI, "CardIcon prefab not found at Res/" + ResResourcePaths.CardIcon);
                return null;
            }

            var start = _dealPoint != null ? _dealPoint.position : _hud.position;
            var startRot = _dealPoint != null ? _dealPoint.rotation : Quaternion.identity;
            var item = CardItemPool.Rent(prefab);
            if (item == null)
            {
                return null;
            }

            item.Initialize(card, CardFaceState.Back, start, startRot, Vector3.one);
            return item;
        }

        private void SyncAllFaces(GameSession session)
        {
            if (session == null)
            {
                return;
            }

            SyncSeatFaces(_player, session.Player, true, session);
            var displayed = session.DisplayedEnemy;
            if (displayed != null)
            {
                SyncSeatFaces(_other, displayed, false, session);
            }
        }

        private void SyncRubSelectFx(GameSession session)
        {
            var show = session != null &&
                       session.SelectingRubTarget &&
                       !_dealing &&
                       !_rubPlaying;
            SetPlayerChangeSelectFx(show);
        }

        private void SetPlayerChangeSelectFx(bool show)
        {
            if (_player == null)
            {
                return;
            }

            for (var i = 0; i < _player.Items.Length; i++)
            {
                var item = _player.Items[i];
                if (item == null || !_player.Landed[i])
                {
                    continue;
                }

                item.SetDragEffectVisible(show);
            }
        }

        private void StopRubReplace(bool restore)
        {
            _rubPlayToken++;
            if (_rubSeq != null && _rubSeq.IsActive())
            {
                _rubSeq.Kill();
            }

            _rubSeq = null;
            _rubPlaying = false;
            if (_player != null && _rubLockIndex >= 0 && _rubLockIndex < _player.Items.Length)
            {
                var item = _player.Items[_rubLockIndex];
                item?.StopTweenAnimation();
                item?.HideChangeCardFx();
            }

            if (!restore)
            {
                return;
            }

            if (_rubLockIndex >= 0)
            {
                CancelRubPreview();
            }
        }

        /// <summary>
        /// 替换技能：旧牌翻面（aini_card_exit）后向右侧抛物线飞出屏幕，
        /// 再从牌堆位置重新发 5 张（aini_card_deal），最后翻回正面。
        /// 换牌数据此时已写入 Session，旧牌飞出阶段仍显示旧 CardItem 贴图。
        /// </summary>
        private void PlayHandReplace(GameSession session)
        {
            if (_player == null || session?.Player == null)
            {
                if (session != null)
                {
                    _shownReplace = session.ReplacePlaySerial;
                    session.CancelHandReplacePlay();
                }

                return;
            }

            StopRubReplace(restore: false);
            StopHandReplace(cancelSession: false);
            var token = ++_replaceToken;
            _replacing = true;
            _shownReplace = session.ReplacePlaySerial;
            _rubLockIndex = -1;
            _rubShakeReady = false;
            SetPlayerChangeSelectFx(false);

            var count = Mathf.Clamp(session.PlayerDealCount, 1, CardsPerHand);
            for (var i = 0; i < _player.Items.Length; i++)
            {
                _shadows.Return(ref _player.Shadows[i]);
            }

            // 1) 旧牌翻面：所有牌同时起播，等待最长的一张播完。
            var exitDuration = 0.05f;
            for (var i = 0; i < count; i++)
            {
                var item = _player.Items[i];
                if (item == null || !_player.Landed[i])
                {
                    continue;
                }

                item.StopMove();
                item.StopTweenAnimation();
                // 终止可能还在进行的根节点翻面，避免与 TweenTarget 的回收旋转叠加。
                item.SetFace(item.FaceState);
                item.SetIdleFloat(false);
                item.HideDragEffects();
                item.SetDragEffectVisible(false);
                exitDuration = Mathf.Max(exitDuration, item.PlayExit(ReplaceExitSpeed));
            }

            var seq = DOTween.Sequence();
            var camera = Camera.main;
            var flyStart = exitDuration * ReplaceFlyStartRatio;
            var flySortingBase = PlayerHandMaxSorting() + 20;

            // 2) 翻面还没结束时就开始往右侧抛物线飞出，翻飞同时进行。
            for (var i = 0; i < count; i++)
            {
                var item = _player.Items[i];
                if (item == null || !_player.Landed[i])
                {
                    continue;
                }

                item.SetSortingOrder(flySortingBase + i);
                var start = item.transform.position;
                var end = GetReplaceFlyEnd(camera, start);
                var control = (start + end) * 0.5f + Vector3.up * ReplaceFlyArcHeight;
                var fly = CreateReplaceFlyTween(item.transform, start, control, end, ReplaceFlyDuration);
                if (fly != null)
                {
                    seq.Insert(flyStart + i * ReplaceFlyStagger, fly);
                }

                var spinTarget = item.transform.eulerAngles + new Vector3(0f, 0f, -360f);
                var spin = item.transform
                    .DORotate(spinTarget, ReplaceFlyDuration, RotateMode.FastBeyond360)
                    .SetEase(Ease.Linear);
                seq.Insert(flyStart + i * ReplaceFlyStagger, spin);
            }

            var flyEnd = flyStart + Mathf.Max(0, count - 1) * ReplaceFlyStagger + ReplaceFlyDuration;
            // 翻面和飞行可能同时结束：等两者都播完再重置到牌堆。
            var oldFlyEnd = Mathf.Max(exitDuration, flyEnd);
            var resetAt = oldFlyEnd + ReplaceResetDelay;

            // 3) 旧牌全部飞出后，把同一批 CardItem 重置到牌堆位置，然后进入发牌阶段。
            seq.InsertCallback(resetAt, () =>
            {
                if (token != _replaceToken)
                {
                    return;
                }

                ResetReplaceDeckCards(session, count);
                StartReplaceDeal(session, count, token);
            });

            _replaceSeq = seq;
        }

        /// <summary>从牌堆逐张飞回手牌：牌已在 ResetReplaceDeckCards 中摆成一叠。</summary>
        private void StartReplaceDeal(GameSession session, int count, int token)
        {
            var seq = DOTween.Sequence();
            var dealSfxPlayed = false;
            for (var i = 0; i < count; i++)
            {
                var cardSeq = CreateReplaceDealSequence(i, token, () =>
                {
                    if (!dealSfxPlayed)
                    {
                        dealSfxPlayed = true;
                        PlayDealSfx();
                    }
                });
                if (cardSeq != null)
                {
                    seq.Insert(ReplaceDeckGap + i * DealStagger, cardSeq);
                }
            }

            var dealEnd = ReplaceDeckGap
                          + Mathf.Max(0, count - 1) * DealStagger
                          + Mathf.Max(DealMoveDuration, ReplaceDealClipDuration)
                          + ReplaceDealSettle;

            // 4) 全部落位后统一翻正面。
            seq.InsertCallback(dealEnd, () =>
            {
                if (token != _replaceToken || _player == null || session.Player == null)
                {
                    return;
                }

                SyncSeatFaces(_player, session.Player, true, session);
            });

            // 5) 收尾。
            seq.InsertCallback(dealEnd + FlipDuration, () =>
            {
                if (token != _replaceToken)
                {
                    return;
                }

                _replacing = false;
                _replaceSeq = null;
                _replaceDealSeq = null;
                session.FinishHandReplacePlay();
            });

            _replaceDealSeq = seq;
        }

        private static Vector3 GetReplaceFlyEnd(Camera camera, Vector3 start)
        {
            if (camera == null)
            {
                return start + new Vector3(14f, 2f, 0f);
            }

            var z = Mathf.Abs(camera.transform.position.z - start.z);
            var end = camera.ViewportToWorldPoint(new Vector3(1.35f, 0.55f, z));
            end.z = start.z;
            end.y = Mathf.Max(end.y, start.y + 1.5f);
            return end;
        }

        private static Tween CreateReplaceFlyTween(
            Transform target,
            Vector3 start,
            Vector3 control,
            Vector3 end,
            float duration)
        {
            if (target == null)
            {
                return null;
            }

            return DOTween.To(
                () => 0f,
                t =>
                {
                    if (target == null)
                    {
                        return;
                    }

                    var inv = 1f - t;
                    target.position = inv * inv * start + 2f * inv * t * control + t * t * end;
                },
                1f,
                duration).SetEase(Ease.Linear);
        }

        /// <summary>旧牌飞出后：同一批 CardItem 重置到牌堆位置，等下一阶段逐张发牌。</summary>
        private void ResetReplaceDeckCards(GameSession session, int count)
        {
            if (_player == null || session?.Player == null)
            {
                return;
            }

            var deckPosition = _dealPoint != null
                ? _dealPoint.position
                : (_hud != null ? _hud.position : Vector3.zero);
            var deckRotation = _dealPoint != null ? _dealPoint.rotation : Quaternion.identity;
            var sortingBase = PlayerHandMaxSorting() + 20;
            var hand = session.Player.Hand;
            for (var i = 0; i < count; i++)
            {
                var item = _player.Items[i];
                if (item == null)
                {
                    continue;
                }

                item.StopMove();
                item.StopTweenAnimation();
                item.transform.SetParent(null, true);
                var card = hand != null && i < hand.Length ? hand[i] : default;
                var position = deckPosition + new Vector3(i * 0.03f, i * -0.02f, 0f);
                item.Initialize(card, CardFaceState.Back, position, deckRotation, Vector3.one);
                item.SetSpritesVisible(true);
                item.SetSortingOrder(sortingBase + i);
                item.SetIdleFloat(false);
                EnsureCollider(item);
                _player.Landed[i] = false;
            }
        }

        /// <summary>从牌堆位置飞到第 index 个卡位，飞行期间播 aini_card_deal。</summary>
        private Sequence CreateReplaceDealSequence(
            int index,
            int token,
            Action onStart)
        {
            if (_player == null || index < 0 || index >= CardsPerHand)
            {
                return null;
            }

            var item = _player.Items[index];
            var point = _player.Points[index];
            if (item == null || point == null)
            {
                return null;
            }

            var seq = DOTween.Sequence();
            seq.AppendCallback(() =>
            {
                if (token != _replaceToken)
                {
                    return;
                }

                onStart?.Invoke();
                item.PlayDeal();
            });
            // 让 PlayDeal 先执行，再抓取飞行起点。
            seq.AppendInterval(0.001f);
            seq.Append(item.transform.DOMove(point.position, DealMoveDuration).SetEase(Ease.OutCubic));
            seq.Join(item.transform
                .DORotateQuaternion(point.rotation * CardItem.FaceYaw(CardFaceState.Back), DealMoveDuration)
                .SetEase(Ease.OutCubic));
            seq.Join(item.transform
                .DOScale(point.lossyScale, DealMoveDuration)
                .SetEase(Ease.OutQuad));
            seq.OnComplete(() =>
            {
                if (token != _replaceToken || _player == null || item == null)
                {
                    return;
                }

                SnapToPoint(item, point);
                _player.Landed[index] = true;
                item.SetIdleFloat(false);
                var sr = item.CurrentRenderer;
                if (sr != null)
                {
                    sr.color = Color.white;
                }
            });
            return seq;
        }

        private void StopHandReplace(bool cancelSession)
        {
            _replaceToken++;
            if (_replaceSeq != null && _replaceSeq.IsActive())
            {
                _replaceSeq.Kill();
            }

            if (_replaceDealSeq != null && _replaceDealSeq.IsActive())
            {
                _replaceDealSeq.Kill();
            }

            _replaceSeq = null;
            _replaceDealSeq = null;
            _replacing = false;
            if (cancelSession)
            {
                _session?.CancelHandReplacePlay();
            }
        }

        private void SyncSeatFaces(SeatView view, SeatState seat, bool player, GameSession session)
        {
            if (view == null || seat == null)
            {
                return;
            }

            for (var i = 0; i < CardsPerHand; i++)
            {
                var item = view.Items[i];
                if (item == null || !view.Landed[i])
                {
                    continue;
                }

                var card = ShownCard(seat, i, player, session);
                if (!item.Card.Equals(card))
                {
                    item.SetCard(card);
                }

                // 搓牌预览锁面：允许换牌数据，但不强制翻回正面。
                if (player && (i == _rubLockIndex || session.PendingRubIndex == i))
                {
                    continue;
                }

                var desired = DesiredFace(session, seat, player, i);
                ApplyFace(item, desired, true);
                var sr = item.CurrentRenderer;
                if (sr == null)
                {
                    continue;
                }

                sr.color = Color.white;
                if (seat.Folded)
                {
                    sr.color = new Color(0.7f, 0.7f, 0.74f, 1f);
                }
                else if (session.RevealWinnerId == seat.Id &&
                         (session.Phase == GamePhase.Showdown ||
                          session.Phase == GamePhase.WaitingAttack ||
                          session.Phase == GamePhase.RoundSettle) &&
                         (seat.CountSelectedCards() == 0 || seat.IsCardSelected(i)))
                {
                    sr.color = new Color(1f, 0.92f, 0.55f, 1f);
                }
                else if (player && session.Run.MagnifierThisRound && session.Run.PeekSuitUsed &&
                    session.Run.PeekSuitIndex == i && item.FaceState == CardFaceState.Back)
                {
                    sr.color = SuitTint(seat.Hand[i].Suit);
                }
            }
        }

        private void SyncSelectLift(GameSession session)
        {
            if (_dealing || session == null)
            {
                return;
            }

            LiftSeat(_player, session.Player, SelectOffset(_player));
            var displayed = session.DisplayedEnemy;
            if (displayed != null)
            {
                LiftSeat(_other, displayed, SelectOffset(_other), EnemyUsesSelectLift(session, displayed));
            }
        }

        /// <summary>透视：先抬牌，抬完再 SetBackSeeThrough。关掉透视则立刻恢复。</summary>
        private void SyncSeeThrough(GameSession session)
        {
            if (session == null)
            {
                return;
            }

            var token = ++_seeThroughToken;
            ApplySeatSeeThrough(_player, session.Player, true, session, token);
            var displayed = session.DisplayedEnemy;
            if (displayed != null)
            {
                ApplySeatSeeThrough(_other, displayed, false, session, token);
            }
        }

        private void ApplySeatSeeThrough(
            SeatView view,
            SeatState seat,
            bool player,
            GameSession session,
            int token)
        {
            if (view == null || seat == null)
            {
                return;
            }

            for (var i = 0; i < CardsPerHand; i++)
            {
                var item = view.Items[i];
                if (item == null || !view.Landed[i])
                {
                    continue;
                }

                var desired = DesiredFace(session, seat, player, i);
                var want = desired == CardFaceState.Back &&
                           session.IsSpyRevealed(seat.Id, i) &&
                           EnemyUsesSelectLift(session, seat);
                if (want == item.IsBackSeeThrough)
                {
                    continue;
                }

                if (!want)
                {
                    item.SetBackSeeThrough(false);
                    continue;
                }

                var dest = view.Points[i] != null
                    ? view.Points[i].position + (seat.IsCardSelected(i) ? SelectOffset(view) : Vector3.zero)
                    : item.transform.position;
                var lifting = (item.transform.position - dest).sqrMagnitude >= 0.0004f;
                if (!lifting)
                {
                    item.SetBackSeeThrough(true);
                    continue;
                }

                var captured = item;
                DOVirtual.DelayedCall(SelectLiftDuration, () =>
                {
                    if (token != _seeThroughToken || captured == null)
                    {
                        return;
                    }

                    captured.SetBackSeeThrough(true);
                });
            }
        }

        /// <summary>
        /// 选中牌朝玩家方向挪开：玩家向上，敌人（上方 otherNode）向下。
        /// </summary>
        private Vector3 SelectOffset(SeatView view)
        {
            if (view == null || view.IsPlayer)
            {
                return Vector3.up * SelectLift;
            }

            return Vector3.down * SelectLift;
        }

        private void LiftSeat(SeatView view, SeatState seat, Vector3 offset, bool allowLift = true)
        {
            if (view == null || seat == null)
            {
                return;
            }

            for (var i = 0; i < view.Items.Length; i++)
            {
                var item = view.Items[i];
                if (item == null || !view.Landed[i] || view.Points[i] == null)
                {
                    continue;
                }

                // 搓牌拖拽/换牌动画中自己管位置，避免 Sync 把牌拽回原点。
                if (view.IsPlayer && (i == _rubLockIndex || (_session != null && _session.PendingRubIndex == i)))
                {
                    continue;
                }

                var selected = allowLift &&
                               (seat.IsCardSelected(i) ||
                                (view.IsPlayer && _session != null && _session.SelectingRubTarget));
                var dest = view.Points[i].position + (selected ? offset : Vector3.zero);
                if ((item.transform.position - dest).sqrMagnitude < 0.0004f)
                {
                    SyncSlotShadow(view, i, selected);
                    continue;
                }

                if (selected)
                {
                    SyncSlotShadow(view, i, true);
                    item.MoveTo(dest, SelectLiftDuration, Ease.OutQuad);
                    continue;
                }

                // 取消选中：阴影保留到落位动画结束再回收；期间重新选中则 OnComplete 里跳过回收。
                var index = i;
                var lift = allowLift;
                item.MoveTo(dest, SelectLiftDuration, Ease.OutQuad).OnComplete(() =>
                {
                    var stillLifted = lift &&
                                      (seat.IsCardSelected(index) ||
                                       (view.IsPlayer && _session != null && _session.SelectingRubTarget));
                    SyncSlotShadow(view, index, stillLifted);
                });
            }
        }

        private void SyncSlotShadow(SeatView view, int index, bool lifted)
        {
            if (lifted)
            {
                if (view.Shadows[index] != null)
                {
                    return;
                }

                view.Shadows[index] = _shadows.Rent(view.Points[index]);
                return;
            }

            _shadows.Return(ref view.Shadows[index]);
        }

        private void FollowRubShadow()
        {
            if (_rubLockIndex >= 0)
            {
                FollowRubShadowAt(_rubLockIndex);
            }
        }

        private void FollowRubShadowAt(int index)
        {
            if (_player == null || index < 0 || index >= CardsPerHand)
            {
                return;
            }

            var shadow = _player.Shadows[index];
            var item = _player.Items[index];
            if (shadow == null || item == null)
            {
                return;
            }

            var pos = item.transform.position;
            shadow.position = new Vector3(pos.x, pos.y, shadow.position.z);
        }

        private void BringRubCardToFront(int index)
        {
            var item = _player.Items[index];
            if (item == null)
            {
                return;
            }

            var sr = item.CurrentRenderer;
            _rubSavedOrder = sr != null ? sr.sortingOrder : 0;
            var top = PlayerHandMaxSorting() + 20;
            item.SetSortingOrder(top);
            SetShadowSorting(_player.Shadows[index], top - 1);
        }

        private void RestoreRubCardLayer(int index)
        {
            if (_player == null || index < 0 || index >= CardsPerHand)
            {
                return;
            }

            var item = _player.Items[index];
            if (item != null)
            {
                item.SetSortingOrder(_rubSavedOrder);
            }

            SetShadowSorting(_player.Shadows[index], 2);
        }

        private int PlayerHandMaxSorting()
        {
            var max = 0;
            if (_player == null)
            {
                return max;
            }

            for (var i = 0; i < CardsPerHand; i++)
            {
                var item = _player.Items[i];
                var sr = item != null ? item.CurrentRenderer : null;
                if (sr != null && sr.sortingOrder > max)
                {
                    max = sr.sortingOrder;
                }
            }

            return max;
        }

        private static void SetShadowSorting(Transform shadow, int order)
        {
            if (shadow == null)
            {
                return;
            }

            var sr = shadow.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sortingOrder = order;
            }
        }

        private static int CardCount(SeatView view, SeatState seat, GameSession session)
        {
            if (view == null)
            {
                return 0;
            }

            return GameBalance.CardsDealt(seat != null && seat.IsPlayer, session?.Run);
        }

        private static CardFaceState DesiredFace(GameSession session, SeatState seat, bool player, int cardIndex)
        {
            if (session == null || seat == null)
            {
                return CardFaceState.Back;
            }

            if (player &&
                session.Phase == GamePhase.WaitingRub &&
                session.PendingRubIndex == cardIndex)
            {
                return CardFaceState.Back;
            }

            if (seat.ShowCards || session.CardsRevealed)
            {
                if (SkipEnemyUnselectedFlip(seat, cardIndex))
                {
                    return CardFaceState.Back;
                }

                return CardFaceState.Front;
            }

            if (player && session.Phase == GamePhase.WaitingRub && session.PendingRubIndex == cardIndex)
            {
                return CardFaceState.Back;
            }

            if (player && seat.Looked)
            {
                return CardFaceState.Front;
            }

            return CardFaceState.Back;
        }

        private static void ApplyFace(CardItem item, CardFaceState face, bool animate)
        {
            if (item == null || item.FaceState == face)
            {
                return;
            }

            if (animate)
            {
                item.FlipTo(face, FlipDuration);
            }
            else
            {
                item.SetFace(face);
            }
        }

        private void ApplySeatVisibility(GameSession session)
        {
            if (_other != null && _other.Node != null)
            {
                var displayed = session != null ? session.DisplayedEnemy : null;
                _other.Node.gameObject.SetActive(displayed != null && displayed.Alive);
            }

            if (_player != null && _player.Node != null)
            {
                _player.Node.gameObject.SetActive(true);
            }
        }

        private List<(SeatView View, SeatState Seat)> CollectDealSeats(GameSession session)
        {
            var list = new List<(SeatView, SeatState)>();
            if (_player != null && session.Player != null)
            {
                list.Add((_player, session.Player));
            }

            var displayed = session.DisplayedEnemy;
            if (_other != null && displayed != null && displayed.Alive)
            {
                list.Add((_other, displayed));
            }

            return list;
        }

        private void EnsureDisplayedEnemy(GameSession session)
        {
            var displayed = session != null ? session.DisplayedEnemy : null;
            var id = displayed != null ? displayed.Id : int.MinValue;
            if (id == _shownEnemyId)
            {
                return;
            }

            _shownEnemyId = id;
            if (displayed == null || !displayed.Alive)
            {
                ClearSeatItems(_other);
                return;
            }

            PlaceEnemyHand(_other, displayed, session);
        }

        private void PlaceEnemyHand(SeatView view, SeatState seat, GameSession session)
        {
            ClearSeatItems(view);
            if (view == null || seat == null || seat.Hand == null)
            {
                return;
            }

            var count = GameBalance.CardsDealt(false, session != null ? session.Run : null);
            for (var i = 0; i < count && i < view.Points.Length; i++)
            {
                var point = view.Points[i];
                if (point == null)
                {
                    continue;
                }

                var card = i < seat.Hand.Length ? seat.Hand[i] : default;
                var item = SpawnLooseDealCard(card);
                if (item == null)
                {
                    continue;
                }

                item.gameObject.name = "EnemyCard" + (i + 1);
                item.SetSpritesVisible(true);
                EnsureCollider(item);
                SnapToPoint(item, point);
                view.Items[i] = item;
                view.Landed[i] = true;
                ApplyFace(item, DesiredFace(session, seat, false, i), false);
            }
        }

        private void BindEnemySlots(Transform hud)
        {
            _other = BuildSeat(FindChild(hud, "otherNode"), false);
        }

        private Vector3[] _playerPointHomes;

        /// <summary>手牌不足 5 张时，把用到的落点平移到整手中心，避免 4 张靠左。</summary>
        private void LayoutPlayerHandPoints(int dealt)
        {
            if (_player == null)
            {
                return;
            }

            var points = _player.Points;
            if (_playerPointHomes == null)
            {
                _playerPointHomes = new Vector3[points.Length];
                for (var i = 0; i < points.Length; i++)
                {
                    if (points[i] != null)
                    {
                        _playerPointHomes[i] = points[i].localPosition;
                    }
                }
            }

            var total = 0;
            var full = Vector3.zero;
            for (var i = 0; i < points.Length; i++)
            {
                if (points[i] == null)
                {
                    continue;
                }

                points[i].localPosition = _playerPointHomes[i];
                full += _playerPointHomes[i];
                total++;
            }

            if (dealt <= 0 || dealt >= total || total == 0)
            {
                return;
            }

            var used = 0;
            var usedCenter = Vector3.zero;
            var take = Math.Min(dealt, points.Length);
            for (var i = 0; i < take; i++)
            {
                if (points[i] == null)
                {
                    continue;
                }

                usedCenter += _playerPointHomes[i];
                used++;
            }

            if (used == 0)
            {
                return;
            }

            var shift = full / total - usedCenter / used;
            for (var i = 0; i < points.Length; i++)
            {
                if (points[i] == null)
                {
                    continue;
                }

                points[i].localPosition = _playerPointHomes[i] + shift;
            }
        }

        private static SeatView BuildSeat(Transform node, bool player)
        {
            if (node == null)
            {
                return null;
            }

            var view = new SeatView { Node = node, IsPlayer = player };
            for (var i = 0; i < CardsPerHand; i++)
            {
                view.Points[i] = FindCardPoint(node, i + 1);
            }

            return view;
        }

        private static Transform FindCardPoint(Transform root, int index)
        {
            if (root == null)
            {
                return null;
            }

            var cardNode = FindChild(root, "cardNode") ?? root;
            return FindChild(cardNode, "carpoint" + index)
                   ?? FindChild(cardNode, "cardpoint" + index)
                   ?? FindChild(cardNode, "CardPoint" + index);
        }

        private static void HideLegacyIcons(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var cardNode = FindChild(root, "cardNode") ?? root;
            for (var i = 0; i < cardNode.childCount; i++)
            {
                var child = cardNode.GetChild(i);
                if (child.name.StartsWith("CardIcon") || child.name.StartsWith("Square"))
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        private void ShowDealPile()
        {
            if (_dealPile != null && _dealPoint != null)
            {
                _dealPoint.gameObject.SetActive(true);
            }
        }

        private void HideDealPile()
        {
            _dealPile?.Clear();
            if (_dealPile != null && _dealPoint != null)
            {
                _dealPoint.gameObject.SetActive(false);
            }
        }

        private void ClearAllItems()
        {
            _dealPile?.Clear();
            ClearSeatItems(_player);
            ClearSeatItems(_other);
            _shownEnemyId = int.MinValue;
        }

        private void ClearSeatItems(SeatView view)
        {
            if (view == null)
            {
                return;
            }

            for (var i = 0; i < view.Items.Length; i++)
            {
                if (view.Items[i] != null)
                {
                    CardItemPool.Return(view.Items[i]);
                    view.Items[i] = null;
                }

                _shadows.Return(ref view.Shadows[i]);
                view.Landed[i] = false;
            }
        }

        private static void SnapToPoint(CardItem item, Transform point)
        {
            var t = item.transform;
            t.SetParent(point, true);
            t.localPosition = Vector3.zero;
            t.localScale = Vector3.one;
            item.SetFace(item.FaceState);
        }

        private static void EnsureCollider(CardItem item)
        {
            var sr = item != null ? item.CurrentRenderer : null;
            if (sr == null)
            {
                return;
            }

            var col = sr.GetComponent<BoxCollider2D>();
            if (col == null)
            {
                col = sr.gameObject.AddComponent<BoxCollider2D>();
            }

            col.size = sr.sprite != null ? sr.sprite.bounds.size : new Vector2(1f, 1.5f);
        }

        private SpriteRenderer PlayerRenderer(int index)
        {
            if (_player == null || index < 0 || index >= _player.Items.Length)
            {
                return null;
            }

            var item = _player.Items[index];
            return item != null ? item.CurrentRenderer : null;
        }

        private GameObject LoadPrefab()
        {
            if (_prefab != null)
            {
                return _prefab;
            }

            if (_resources == null)
            {
                AppLog.Warn(LogChannel.UI, "CardIcon prefab not loaded: IResourceService is missing.");
                return null;
            }

            // WebGL/微信单线程：禁止 GetResult 等 UnityWebRequest，否则模拟器直接未响应。
            if (_resources.TryGetCached(ResResourcePaths.CardIcon, out GameObject cached) && cached != null)
            {
                _prefab = cached;
                _prefabOwned = false;
                return _prefab;
            }

            AppLog.Warn(
                LogChannel.UI,
                "CardIcon not cached. Preload with await LoadAsync before dealing. key=" + ResResourcePaths.CardIcon);
            return null;
        }

        /// <summary>发牌前 await。替代已删除的同步 GetResult 加载。</summary>
        public async Task EnsurePrefabAsync()
        {
            if (_prefab != null || _resources == null)
            {
                return;
            }

            if (_resources.TryGetCached(ResResourcePaths.CardIcon, out GameObject cached) && cached != null)
            {
                _prefab = cached;
                _prefabOwned = false;
                return;
            }

            try
            {
                _prefab = await _resources.LoadAsync<GameObject>(ResResourcePaths.CardIcon);
                _prefabOwned = _prefab != null;
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.UI, "CardIcon prefab not found at Res/" + ResResourcePaths.CardIcon + ": " + ex.Message);
            }
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindChild(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// 老花眼金花：已选三张同色时，少数花色贴图改成多数花色。手里牌数据不改。
        /// </summary>
        private static Card ShownCard(SeatState seat, int index, bool player, GameSession session)
        {
            if (seat?.Hand == null || index < 0 || index >= seat.Hand.Length)
            {
                return default;
            }

            var card = seat.Hand[index];
            if (!player ||
                session?.Run == null ||
                !RelicMechanics.HasMechanism(session.Run, MechanismType.SpecialFlush) ||
                seat.CountSelectedCards() != GameBalance.OpenHandSize ||
                !seat.IsCardSelected(index))
            {
                return card;
            }

            var selected = HandEvaluator.CopySelectedCards(seat.Hand, seat.CardSelected);
            if (!HandEvaluator.TryColorFlushDisplaySuit(selected, out var displaySuit))
            {
                return card;
            }

            return HandEvaluator.WithSuit(card, displaySuit);
        }

        private static Color SuitTint(Suit suit)
        {
            switch (suit)
            {
                case Suit.Heart: return new Color(0.85f, 0.25f, 0.25f);
                case Suit.Diamond: return new Color(0.9f, 0.45f, 0.2f);
                case Suit.Club: return new Color(0.2f, 0.45f, 0.25f);
                default: return new Color(0.2f, 0.25f, 0.55f);
            }
        }
    }
}
