using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Framework.UI.DI
{
    public enum ServiceLifetime
    {
        Singleton,
        Transient
    }

    public sealed class ServiceContainer : IServiceProvider
    {
        private readonly Dictionary<Type, ServiceRegistration> _registrations =
            new Dictionary<Type, ServiceRegistration>();

        private readonly Dictionary<Type, object> _singletons = new Dictionary<Type, object>();
        private readonly object _gate = new object();

        public ServiceContainer RegisterInstance<TService>(TService instance)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            lock (_gate)
            {
                _registrations[typeof(TService)] = new ServiceRegistration(
                    typeof(TService),
                    ServiceLifetime.Singleton,
                    _ => instance);
                _singletons[typeof(TService)] = instance;
            }

            return this;
        }

        public ServiceContainer AddSingleton<TService>()
        {
            return AddSingleton(typeof(TService), typeof(TService));
        }

        public ServiceContainer AddSingleton<TService, TImplementation>() where TImplementation : TService
        {
            return AddSingleton(typeof(TService), typeof(TImplementation));
        }

        public ServiceContainer AddSingleton(Type serviceType, Type implementationType)
        {
            return Register(serviceType, implementationType, ServiceLifetime.Singleton);
        }

        public ServiceContainer AddSingleton<TService>(Func<ServiceContainer, TService> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            lock (_gate)
            {
                _registrations[typeof(TService)] = new ServiceRegistration(
                    typeof(TService),
                    ServiceLifetime.Singleton,
                    c => factory(c));
            }

            return this;
        }

        public ServiceContainer AddTransient<TService>()
        {
            return AddTransient(typeof(TService), typeof(TService));
        }

        public ServiceContainer AddTransient<TService, TImplementation>() where TImplementation : TService
        {
            return AddTransient(typeof(TService), typeof(TImplementation));
        }

        public ServiceContainer AddTransient(Type serviceType, Type implementationType)
        {
            return Register(serviceType, implementationType, ServiceLifetime.Transient);
        }

        public ServiceContainer AddTransient<TService>(Func<ServiceContainer, TService> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            lock (_gate)
            {
                _registrations[typeof(TService)] = new ServiceRegistration(
                    typeof(TService),
                    ServiceLifetime.Transient,
                    c => factory(c));
            }

            return this;
        }

        public bool IsRegistered(Type serviceType)
        {
            lock (_gate)
            {
                return _registrations.ContainsKey(serviceType);
            }
        }

        public T Resolve<T>() => (T)Resolve(typeof(T));

        public object Resolve(Type serviceType)
        {
            lock (_gate)
            {
                return ResolveLocked(serviceType);
            }
        }

        public object GetService(Type serviceType)
        {
            lock (_gate)
            {
                if (!_registrations.ContainsKey(serviceType))
                {
                    return null;
                }

                return ResolveLocked(serviceType);
            }
        }

        private ServiceContainer Register(Type serviceType, Type implementationType, ServiceLifetime lifetime)
        {
            if (serviceType == null)
            {
                throw new ArgumentNullException(nameof(serviceType));
            }

            if (implementationType == null)
            {
                throw new ArgumentNullException(nameof(implementationType));
            }

            if (!serviceType.IsAssignableFrom(implementationType))
            {
                throw new ArgumentException(
                    $"{implementationType.FullName} does not implement {serviceType.FullName}");
            }

            lock (_gate)
            {
                _registrations[serviceType] = new ServiceRegistration(
                    implementationType,
                    lifetime,
                    c => c.CreateInstance(implementationType));
            }

            return this;
        }

        private object ResolveLocked(Type serviceType)
        {
            if (!_registrations.TryGetValue(serviceType, out var registration))
            {
                throw new InvalidOperationException($"Service not registered: {serviceType.FullName}");
            }

            if (registration.Lifetime == ServiceLifetime.Singleton)
            {
                if (_singletons.TryGetValue(serviceType, out var existing))
                {
                    return existing;
                }

                var singleton = registration.Factory(this);
                _singletons[serviceType] = singleton;
                return singleton;
            }

            return registration.Factory(this);
        }

        private object CreateInstance(Type type)
        {
            var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
            if (constructors.Length == 0)
            {
                throw new InvalidOperationException($"No public constructor found for {type.FullName}");
            }

            var constructor = constructors
                .OrderByDescending(c => c.GetParameters().Length)
                .First();

            var parameters = constructor.GetParameters();
            var args = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                args[i] = ResolveLocked(parameters[i].ParameterType);
            }

            return constructor.Invoke(args);
        }

        private sealed class ServiceRegistration
        {
            public ServiceRegistration(Type implementationType, ServiceLifetime lifetime, Func<ServiceContainer, object> factory)
            {
                ImplementationType = implementationType;
                Lifetime = lifetime;
                Factory = factory;
            }

            public Type ImplementationType { get; }
            public ServiceLifetime Lifetime { get; }
            public Func<ServiceContainer, object> Factory { get; }
        }
    }
}
