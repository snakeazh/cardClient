using System;
using System.Collections.Generic;
using System.Reflection;
using Framework.UI.DI;
using Framework.UI.View;
using UnityEngine;

namespace Framework.UI.Navigation
{
    internal static class ScreenAutoRegistrar
    {
        private static bool _didRegister;

        public static void AutoRegisterScreens(
            ServiceContainer container,
            ScreenRegistry registry)
        {
            if (_didRegister)
            {
                return;
            }

            _didRegister = true;

            var viewTypes = GetTypesWithAttribute<AutoScreenAttribute>();
            foreach (var viewType in viewTypes)
            {
                RegisterOne(container, registry, viewType);
            }
        }

        private static void RegisterOne(
            ServiceContainer container,
            ScreenRegistry registry,
            Type viewType)
        {
            var attr = viewType.GetCustomAttribute<AutoScreenAttribute>(inherit: false);
            if (attr == null)
            {
                return;
            }

            var viewModelType = ResolveViewModelType(viewType);
            if (viewModelType == null)
            {
                throw new InvalidOperationException(
                    $"View '{viewType.FullName}' must inherit from ViewBase<TVm> to infer its ViewModel type.");
            }

            var screenId = new ScreenId(attr.ScreenId);

            EnsureViewModelRegistered(container, viewModelType);

            registry.Register(
                new ScreenRegistration(
                    screenId,
                    attr.Layer,
                    attr.AssetKey,
                    viewModelType,
                    viewModelFactory: null));
        }

        private static void EnsureViewModelRegistered(ServiceContainer container, Type viewModelType)
        {
            if (container.IsRegistered(viewModelType))
            {
                return;
            }

            container.AddTransient(viewModelType, viewModelType);
        }


        private static Type ResolveViewModelType(Type viewType)
        {
            // Find ViewBase<TVm> in inheritance chain.
            var current = viewType;
            while (current != null && current != typeof(object))
            {
                if (current.IsGenericType &&
                    current.GetGenericTypeDefinition() == typeof(ViewBase<>))
                {
                    var args = current.GetGenericArguments();
                    return args.Length == 1 ? args[0] : null;
                }

                current = current.BaseType;
            }

            return null;
        }

        private static IEnumerable<Type> GetTypesWithAttribute<TAttr>() where TAttr : Attribute
        {
            var result = new List<Type>();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }

                if (types == null)
                {
                    continue;
                }

                foreach (var t in types)
                {
                    if (t == null)
                    {
                        continue;
                    }

                    if (!t.IsClass || t.IsAbstract)
                    {
                        continue;
                    }

                    if (t.GetCustomAttribute<TAttr>(inherit: false) != null)
                    {
                        result.Add(t);
                    }
                }
            }

            return result;
        }
    }
}

