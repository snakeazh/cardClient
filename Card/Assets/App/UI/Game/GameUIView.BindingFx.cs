using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Resources;
using Framework.Log;
using UnityEngine;

namespace App.UI
{
    public sealed partial class GameUIView
    {
        private static readonly string[] BindingFxNames =
        {
            "JibanXingYunDian01",
            "JibanLeitingzhanji01",
            "SaManJiSiHeal01",
            "JibanXueZhiYiShi01",
            "JibanXueZhiYiShi02"
        };

        private readonly GameObject[] _bindingFxPrefabs = new GameObject[5];
        private readonly List<GameObject> _bindingFxInstances = new List<GameObject>();
        private readonly List<Coroutine> _bindingFxFlights = new List<Coroutine>();

        private readonly int[] _pendingThunderDamage = new int[3];
        private bool _thunderBindingPlaying;
        private Coroutine _thunderBindingRoutine;

        private bool HasPendingThunder(int slot)
        {
            return slot >= 0 && slot < _pendingThunderDamage.Length && _pendingThunderDamage[slot] > 0;
        }

        private bool TryPlayThunderBinding()
        {
            if (_thunderBindingPlaying)
            {
                return true;
            }

            if (!Array.Exists(_pendingThunderDamage, damage => damage > 0))
            {
                return false;
            }

            _thunderBindingPlaying = true;
            _thunderBindingRoutine = StartCoroutine(PlayThunderBinding());
            return true;
        }

        private IEnumerator PlayThunderBinding()
        {
            ViewModel.ShowMask.Value = true;
            HideCritHp();
            ViewModel.ShowHpText.Value = false;
            RestoreSplashHpClones();
            // 普攻演完后留一个间隔，再独立播放雷霆和伤害字。
            var tuning = AttackTuningConfig.Instance;
            yield return new WaitForSeconds(tuning.ThunderStartDelay);
            for (var slot = 0; slot < _pendingThunderDamage.Length; slot++)
            {
                if (HasPendingThunder(slot))
                {
                    SpawnBindingFx(1, _attackFx.HitPosition(slot), tuning.ThunderHitDelay + tuning.ThunderDamageHoldDuration);
                }
            }

            yield return new WaitForSeconds(tuning.ThunderHitDelay);
            for (var slot = 0; slot < _pendingThunderDamage.Length; slot++)
            {
                if (HasPendingThunder(slot))
                {
                    PlaceSplashHpAt(slot, $"雷霆 -{_pendingThunderDamage[slot]}", playAnim: true);
                }
            }

            yield return new WaitForSeconds(tuning.ThunderDamageHoldDuration);
            Array.Clear(_pendingThunderDamage, 0, _pendingThunderDamage.Length);
            _thunderBindingRoutine = null;
            RestoreSplashHpClones();
            TryDissolveIfLethal(ViewModel.Session);
            PlayPendingEnemyDeathEffects();
            ViewModel.ShowMask.Value = false;
            _thunderBindingPlaying = false;
            FinishAttackIfReady();
        }

        private async Task PreloadBindingFxAsync()
        {
            for (var i = 0; i < BindingFxNames.Length; i++)
            {
                try
                {
                    _bindingFxPrefabs[i] = await ViewModel.Resources.LoadAsync<GameObject>(
                        ResResourcePaths.HeroEffect(BindingFxNames[i]));
                }
                catch (Exception ex)
                {
                    AppLog.Warn(LogChannel.UI, "Binding FX preload failed: " + BindingFxNames[i] + " " + ex.Message);
                }
            }
        }

        private void OnThunderBindingTriggered(int slot, int damage)
        {
            if (slot < 0 || slot >= _pendingThunderDamage.Length || damage <= 0)
            {
                return;
            }

            _pendingThunderDamage[slot] += damage;
            if (!ViewModel.Session.AttackPlaying)
            {
                TryPlayThunderBinding();
            }
        }

        private void OnBloodCeremonyHealing()
        {
            if (_playerItem != null)
            {
                SpawnBindingFx(2, _playerItem.transform.position);
            }
        }

