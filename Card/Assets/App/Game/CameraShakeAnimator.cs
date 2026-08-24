using System.Collections;
using UnityEngine;

namespace App.Game
{
    public enum CameraShakeLevel
    {
        Low = 1,
        Middle = 2,
        High = 3
    }

    /// <summary>
    /// 驱动 Camera.controller 上的抖动动画。挂在与 <see cref="CameraAdaptatoon"/> 相同的 Camera 节点。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class CameraShakeAnimator : MonoBehaviour
    {
        public const string DefaultState = "CameraDefault";
        public const string ShakeLowState = "CameraShakeLow";
        public const string ShakeMiddleState = "CameraShakeMiddle";
        public const string ShakeHighState = "CameraShakeHigh";

        [SerializeField] private Animator animator;

        private Coroutine _resetRoutine;

        private void Awake()
        {
            EnsureAnimator();
        }

        public void PlayByLevel(int level)
        {
            level = Mathf.Clamp(level, 1, 3);
            Play((CameraShakeLevel)level);
        }

        public void Play(CameraShakeLevel level)
        {
            EnsureAnimator();
            if (animator == null)
            {
                return;
            }

            CancelReset();
            PlayState(StateName(level));
            _resetRoutine = StartCoroutine(ResetAfterCurrentClip());
        }

        public void Reset()
        {
            CancelReset();
            PlayState(DefaultState);
        }

        private static string StateName(CameraShakeLevel level)
        {
            switch (level)
            {
                case CameraShakeLevel.High:
                    return ShakeHighState;
                case CameraShakeLevel.Middle:
                    return ShakeMiddleState;
                default:
                    return ShakeLowState;
            }
        }

        private void PlayState(string stateName)
        {
            if (animator == null || string.IsNullOrEmpty(stateName))
            {
                return;
            }

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
            if (!animator.isInitialized)
            {
                animator.Rebind();
                animator.Update(0f);
            }

            animator.Play(stateName, 0, 0f);
            animator.Update(0f);
        }

        private IEnumerator ResetAfterCurrentClip()
        {
            yield return null;

            if (animator == null)
            {
                _resetRoutine = null;
                yield break;
            }

            var length = animator.GetCurrentAnimatorStateInfo(0).length;
            if (length > 0f)
            {
                yield return new WaitForSeconds(length);
            }

            PlayState(DefaultState);
            _resetRoutine = null;
        }

        private void CancelReset()
        {
            if (_resetRoutine == null)
            {
                return;
            }

            StopCoroutine(_resetRoutine);
            _resetRoutine = null;
        }

        private void EnsureAnimator()
        {
            if (animator != null)
            {
                return;
            }

            animator = GetComponent<Animator>();
        }
    }
}
