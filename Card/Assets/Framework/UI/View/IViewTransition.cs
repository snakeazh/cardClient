using System.Threading.Tasks;
using UnityEngine;

namespace Framework.UI.View
{
    public interface IViewTransition
    {
        Task PlayEnter(GameObject target);
        Task PlayExit(GameObject target);
    }

    public sealed class InstantViewTransition : IViewTransition
    {
        public static readonly InstantViewTransition Instance = new InstantViewTransition();

        public Task PlayEnter(GameObject target)
        {
            if (target != null)
            {
                target.SetActive(true);
            }

            return Task.CompletedTask;
        }

        public Task PlayExit(GameObject target)
        {
            if (target != null)
            {
                target.SetActive(false);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// View 上手动挂了弹窗节点则播放缩放动画，否则与 <see cref="InstantViewTransition"/> 相同。
    /// </summary>
    public sealed class PopupViewTransition : IViewTransition
    {
        public static readonly PopupViewTransition Instance = new PopupViewTransition();

        public async Task PlayEnter(GameObject target)
        {
            if (target != null)
            {
                target.SetActive(true);
            }

            var view = target != null ? target.GetComponent<IView>() : null;
            if (view != null)
            {
                await view.PlayPopupEnter();
            }
        }

        public async Task PlayExit(GameObject target)
        {
            var view = target != null ? target.GetComponent<IView>() : null;
            if (view != null)
            {
                await view.PlayPopupExit();
            }

            if (target != null)
            {
                target.SetActive(false);
            }
        }
    }
}