        private void OnBloodCeremonyHit(int slot)
        {
            var target = AttackItemAtSlot(slot);
            if (_playerItem == null || target == null)
            {
                return;
            }

            var destination = target.transform.position;
            var missile = SpawnBindingFx(3, _playerItem.transform.position, 0f);
            if (missile == null)
            {
                SpawnBindingFx(4, destination);
                return;
            }

            _bindingFxFlights.Add(StartCoroutine(FlyBloodCeremonyFx(missile, destination)));
        }

        private IEnumerator FlyBloodCeremonyFx(GameObject missile, Vector3 destination)
        {
            var start = missile.transform.position;
            var direction = destination - start;
            var tuning = AttackTuningConfig.Instance;
            // 二次贝塞尔向目标外侧拱起，正上方目标默认向右弯。
            var side = direction.x < -0.001f ? -1f : 1f;
            var outward = new Vector3(direction.y, -direction.x, 0f).normalized * side;
            var control = (start + destination) * 0.5f
                          + outward * direction.magnitude * tuning.BloodCeremonyArcRatio;
            var duration = tuning.BloodCeremonyFlightDuration;
            var elapsed = 0f;
            while (missile != null && elapsed < duration)
            {
                var progress = Mathf.Clamp01(elapsed / duration);
                var remaining = 1f - progress;
                missile.transform.position = remaining * remaining * start
                                             + 2f * remaining * progress * control
                                             + progress * progress * destination;
                var tangent = 2f * (remaining * (control - start)
                                    + progress * (destination - control));
                if (tangent.sqrMagnitude > 0.001f)
                {
                    missile.transform.rotation = Quaternion.Euler(
                        0f, 0f, Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg);
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            if (missile != null)
            {
                Destroy(missile);
                SpawnBindingFx(4, destination);
            }
        }

        private GameObject SpawnBindingFx(int index, Vector3 position, float lifetime = 2f)
        {
            var prefab = _bindingFxPrefabs[index];
            if (prefab == null)
            {
                return null;
            }

            _bindingFxInstances.RemoveAll(instance => instance == null);
            var instance = Instantiate(prefab, transform, false);
            instance.transform.position = position;
            instance.transform.rotation = Quaternion.identity;
            // Match the existing hero effects' world-to-UI camera scale.
            var canvas = GetComponentInParent<UnityEngine.Canvas>();
            var uiCamera = canvas != null ? canvas.rootCanvas.worldCamera : null;
            var worldCamera = Camera.main;
            var scale = uiCamera != null && worldCamera != null &&
                        uiCamera.orthographic && worldCamera.orthographic && worldCamera.orthographicSize > 0f
                ? uiCamera.orthographicSize / worldCamera.orthographicSize
                : 1f;
            // Parent scale must not multiply a world-sized particle effect.
            var parentScale = transform.lossyScale;
            instance.transform.localScale = new Vector3(
                scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
            UiFx.ApplyUiLayer(instance);
            UiFx.ApplySorting(instance, 240);
            instance.SetActive(true);
            UiFx.ClearTrails(instance);
            UiFx.RestartParticles(instance);
            _bindingFxInstances.Add(instance);
            if (lifetime > 0f)
            {
                Destroy(instance, lifetime);
            }
            return instance;
        }

        private void ClearBindingFx()
        {
            if (_thunderBindingRoutine != null)
            {
                StopCoroutine(_thunderBindingRoutine);
                _thunderBindingRoutine = null;
            }
            _thunderBindingPlaying = false;
            Array.Clear(_pendingThunderDamage, 0, _pendingThunderDamage.Length);
            foreach (var flight in _bindingFxFlights)
            {
                if (flight != null)
                {
                    StopCoroutine(flight);
                }
            }
            _bindingFxFlights.Clear();
            foreach (var instance in _bindingFxInstances)
            {
                if (instance != null)
                {
                    Destroy(instance);
                }
            }
            _bindingFxInstances.Clear();
            for (var i = 0; i < BindingFxNames.Length; i++)
            {
                ViewModel?.Resources?.Release(ResResourcePaths.HeroEffect(BindingFxNames[i]));
                _bindingFxPrefabs[i] = null;
            }
        }
    }
}
