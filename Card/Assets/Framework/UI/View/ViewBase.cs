using System;
using System.Threading.Tasks;
using DG.Tweening;
using Framework.UI.Binding;
using UnityEngine;

namespace Framework.UI.View
{
    public interface IView
    {
        GameObject gameObject { get; }
        Transform transform { get; }
        ViewModelBase ViewModelObject { get; }
        Task Open(ViewModelBase viewModel, object args);
        Task Hide();
        Task Close();
        Task PlayPopupEnter();
        Task PlayPopupExit();

        /// <summary>
        /// 上一页遮罩已撤掉、本页真正露出来后调用。用于延后改 Camera/Fit，避免撑变形盖着的局外 UI。
        /// </summary>
        void OnPresented();
    }

    public abstract class ViewBase<TVm> : MonoBehaviour, IView where TVm : ViewModelBase
    {
        private const float PopupEnterDuration = 0.25f;
        private const float PopupExitDuration = 0.18f;

        [SerializeField] private UIReference _ui;

        [SerializeField]
        [Tooltip("弹窗动画节点。手动拖入面板 Transform 后，打开/关闭时播放缩放动画；留空则无动画。")]
        private Transform _popupAnim;

        private BindingContext _binding;
        private bool _opened;
        private Vector3 _popupAnimRestScale = Vector3.one;
        private bool _popupAnimRestCached;
        private Tween _popupAnimTween;

        public TVm ViewModel { get; private set; }
        public ViewModelBase ViewModelObject => ViewModel;
        protected BindingContext Binding => _binding;

        protected UIReference UI
        {
            get
            {
                if (_ui == null)
                {
                    _ui = GetComponent<UIReference>();
                }

                return _ui;
            }
        }

        public async Task Open(ViewModelBase viewModel, object args)
        {
            if (_opened)
            {
                await Close();
            }

            if (UI == null)
            {
                throw new MissingComponentException(
                    $"{GetType().Name} requires a UIReference on the same GameObject.");
            }

            ViewModel = (TVm)viewModel;
            _binding = new BindingContext();
            _opened = true;

            gameObject.SetActive(true);
            PreparePopupEnter();

            // OnViewOpen 失败时仍要 OnBind，否则会停在预制体占位文案（如 9999/-9999）。
            Exception viewOpenError = null;
            var viewName = GetType().Name;
            Debug.LogWarning($"[BattleTrace] ViewBase.Open begin {viewName}");
            try
            {
                await OnViewOpen();
                Debug.LogWarning($"[BattleTrace] ViewBase.OnViewOpen done {viewName}");
            }
            catch (Exception ex)
            {
                viewOpenError = ex;
                Debug.LogWarning($"[BattleTrace] ViewBase.OnViewOpen EX {viewName}: {ex.Message}");
                Debug.LogException(ex);
            }

            Debug.LogWarning($"[BattleTrace] ViewBase.OnBind begin {viewName}");
            OnBind();
            Debug.LogWarning($"[BattleTrace] ViewBase.OnBind done {viewName}, ViewModel.Open…");
            await ViewModel.Open(args);
            Debug.LogWarning($"[BattleTrace] ViewBase.Open complete {viewName}");

            if (viewOpenError != null)
            {
                throw viewOpenError;
            }
        }

        public async Task Hide()
        {
            if (!_opened)
            {
                return;
            }

            if (ViewModel != null)
            {
                await ViewModel.Hide();
            }

            await OnViewHide();
        }

        public async Task Close()
        {
            if (!_opened)
            {
                return;
            }

            _opened = false;
            Unbind();
            await OnViewClose();

            if (ViewModel != null)
            {
                await ViewModel.Close();
                ViewModel = null;
            }
        }

        protected abstract void OnBind();

        protected virtual Task OnViewOpen() => Task.CompletedTask;

        protected virtual Task OnViewHide() => Task.CompletedTask;

        protected virtual Task OnViewClose() => Task.CompletedTask;

        /// <inheritdoc cref="IView.OnPresented"/>
        public virtual void OnPresented()
        {
        }

        Task IView.PlayPopupEnter()
        {
            if (_popupAnim == null)
            {
                return Task.CompletedTask;
            }

            EnsurePopupAnimRest();
            KillPopupAnim(snapToRest: false);
            _popupAnim.localScale = Vector3.zero;
            _popupAnimTween = _popupAnim
                .DOScale(_popupAnimRestScale, PopupEnterDuration)
                .SetEase(Ease.OutBack)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            return WaitTween(_popupAnimTween);
        }

        Task IView.PlayPopupExit()
        {
            if (_popupAnim == null)
            {
                return Task.CompletedTask;
            }

            EnsurePopupAnimRest();
            KillPopupAnim(snapToRest: false);
            _popupAnimTween = _popupAnim
                .DOScale(Vector3.zero, PopupExitDuration)
                .SetEase(Ease.InBack)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            return WaitTween(_popupAnimTween);
        }

        private void PreparePopupEnter()
        {
            if (_popupAnim == null)
            {
                return;
            }

            EnsurePopupAnimRest();
            KillPopupAnim(snapToRest: false);
            _popupAnim.localScale = Vector3.zero;
        }

        private void EnsurePopupAnimRest()
        {
            if (_popupAnimRestCached || _popupAnim == null)
            {
                return;
            }

            _popupAnimRestScale = _popupAnim.localScale;
            if (_popupAnimRestScale.sqrMagnitude < 0.0001f)
            {
                _popupAnimRestScale = Vector3.one;
            }

            _popupAnimRestCached = true;
        }

        private void KillPopupAnim(bool snapToRest)
        {
            if (_popupAnimTween != null && _popupAnimTween.IsActive())
            {
                _popupAnimTween.Kill();
            }

            _popupAnimTween = null;
            if (snapToRest && _popupAnimRestCached && _popupAnim != null)
            {
                _popupAnim.localScale = _popupAnimRestScale;
            }
        }

        private static async Task WaitTween(Tween tween)
        {
            if (tween == null || !tween.IsActive())
            {
                return;
            }

            await tween.AsyncWaitForCompletion();
        }

        private void Unbind()
        {
            _binding?.Dispose();
            _binding = null;
        }

        protected virtual async void OnDestroy()
        {
            KillPopupAnim(snapToRest: false);
            if (_opened)
            {
                await Close();
            }
        }
    }
}
