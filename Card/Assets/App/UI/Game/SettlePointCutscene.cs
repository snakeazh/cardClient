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
        private const float AttackLowDuration = 0.35f;
        private const float BonusStepDuration = 0.5f;
        private const float Fx03Duration = 0.55f;
        private const float BeilvFloatDuration = 0.1f;
        private const float BeilvFloatPixels = 20f;
        private const int FxSortingOrder = 220;

        public struct BonusBeat
        {
            public Animator EquipAnimator;
            public bool IsAttack;
            public string BeilvText;
            public string CardTypeText;
            public int AttackValue;
        }

        private IResourceService _resources;
        private RectTransform _uiRoot;
        private Canvas _uiCanvas;
        private GameObject _fx01Prefab;
        private GameObject _fx02Prefab;
        private GameObject _fx03Prefab;
        private Sequence _seq;
        private int _playToken;
        private readonly List<GameObject> _spawned = new List<GameObject>(8);
        private readonly List<BonusBeat> _beats = new List<BonusBeat>(8);
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
            TMP_Text cardTypeNum,
            IReadOnlyList<BonusBeat> bonusBeats,
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
            _beats.Clear();
            if (bonusBeats != null)
            {
                for (var i = 0; i < bonusBeats.Count; i++)
                {
                    _beats.Add(bonusBeats[i]);
                }
            }

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
            _seq.Pause();
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
            });
            _seq.AppendInterval(AttackLowDuration);
            for (var i = 0; i < _beats.Count; i++)
            {
                var index = i;
                _seq.AppendCallback(() =>
                {
                    if (token != _playToken)
                    {
                        return;
                    }

                    ApplyBonusBeat(_beats[index], attackItem, beilvNum, cardTypeNum, onAttackNumber);
                });
                _seq.AppendInterval(BonusStepDuration);
            }

            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                HideBeilv();
                if (attackItem != null)
                {
                    SetAttackNumber(attackItem, Mathf.Max(1, finalDamage), onAttackNumber);
                    attackItem.PlayAttackNumberShake(false, true);
                }

                SpawnFx03(attackItem);
            });
            _seq.AppendInterval(Fx03Duration);
            _seq.OnComplete(() =>
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

                PlayNumberShake(cardTypeNum, false, false);
                ResetBonusEquips();
                RestoreBeilv();
                IsPlaying = false;
                onDone?.Invoke();
            });
            _seq.Play();
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

            PlayNumberShake(text.GetComponent<Animator>(), low, high);
        }

        private void ApplyBonusBeat(
            BonusBeat beat,
            PlayerItem attackItem,
            TMP_Text beilvNum,
            TMP_Text cardTypeNum,
            Action<int> onAttackNumber)
        {
            PlayNumberShake(beat.EquipAnimator, true, false);
            ShowBeilv(beilvNum, beat.BeilvText);
            if (beat.IsAttack)
            {
                if (attackItem != null)
                {
                    SetAttackNumber(attackItem, Mathf.Max(0, beat.AttackValue), onAttackNumber);
                    attackItem.PlayAttackNumberShake(true, false);
                }

                return;
            }

            if (cardTypeNum != null && !string.IsNullOrEmpty(beat.CardTypeText))
            {
                cardTypeNum.text = beat.CardTypeText;
                PlayNumberShake(cardTypeNum, true, false);
            }
        }

        private void ResetBonusEquips()
        {
            for (var i = 0; i < _beats.Count; i++)
            {
                PlayNumberShake(_beats[i].EquipAnimator, false, false);
            }
        }

        private static void PlayNumberShake(Animator animator, bool low, bool high)
        {
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

                UiFx.ClearTrails(go);
                go.transform.DOMove(targetPos, Fx02MoveDuration).SetEase(Ease.InQuad);
            }
        }

        private void SpawnFx03(PlayerItem attackItem)
        {
            if (_fx03Prefab == null)
            {
                return;
            }

            var rect = attackItem != null ? attackItem.AttackValueRect : null;
            var pos = rect != null ? rect.position : Vector3.zero;
            var go = UnityEngine.Object.Instantiate(_fx03Prefab);
            go.transform.SetParent(null, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.SetActive(true);
            UiFx.ApplySorting(go, FxSortingOrder);
            UiFx.RestartParticles(go);
            UiFx.ClearTrails(go);
            _spawned.Add(go);
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
            UiFx.ApplySorting(go, FxSortingOrder);
            UiFx.RestartParticles(go);
            UiFx.ClearTrails(go);
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
            UiFx.ApplySorting(go, FxSortingOrder);
            UiFx.RestartParticles(go);
            UiFx.ClearTrails(go);
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
            PlayNumberShake(beilvNum, true, false);
            _beilvRt.DOAnchorPos(_beilvHome + new Vector2(0f, BeilvFloatPixels), BeilvFloatDuration)
                .SetEase(Ease.OutQuad);
        }

        private void HideBeilv()
        {
            if (_beilvRt == null)
            {
                return;
            }

            _beilvRt.DOKill();
            _beilvRt.anchoredPosition = _beilvHome;
            PlayNumberShake(_beilvRt.GetComponent<TMP_Text>(), false, false);
            _beilvRt.gameObject.SetActive(false);
        }

        private void RestoreBeilv()
        {
            HideBeilv();
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
    }
}
