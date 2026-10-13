using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Assets;
using Framework.UI.View;
using UnityEngine;

namespace Framework.UI.Navigation
{
    public interface IUINavigator
    {
        /// <summary>
        /// Opens a screen by id and injects the provided ViewModel into the View.
        /// </summary>
        Task Open(ScreenId screenId, ViewModelBase viewModel, object args = null);

        /// <summary>
        /// Opens the screen registered for the ViewModel's type and injects that instance.
        /// </summary>
        Task Open(ViewModelBase viewModel, object args = null);

        /// <summary>
        /// Closes the screen bound to the given ViewModel. If null, closes the top-most screen.
        /// </summary>
        Task Close(ViewModelBase viewModel = null);

        /// <summary>
        /// Closes the top screen on the specified layer.
        /// </summary>
        Task Close(UILayer layer);

        bool HasScreen(UILayer layer);

        /// <summary>
        /// 在全部层栈中查找已打开的指定类型 ViewModel（含被压住未销毁的页），未找到返回 null。
        /// </summary>
        T FindOpen<T>() where T : ViewModelBase;
    }

    public sealed class UINavigator : IUINavigator
    {
        private readonly UIRoot _root;
        private readonly ScreenRegistry _registry;
        private readonly IResourceService _resources;
        private readonly Dictionary<UILayer, Stack<ScreenInstance>> _stacks =
            new Dictionary<UILayer, Stack<ScreenInstance>>();
        private readonly IViewTransition _transition;

        public UINavigator(
            UIRoot root,
            ScreenRegistry registry,
            IResourceService resources,
            IViewTransition transition = null)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _transition = transition ?? PopupViewTransition.Instance;

            foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
            {
                _stacks[layer] = new Stack<ScreenInstance>();
            }
        }

        public Task Open(ViewModelBase viewModel, object args = null)
        {
            if (viewModel == null)
            {
                throw new ArgumentNullException(nameof(viewModel));
            }

            var registration = _registry.GetByViewModelType(viewModel.GetType());
            return OpenCore(registration, viewModel, args);
        }

        public Task Open(ScreenId screenId, ViewModelBase viewModel, object args = null)
        {
            if (viewModel == null)
            {
                throw new ArgumentNullException(nameof(viewModel));
            }

            var registration = _registry.Get(screenId);
            if (!registration.ViewModelType.IsInstanceOfType(viewModel))
            {
                throw new ArgumentException(
                    $"ViewModel type {viewModel.GetType().Name} does not match registered type {registration.ViewModelType.Name} for screen '{screenId}'.",
                    nameof(viewModel));
            }

            return OpenCore(registration, viewModel, args);
        }

        public async Task Close(ViewModelBase viewModel = null)
        {
            if (viewModel != null)
            {
                await CloseByViewModel(viewModel);
                return;
            }

            await Close(FindTopMostLayerWithScreens());
        }

        public bool HasScreen(UILayer layer)
        {
            return _stacks.TryGetValue(layer, out var stack) && stack.Count > 0;
        }

        public T FindOpen<T>() where T : ViewModelBase
        {
            foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
            {
                foreach (var instance in _stacks[layer])
                {
                    if (instance.ViewModel is T typed)
                    {
                        return typed;
                    }
                }
            }

            return null;
        }

        public async Task Close(UILayer layer)
        {
            var stack = _stacks[layer];
            if (stack.Count == 0)
            {
                return;
            }

            var closing = stack.Pop();
            await _transition.PlayExit(closing.View.gameObject);
            await DestroyInstance(closing);

            if (stack.Count > 0)
            {
                var revealed = stack.Peek();
                revealed.View.gameObject.SetActive(true);
                await _transition.PlayEnter(revealed.View.gameObject);
            }
        }

        private async Task OpenCore(ScreenRegistration registration, ViewModelBase viewModel, object args)
        {
            var stack = _stacks[registration.Layer];
            // 先把新界面加载完，再藏上一页，避免 AB/await 期间露底闪一下。
            var covered = stack.Count > 0 ? stack.Peek() : null;
            var coverGo = covered != null && covered.View != null ? covered.View.gameObject : null;

            var instance = await CreateInstanceAsync(registration, viewModel, args, coverGo);

            if (covered != null && coverGo != null)
            {
                coverGo.transform.SetAsLastSibling();
                await covered.View.Hide();
                await _transition.PlayExit(coverGo);
                // 先关掉遮罩页再改局内 Camera/Fit，避免选关界面被撑宽一帧。
                coverGo.SetActive(false);
            }

            stack.Push(instance);
            await _transition.PlayEnter(instance.View.gameObject);
            instance.View.OnPresented();
        }

