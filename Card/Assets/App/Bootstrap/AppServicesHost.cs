using System;
using System.Collections.Generic;
using Framework.Save;
using Framework.UI.DI;
using UnityEngine;

namespace App.Bootstrap
{
    /// <summary>
    /// Persistent (DontDestroyOnLoad) owner of the app <see cref="ServiceContainer"/>.
    /// Services registered here survive scene loads and are flushed on pause / quit.
    /// </summary>
    public sealed class AppServicesHost : MonoBehaviour
    {
        private readonly List<ISaveFlushable> _flushables = new List<ISaveFlushable>();

        public ServiceContainer Container { get; private set; }

        internal void Initialize()
        {
            if (Container != null)
            {
                return;
            }

            Container = new ServiceContainer();
            Container.RegisterInstance(Container);
        }

        /// <summary>
        /// Registers a singleton instance and tracks it for auto-flush when it can persist state.
        /// </summary>
        public AppServicesHost Register<TService>(TService instance)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            Container.RegisterInstance(instance);
            Track(instance);
            return this;
        }

        public T Resolve<T>() => Container.Resolve<T>();

        /// <summary>
        /// Flushes every tracked service that has pending changes.
        /// </summary>
        public void FlushAll()
        {
            for (var i = 0; i < _flushables.Count; i++)
            {
                try
                {
                    _flushables[i].Save();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private void Track(object instance)
        {
            if (instance is ISaveFlushable flushable && !_flushables.Contains(flushable))
            {
                _flushables.Add(flushable);
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                FlushAll();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                FlushAll();
            }
        }

        private void OnApplicationQuit()
        {
            FlushAll();
        }

        private void OnDestroy()
        {
            FlushAll();
            if (AppServices.HostOrNull == this)
            {
                AppServices.Detach();
            }
        }
    }
}
