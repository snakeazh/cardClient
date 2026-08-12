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
}