        private async Task CloseByViewModel(ViewModelBase viewModel)
        {
            foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
            {
                var stack = _stacks[layer];
                if (stack.Count == 0)
                {
                    continue;
                }

                // Only the top instance on a layer is interactive; close if it matches.
                if (ReferenceEquals(stack.Peek().ViewModel, viewModel))
                {
                    await Close(layer);
                    return;
                }

                // 压在下面的页（例如开局先 Open GameUI 再关 LevelUI）直接拆掉，不要 Reveal。
                if (TryRemoveBuried(stack, viewModel, out var buried))
                {
                    await DestroyInstance(buried);
                    return;
                }
            }

            throw new InvalidOperationException(
                $"ViewModel '{viewModel.GetType().Name}' is not open on any layer.");
        }

        private static bool TryRemoveBuried(
            Stack<ScreenInstance> stack,
            ViewModelBase viewModel,
            out ScreenInstance buried)
        {
            buried = null;
            if (stack == null || stack.Count == 0)
            {
                return false;
            }

            var kept = new List<ScreenInstance>(stack.Count);
            while (stack.Count > 0)
            {
                var top = stack.Pop();
                if (buried == null && ReferenceEquals(top.ViewModel, viewModel))
                {
                    buried = top;
                    continue;
                }

                kept.Add(top);
            }

            for (var i = kept.Count - 1; i >= 0; i--)
            {
                stack.Push(kept[i]);
            }

            return buried != null;
        }

        private async Task<ScreenInstance> CreateInstanceAsync(
            ScreenRegistration registration,
            ViewModelBase viewModel,
            object args,
            GameObject keepCoverOnTop = null)
        {
            var prefab = registration.Prefab;
            if (prefab == null)
            {
                prefab = await _resources.LoadAsync<GameObject>(registration.AssetKey);
                if (keepCoverOnTop != null)
                {
                    keepCoverOnTop.transform.SetAsLastSibling();
                }
            }

            var parent = _root.GetLayer(registration.Layer);
            var go = UnityEngine.Object.Instantiate(prefab, parent, false);
            go.name = prefab.name;
            go.SetActive(true);
            // 新页一出来就可能盖住旧页；加载期间始终把遮罩页按在最上。
            if (keepCoverOnTop != null)
            {
                keepCoverOnTop.transform.SetAsLastSibling();
            }
            
            var view = go.GetComponent<IView>();
            if (view == null)
            {
                UnityEngine.Object.Destroy(go);
                throw new InvalidOperationException(
                    $"Prefab for screen '{registration.Id}' must have a component implementing IView.");
            }

            await view.Open(viewModel, args);
            if (keepCoverOnTop != null)
            {
                keepCoverOnTop.transform.SetAsLastSibling();
            }

            return new ScreenInstance(registration, view, viewModel);
        }

        private static async Task DestroyInstance(ScreenInstance instance)
        {
            await instance.View.Close();
            if (instance.View.gameObject != null)
            {
                UnityEngine.Object.Destroy(instance.View.gameObject);
            }
        }

        private UILayer FindTopMostLayerWithScreens()
        {
            if (_stacks[UILayer.Guide].Count > 0)
            {
                return UILayer.Guide;
            }

            if (_stacks[UILayer.TopMost].Count > 0)
            {
                return UILayer.TopMost;
            }

            // Toast 层常驻，不作为 Close() 的目标。

            if (_stacks[UILayer.Loading].Count > 0)
            {
                return UILayer.Loading;
            }

            if (_stacks[UILayer.Resource].Count > 0)
            {
                return UILayer.Resource;
            }

            if (_stacks[UILayer.Popup].Count > 0)
            {
                return UILayer.Popup;
            }

            if (_stacks[UILayer.Navigation].Count > 0)
            {
                return UILayer.Navigation;
            }

            if (_stacks[UILayer.Page].Count > 0)
            {
                return UILayer.Page;
            }

            return UILayer.Hud;
        }

        private sealed class ScreenInstance
        {
            public ScreenInstance(ScreenRegistration registration, IView view, ViewModelBase viewModel)
            {
                Registration = registration;
                View = view;
                ViewModel = viewModel;
            }

            public ScreenRegistration Registration { get; }
            public IView View { get; }
            public ViewModelBase ViewModel { get; }
        }
    }
}
