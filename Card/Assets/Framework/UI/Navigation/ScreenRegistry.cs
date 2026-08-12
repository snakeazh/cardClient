using System;
using System.Collections.Generic;
using Framework.UI.DI;
using Framework.UI.View;
using UnityEngine;

namespace Framework.UI.Navigation
{
    public sealed class ScreenRegistry
    {
        private readonly Dictionary<string, ScreenRegistration> _screens =
            new Dictionary<string, ScreenRegistration>(StringComparer.Ordinal);

        private readonly Dictionary<Type, ScreenRegistration> _screensByViewModel =
            new Dictionary<Type, ScreenRegistration>();

        private ServiceContainer _container;

        public void BindContainer(ServiceContainer container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public ScreenRegistry Register(ScreenRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            _screens[registration.Id.Value] = registration;
            _screensByViewModel[registration.ViewModelType] = registration;
            return this;
        }

        public ScreenRegistry Register<TView, TVm>(
            ScreenId id,
            UILayer layer,
            GameObject prefab,
            Func<TVm> viewModelFactory = null)
            where TView : ViewBase<TVm>
            where TVm : ViewModelBase
        {
            return Register(new ScreenRegistration(id, layer, prefab, typeof(TVm), ToFactory(viewModelFactory)));
        }

        public ScreenRegistry Register<TView, TVm>(
            ScreenId id,
            UILayer layer,
            string assetKey,
            Func<TVm> viewModelFactory = null)
            where TView : ViewBase<TVm>
            where TVm : ViewModelBase
        {
            return Register(new ScreenRegistration(id, layer, assetKey, typeof(TVm), ToFactory(viewModelFactory)));
        }

        public bool TryGet(ScreenId id, out ScreenRegistration registration)
        {
            return _screens.TryGetValue(id.Value, out registration);
        }

        public ScreenRegistration Get(ScreenId id)
        {
            if (!TryGet(id, out var registration))
            {
                throw new InvalidOperationException($"Screen not registered: {id}");
            }

            return registration;
        }

        public ScreenRegistration GetByViewModelType(Type viewModelType)
        {
            if (viewModelType == null)
            {
                throw new ArgumentNullException(nameof(viewModelType));
            }

            if (!_screensByViewModel.TryGetValue(viewModelType, out var registration))
            {
                throw new InvalidOperationException(
                    $"No screen registered for ViewModel type: {viewModelType.FullName}");
            }

            return registration;
        }

        public ViewModelBase CreateViewModel(ScreenRegistration registration)
        {
            if (registration.ViewModelFactory != null)
            {
                return (ViewModelBase)registration.ViewModelFactory();
            }

            if (_container == null)
            {
                throw new InvalidOperationException("ServiceContainer is not bound to ScreenRegistry.");
            }

            return (ViewModelBase)_container.Resolve(registration.ViewModelType);
        }

        private static Func<object> ToFactory<TVm>(Func<TVm> viewModelFactory)
            where TVm : ViewModelBase
        {
            if (viewModelFactory == null)
            {
                return null;
            }

            return () => viewModelFactory();
        }
    }
}
