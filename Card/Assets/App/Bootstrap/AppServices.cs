using System;
using Framework.UI.DI;
using UnityEngine;

namespace App.Bootstrap
{
    /// <summary>
    /// Global access point to the persistent service container.
    /// Any scene can resolve services without holding a reference to AppBootstrap.
    /// </summary>
    public static class AppServices
    {
        private static AppServicesHost _host;

        internal static AppServicesHost HostOrNull => _host;

        public static bool IsReady => _host != null;

        public static AppServicesHost Host
        {
            get
            {
                if (_host == null)
                {
                    throw new InvalidOperationException(
                        "AppServices is not created yet. Call AppServices.Create() from AppBootstrap first.");
                }

                return _host;
            }
        }

        public static ServiceContainer Container => Host.Container;

        public static AppServicesHost Create()
        {
            if (_host != null)
            {
                return _host;
            }

            var go = new GameObject(nameof(AppServicesHost));
            UnityEngine.Object.DontDestroyOnLoad(go);
            _host = go.AddComponent<AppServicesHost>();
            _host.Initialize();
            return _host;
        }

        public static T Resolve<T>() => Host.Resolve<T>();

        internal static void Detach()
        {
            _host = null;
        }
    }
}
