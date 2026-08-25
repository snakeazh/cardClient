using System;
using System.Collections.Generic;
using App.Game;
using App.Resources;
using DG.Tweening;
using Framework.Assets;
using TMPro;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 开牌结算点数演出：选中牌特效飞到攻击数字，再按点数、倍率刷新伤害。
    /// </summary>
    public sealed class SettlePointCutscene
    {
        private const float Fx01Duration = 0.45f;
        private const float Fx02MoveDuration = 0.4f;
        private const float Fx03Duration = 0.55f;
        private const float BeilvFloatDuration = 0.1f;
        private const float BeilvFloatPixels = 48f;
        private const float CardTypeNumLowDuration = 0.5f;
        private const float HighDuration = 0.4f;
        private const int FxSortingOrder = 220;

        private IResourceService _resources;
        private RectTransform _uiRoot;
        private Canvas _uiCanvas;
        private GameObject _fx01Prefab;
        private GameObject _fx02Prefab;
        private GameObject _fx03Prefab;
        private Sequence _seq;
        private int _playToken;
        private readonly List<GameObject> _spawned = new List<GameObject>(8);
        private Vector2 _beilvHome;
        private RectTransform _beilvRt;

        public bool IsPlaying { get; private set; }

        public void Bind(IResourceService resources, Transform uiRoot)
        {
            _resources = resources;
            _uiRoot = uiRoot as RectTransform;
            if (_uiRoot == null && uiRoot != null)
            {
                _uiRoot = uiRoot.GetComponent<RectTransform>();
            }

            _uiCanvas = uiRoot != null ? uiRoot.GetComponentInParent<Canvas>() : null;
            Preload();
        }

        public void Play(
            IReadOnlyList<CardItem> cards,
            PlayerItem attackItem,
            TMP_Text beilvNum,
            string relicMultText,
            TMP_Text cardTypeNum,
            string cardTypeMultText,
            int baseAttack,
            int cardPoints,
            int finalDamage,
            Action<int> onAttackNumber,
            Action onDone)
        {
            Kill();
            IsPlaying = true;
            var token = ++_playToken;
            var attackRect = attackItem != null ? attackItem.AttackValueRect : null;
            var targetPos = attackRect != null ? attackRect.position : Vector3.zero;
            _beilvRt = beilvNum != null ? beilvNum.rectTransform : null;
            if (_beilvRt != null)
            {
                _beilvHome = _beilvRt.anchoredPosition;
                _beilvRt.gameObject.SetActive(false);
            }

            if (attackItem != null)
            {
                SetAttackNumber(attackItem, Mathf.Max(0, baseAttack), onAttackNumber);
                attackItem.PlayAttackNumberShake(false, false);
            }

            _seq = DOTween.Sequence();
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                SpawnFx01(cards);
            });
            _seq.AppendInterval(Fx01Duration);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                HideSpawned();
                SpawnFx02(cards, targetPos);
            });
            _seq.AppendInterval(Fx02MoveDuration);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                HideSpawned();
                if (attackItem != null)
                {
                    SetAttackNumber(attackItem, Mathf.Max(0, baseAttack) + Mathf.Max(0, cardPoints), onAttackNumber);
                    attackItem.PlayAttackNumberShake(true, false);
                }

                SpawnFx03(targetPos);
            });
            _seq.AppendInterval(Fx03Duration);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                HideSpawned();
                if (attackItem != null)
                {
                    attackItem.PlayAttackNumberShake(false, false);
                }

                if (cardTypeNum != null)
                {
                    if (!string.IsNullOrEmpty(cardTypeMultText))
                    {
                        cardTypeNum.text = cardTypeMultText;
                    }

                    PlayNumberShake(cardTypeNum, true, false);
                }

                ShowBeilv(beilvNum, relicMultText);
            });
            _seq.AppendInterval(CardTypeNumLowDuration);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                if (attackItem != null)
                {
                    SetAttackNumber(attackItem, Mathf.Max(1, finalDamage), onAttackNumber);
                    attackItem.PlayAttackNumberShake(false, true);
                }
            });
            _seq.AppendInterval(HighDuration);
            _seq.OnComplete(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                if (attackItem != null)
                {
                    attackItem.PlayAttackNumberShake(false, false);
                }

                PlayNumberShake(cardTypeNum, false, false);
                RestoreBeilv();
                IsPlaying = false;
                onDone?.Invoke();
            });
        }

        public void Dispose()
        {
            Kill();
            _fx01Prefab = null;
            _fx02Prefab = null;
            _fx03Prefab = null;
            _resources = null;
            _uiRoot = null;
            _uiCanvas = null;
        }

        private static void SetAttackNumber(PlayerItem attackItem, int value, Action<int> onAttackNumber)
        {
            attackItem.SetAttack(value);
            onAttackNumber?.Invoke(value);
        }

        private static void PlayNumberShake(TMP_Text text, bool low, bool high)
        {
            if (text == null)
            {
                return;
            }

            var animator = text.GetComponent<Animator>();
            if (animator == null)
            {
                return;
            }

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
            animator.SetBool("low", false);
            animator.SetBool("high", false);
            animator.SetBool("normal", false);

            var state = low ? "NumberShackLow" : high ? "NumberShackHigh" : "NumberNormal";
            animator.SetBool("low", low);
            animator.SetBool("high", high);
            animator.SetBool("normal", !low && !high);
            animator.Play(state, 0, 0f);
            animator.Update(0f);

            // bool 若一直为 true，AnyState 会每帧重进，看起来像没播。
            if (low)
            {
                animator.SetBool("low", false);
            }

            if (high)
            {
                animator.SetBool("high", false);
            }
        }

        private void Preload()
        {
            _fx01Prefab = LoadPrefab(ResResourcePaths.CardPointEffect01);
            _fx02Prefab = LoadPrefab(ResResourcePaths.CardPointEffect02);
            _fx03Prefab = LoadPrefab(ResResourcePaths.CardPointEffect03);
        }

        private GameObject LoadPrefab(string key)
        {
            if (_resources == null || string.IsNullOrEmpty(key))
            {
                return null;
            }

            try
            {
                return _resources.LoadAsync<GameObject>(key).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void SpawnFx01(IReadOnlyList<CardItem> cards)
        {
            if (_fx01Prefab == null || cards == null)
            {
                return;
            }

            for (var i = 0; i < cards.Count; i++)
            {
                SpawnOnCard(_fx01Prefab, cards[i]);
            }
        }

        private void SpawnFx02(IReadOnlyList<CardItem> cards, Vector3 targetPos)
        {
            if (_fx02Prefab == null || cards == null || cards.Count == 0)
            {
                return;
            }

            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (card == null)
                {
                    continue;
                }

                var anchor = card.FxAnchor != null ? card.FxAnchor : card.transform;
                var start = CardWorldToUiWorld(anchor.position, targetPos);
                var go = SpawnOnUi(_fx02Prefab, start);
                if (go == null)
                {
                    continue;
                }

                ClearTrails(go);
                go.transform.DOMove(targetPos, Fx02MoveDuration).SetEase(Ease.InQuad);
            }
        }

        private void SpawnFx03(Vector3 targetPos)
        {
            if (_fx03Prefab == null)
            {
                return;
            }

            SpawnOnUi(_fx03Prefab, targetPos);
        }

        private GameObject SpawnOnCard(GameObject prefab, CardItem card)
        {
            if (prefab == null || card == null)
            {
                return null;
            }

            var anchor = card.FxAnchor != null ? card.FxAnchor : card.transform;
            var go = UnityEngine.Object.Instantiate(prefab, anchor, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.SetActive(true);
            ApplySorting(go, FxSortingOrder);
            RestartParticles(go);
            ClearTrails(go);
            _spawned.Add(go);
            return go;
        }

        private GameObject SpawnOnUi(GameObject prefab, Vector3 uiWorld)
        {
            if (prefab == null)
            {
                return null;
            }

            var go = UnityEngine.Object.Instantiate(prefab, _uiRoot, false);
            go.transform.position = uiWorld;
            go.transform.localRotation = Quaternion.identity;
            go.SetActive(true);
            ApplySorting(go, FxSortingOrder);
            RestartParticles(go);
            ClearTrails(go);
            _spawned.Add(go);
            return go;
        }

        /// <summary>
        /// 桌上 2D 牌世界坐标 → 屏幕 → GameUI 平面世界坐标。
        /// </summary>
        private Vector3 CardWorldToUiWorld(Vector3 cardWorld, Vector3 fallback)
        {
            if (_uiRoot == null)
            {
                return fallback;
            }

            var worldCam = Camera.main;
            if (worldCam == null)
            {
                return fallback;
            }

            var screen = worldCam.WorldToScreenPoint(cardWorld);
            if (screen.z < 0f)
            {
                return fallback;
            }

            var canvas = _uiCanvas != null ? _uiCanvas.rootCanvas : _uiCanvas;
            var uiCam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(_uiRoot, screen, uiCam, out var uiWorld))
            {
                return fallback;
            }

            return uiWorld;
        }

        private void ShowBeilv(TMP_Text beilvNum, string relicMultText)
        {
            if (beilvNum == null || string.IsNullOrEmpty(relicMultText) || _beilvRt == null)
            {
                return;
            }

            beilvNum.text = relicMultText;
            _beilvRt.DOKill();
            _beilvRt.anchoredPosition = _beilvHome;
            _beilvRt.gameObject.SetActive(true);
            _beilvRt.DOAnchorPos(_beilvHome + new Vector2(0f, BeilvFloatPixels), BeilvFloatDuration)
                .SetEase(Ease.OutQuad);
        }

        private void RestoreBeilv()
        {
            if (_beilvRt == null)
            {
                return;
            }

            _beilvRt.DOKill();
            _beilvRt.anchoredPosition = _beilvHome;
            _beilvRt.gameObject.SetActive(false);
            _beilvRt = null;
        }

        private void HideSpawned()
        {
            for (var i = 0; i < _spawned.Count; i++)
            {
                var go = _spawned[i];
                if (go == null)
                {
                    continue;
                }

                go.transform.DOKill();
                UnityEngine.Object.Destroy(go);
            }

            _spawned.Clear();
        }

        private void Kill()
        {
            _playToken++;
            _seq?.Kill();
            _seq = null;
            HideSpawned();
            RestoreBeilv();
            IsPlaying = false;
        }

        private static void ApplySorting(GameObject go, int order)
        {
            if (go == null)
            {
                return;
            }

            var particles = go.GetComponentsInChildren<ParticleSystemRenderer>(true);
            for (var i = 0; i < particles.Length; i++)
            {
                particles[i].sortingOrder = order;
            }

            var trails = go.GetComponentsInChildren<TrailRenderer>(true);
            for (var i = 0; i < trails.Length; i++)
            {
                trails[i].sortingOrder = order;
            }
        }

        private static void RestartParticles(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (var i = 0; i < systems.Length; i++)
            {
                systems[i].Clear(true);
                systems[i].Play(true);
            }
        }

        private static void ClearTrails(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var trails = go.GetComponentsInChildren<TrailRenderer>(true);
            for (var i = 0; i < trails.Length; i++)
            {
                trails[i].Clear();
            }
        }
    }
}
