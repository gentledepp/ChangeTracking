﻿using Castle.DynamicProxy;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace ChangeTracking
{
    public static class Core
    {
        private static ProxyGenerator _ProxyGenerator;
        private static IInterceptorSelector _Selector;
        private static ConcurrentDictionary<Type, ProxyGenerationOptions> _Options;

        static Core()
        {
            _ProxyGenerator = new ProxyGenerator();
            _Selector = new ChangeTrackingInterceptorSelector();
            _Options = new ConcurrentDictionary<Type, ProxyGenerationOptions>();
        }

        private static ProxyGenerationOptions CreateOptions(Type type) =>  new ProxyGenerationOptions
        {
            Hook = new ChangeTrackingProxyGenerationHook(type),
            Selector = _Selector
        };

        private static ProxyGenerationOptions GetOptions(Type type) => _Options.GetOrAdd(type, CreateOptions);

        internal static object AsTrackableCollectionChild(Type type, object target, bool makeComplexPropertiesTrackable, bool makeCollectionPropertiesTrackable)
        {
            Type genericArgument = type.GetGenericArguments().First();
            return _ProxyGenerator.CreateInterfaceProxyWithTarget(typeof(IList<>).MakeGenericType(genericArgument),
                        new[] { typeof(IChangeTrackableCollection<>).MakeGenericType(genericArgument), typeof(IBindingList), typeof(INotifyCollectionChanged) },
                        target,
                        GetOptions(type),
                        (IInterceptor)CreateInstance(typeof(ChangeTrackingCollectionInterceptor<>).MakeGenericType(genericArgument), target, makeComplexPropertiesTrackable, makeCollectionPropertiesTrackable));
        }

        internal static object AsTrackableChild(Type type, object target, Action<object> notifyParentItemCanceled, bool makeComplexPropertiesTrackable, bool makeCollectionPropertiesTrackable)
        {
            var changeTrackingInterceptor = CreateInstance(typeof(ChangeTrackingInterceptor<>).MakeGenericType(type), ChangeStatus.Unchanged);
            var notifyPropertyChangedInterceptor = CreateInstance(typeof(NotifyPropertyChangedInterceptor<>).MakeGenericType(type), changeTrackingInterceptor);
            var editableObjectInterceptor = CreateInstance(typeof(EditableObjectInterceptor<>).MakeGenericType(type), notifyParentItemCanceled);
            var complexPropertyInterceptor = CreateInstance(typeof(ComplexPropertyInterceptor<>).MakeGenericType(type), makeComplexPropertiesTrackable, makeCollectionPropertiesTrackable);
            var collectionPropertyInterceptor = CreateInstance(typeof(CollectionPropertyInterceptor<>).MakeGenericType(type), makeComplexPropertiesTrackable, makeCollectionPropertiesTrackable);
            object proxy = _ProxyGenerator.CreateClassProxyWithTarget(type,
                         new[] { typeof(IChangeTrackableInternal), typeof(IChangeTrackable<>).MakeGenericType(type), typeof(IChangeTrackingManager), typeof(IComplexPropertyTrackable), typeof(ICollectionPropertyTrackable), typeof(IEditableObject), typeof(System.ComponentModel.INotifyPropertyChanged) },
                         target,
                         GetOptions(type),
                         (IInterceptor)notifyPropertyChangedInterceptor,
                         (IInterceptor)changeTrackingInterceptor,
                         (IInterceptor)editableObjectInterceptor,
                         (IInterceptor)complexPropertyInterceptor,
                         (IInterceptor)collectionPropertyInterceptor);
            ((IInterceptorSettings)notifyPropertyChangedInterceptor).IsInitialized = true;
            ((IInterceptorSettings)changeTrackingInterceptor).IsInitialized = true;
            ((IInterceptorSettings)editableObjectInterceptor).IsInitialized = true;
            ((IInterceptorSettings)complexPropertyInterceptor).IsInitialized = true;
            ((IInterceptorSettings)collectionPropertyInterceptor).IsInitialized = true;
            return proxy;
        }

        private static object CreateInstance(Type type, params object[] args)
        {
            return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, args, null);
        }

        public static T AsTrackable<T>(this T target, ChangeStatus status = ChangeStatus.Unchanged, bool makeComplexPropertiesTrackable = true, bool makeCollectionPropertiesTrackable = true) where T : class
        {
            return AsTrackable(target, status, null, makeComplexPropertiesTrackable, makeCollectionPropertiesTrackable);
        }

        internal static T AsTrackable<T>(this T target, ChangeStatus status, Action<T> notifyParentListItemCanceled, bool makeComplexPropertiesTrackable, bool makeCollectionPropertiesTrackable) where T : class
        {
            //if T was ICollection<T> it would of gone to one of the other overloads
            if (target as ICollection != null)
            {
                throw new InvalidOperationException("Only IList<T>, List<T> and ICollection<T> are supported");
            }

            var changeTrackingInterceptor = new ChangeTrackingInterceptor<T>(status);
            var notifyPropertyChangedInterceptor = new NotifyPropertyChangedInterceptor<T>(changeTrackingInterceptor);
            var editableObjectInterceptor = new EditableObjectInterceptor<T>(notifyParentListItemCanceled);
            var complexPropertyInterceptor = new ComplexPropertyInterceptor<T>(makeComplexPropertiesTrackable, makeCollectionPropertiesTrackable);
            var collectionPropertyInterceptor = new CollectionPropertyInterceptor<T>(makeComplexPropertiesTrackable, makeCollectionPropertiesTrackable);
            object proxy = _ProxyGenerator.CreateClassProxyWithTarget(typeof(T),
                new[] { typeof(IChangeTrackableInternal), typeof(IChangeTrackable<T>), typeof(IChangeTrackingManager), typeof(IComplexPropertyTrackable), typeof(ICollectionPropertyTrackable), typeof(IEditableObject), typeof(System.ComponentModel.INotifyPropertyChanged) },
                target,
                GetOptions(typeof(T)),
                notifyPropertyChangedInterceptor,
                changeTrackingInterceptor,
                editableObjectInterceptor,
                complexPropertyInterceptor,
                collectionPropertyInterceptor);
            notifyPropertyChangedInterceptor.IsInitialized = true;
            changeTrackingInterceptor.IsInitialized = true;
            editableObjectInterceptor.IsInitialized = true;
            complexPropertyInterceptor.IsInitialized = true;
            collectionPropertyInterceptor.IsInitialized = true;
            return (T)proxy;
        }

        public static ICollection<T> AsTrackable<T>(this System.Collections.ObjectModel.Collection<T> target) where T : class
        {
            return AsTrackable(target, true, true);
        }

        public static ICollection<T> AsTrackable<T>(this System.Collections.ObjectModel.Collection<T> target, bool makeComplexPropertiesTrackable = true, bool makeCollectionPropertiesTrackable = true) where T : class
        {
            return ((ICollection<T>)target).AsTrackable(makeComplexPropertiesTrackable, makeCollectionPropertiesTrackable);
        }

        public static ICollection<T> AsTrackable<T>(this ICollection<T> target) where T : class
        {
            return AsTrackable(target, true, true);
        }

        public static ICollection<T> AsTrackable<T>(this ICollection<T> target, bool makeComplexPropertiesTrackable, bool makeCollectionPropertiesTrackable) where T : class
        {
            var list = target as IList<T>;
            if (list == null)
            {
                list = target.ToList();
            }
            return list.AsTrackable(makeComplexPropertiesTrackable, makeCollectionPropertiesTrackable);
        }

        public static IList<T> AsTrackable<T>(this List<T> target) where T : class
        {
            return AsTrackable(target, true, true);
        }

        public static IList<T> AsTrackable<T>(this List<T> target, bool makeComplexPropertiesTrackable, bool makeCollectionPropertiesTrackable) where T : class
        {
            return ((IList<T>)target).AsTrackable(makeComplexPropertiesTrackable, makeCollectionPropertiesTrackable);
        }

        public static IList<T> AsTrackable<T>(this IList<T> target) where T : class
        {
            return AsTrackable(target, true, true);
        }

        public static IList<T> AsTrackable<T>(this IList<T> target, bool makeComplexPropertiesTrackable, bool makeCollectionPropertiesTrackable) where T : class
        {
            if (target.OfType<IChangeTrackable<T>>().Any(ct => ct.ChangeTrackingStatus != ChangeStatus.Unchanged))
            {
                throw new InvalidOperationException("some items in the collection are already being tracked");
            }
            object proxy = _ProxyGenerator.CreateInterfaceProxyWithTarget(typeof(IList<T>),
                new[] { typeof(IChangeTrackableCollection<T>), typeof(IBindingList), typeof(ICancelAddNew), typeof(INotifyCollectionChanged) }, target, GetOptions(typeof(T)), new ChangeTrackingCollectionInterceptor<T>(target, makeComplexPropertiesTrackable, makeCollectionPropertiesTrackable));
            return (IList<T>)proxy;
        }
    }
}
