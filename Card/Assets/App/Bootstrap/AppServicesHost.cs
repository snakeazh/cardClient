using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Net;
using Framework.Log;
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
        private const float ProfileRefreshMinIntervalSeconds = 2f;

        private readonly List<ISaveFlushable> _flushables = new List<ISaveFlushable>();
        private float _lastProfileRefreshRealtime = -999f;
        private bool _refreshingProfile;

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
                    AppLog.Exception(LogChannel.Bootstrap, e);
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
            else
            {
                _ = RefreshProfileFromServerAsync();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                FlushAll();
                return;
            }

            _ = RefreshProfileFromServerAsync();
        }

        private async Task RefreshProfileFromServerAsync()
        {
            if (!GameApi.IsReady || _refreshingProfile)
            {
                return;
            }

            if (Time.realtimeSinceStartup - _lastProfileRefreshRealtime < ProfileRefreshMinIntervalSeconds)
            {
                return;
            }

            _refreshingProfile = true;
            try
            {
                var profile = await GameApi.Client.GetProfileAsync();
                GameApi.ApplyProfile(profile);
                _lastProfileRefreshRealtime = Time.realtimeSinceStartup;
            }
            catch (Exception e)
            {
                AppLog.Warn(LogChannel.Net, "refresh profile failed: " + e.Message);
            }
            finally
            {
                _refreshingProfile = false;
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
