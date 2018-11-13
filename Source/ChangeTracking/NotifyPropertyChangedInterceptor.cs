﻿using Castle.DynamicProxy;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace ChangeTracking
{
    internal class NotifyPropertyChangedInterceptor<T> : IInterceptor, IInterceptorSettings where T : class
    {
        private static Dictionary<string, PropertyInfo> _Properties;
        private readonly Dictionary<string, PropertyChangedEventHandler> _PropertyChangedEventHandlers;
        private readonly object _PropertyChangedEventHandlersLock;
        private readonly Dictionary<string, ListChangedEventHandler> _ListChangedEventHandlers;
        private readonly object _ListChangedEventHandlersLock;

        private static readonly PropertyInfo _DynamicProperty;

        public bool IsInitialized { get; set; }

        static NotifyPropertyChangedInterceptor()
        {
            _Properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(pi => pi.Name);

            // in case an object implements something like
            // public virtual string this[string key]{get;set;}
            // this is the only type of property in C# that has a separate parameter for getter and setter methods
            // and can be used to hold dynamic properties (which are not known at compile time)
            // this is often used in conjunction with JSON
            _DynamicProperty = _Properties.ExtractIndexerProperty();
        }

        internal NotifyPropertyChangedInterceptor(ChangeTrackingInterceptor<T> changeTrackingInterceptor)
        {
            _PropertyChangedEventHandlers = new Dictionary<string, PropertyChangedEventHandler>();
            _PropertyChangedEventHandlersLock = new object();
            _ListChangedEventHandlers = new Dictionary<string, ListChangedEventHandler>();
            _ListChangedEventHandlersLock = new object();
            changeTrackingInterceptor._StatusChanged += (o, e) =>
            {
                RaisePropertyChanged(o, nameof(IChangeTrackable.ChangeTrackingStatus));
                RaisePropertyChanged(o, nameof(IChangeTrackable.ChangedProperties));
            };
            PropertyChanged += delegate { };
        }

        public void Intercept(IInvocation invocation)
        {
            if (!IsInitialized)
            {
                return;
            }
            if (invocation.Method.IsSetter())
            {
                string propertyName = invocation.GetPropertyName();
                var previousValue = GetProperty(propertyName).GetValue(invocation.Proxy, invocation.GetParameter());
                invocation.Proceed();
                var newValue = GetProperty(propertyName).GetValue(invocation.Proxy, invocation.GetParameter());
                if (!Equals(previousValue, newValue))
                {
                    RaisePropertyChanged(invocation.Proxy, propertyName);
                    RaisePropertyChanged(invocation.Proxy, nameof(IChangeTrackable.ChangedProperties));
                }
                if (!ReferenceEquals(previousValue, newValue))
                {
                    UnsubscribeFromChildPropertyChanged(propertyName, previousValue);
                    SubscribeToChildPropertyChanged(invocation, propertyName, newValue);
                    UnsubscribeFromChildListChanged(propertyName, previousValue);
                    SubscribeToChildListChanged(invocation, propertyName, newValue);
                }
                return;
            }
            if (invocation.Method.IsGetter())
            {
                invocation.Proceed();
                string propertyName = invocation.GetPropertyName();
                SubscribeToChildPropertyChanged(invocation, propertyName, invocation.ReturnValue);
                SubscribeToChildListChanged(invocation, propertyName, invocation.ReturnValue);
                return;
            }
            switch (invocation.Method.Name)
            {
                case "add_PropertyChanged":
                    PropertyChanged += (PropertyChangedEventHandler)invocation.Arguments[0];
                    break;
                case "remove_PropertyChanged":
                    PropertyChanged -= (PropertyChangedEventHandler)invocation.Arguments[0];
                    break;
                default:
                    invocation.Proceed();
                    break;
            }
        }

        private void UnsubscribeFromChildPropertyChanged(string propertyName, object oldChild)
        {
            if (oldChild is INotifyPropertyChanged trackable)
            {
                lock (_PropertyChangedEventHandlersLock)
                {
                    if (_PropertyChangedEventHandlers.TryGetValue(propertyName, out PropertyChangedEventHandler handler))
                    {
                        trackable.PropertyChanged -= handler;
                        _PropertyChangedEventHandlers.Remove(propertyName);
                    } 
                }
            }
        }

        private void SubscribeToChildPropertyChanged(IInvocation invocation, string propertyName, object newValue)
        {
            if (newValue is INotifyPropertyChanged newChild)
            {
                lock (_PropertyChangedEventHandlersLock)
                {
                    if (!_PropertyChangedEventHandlers.ContainsKey(propertyName))
                    {
                        void newHandler(object sender, PropertyChangedEventArgs e) => RaisePropertyChanged(invocation.Proxy, propertyName);
                        newChild.PropertyChanged += newHandler;
                        _PropertyChangedEventHandlers.Add(propertyName, newHandler);
                    } 
                }
            }
        }

        private void UnsubscribeFromChildListChanged(string propertyName, object oldChild)
        {
            if (oldChild is IBindingList trackable)
            {
                lock (_ListChangedEventHandlersLock)
                {
                    if (_ListChangedEventHandlers.TryGetValue(propertyName, out ListChangedEventHandler handler))
                    {
                        trackable.ListChanged -= handler;
                        _ListChangedEventHandlers.Remove(propertyName);
                    }
                }
            }
        }

        private void SubscribeToChildListChanged(IInvocation invocation, string propertyName, object newValue)
        {
            if (newValue is IBindingList newChild)
            {
                lock (_ListChangedEventHandlersLock)
                {
                    if (!_ListChangedEventHandlers.ContainsKey(propertyName))
                    {
                        void newHandler(object sender, ListChangedEventArgs e) => RaisePropertyChanged(invocation.Proxy, propertyName);
                        newChild.ListChanged += newHandler;
                        _ListChangedEventHandlers.Add(propertyName, newHandler);
                    }
                }
            }
        }

        private event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged(object proxy, string propertyName)
        {
            PropertyChanged(proxy, new PropertyChangedEventArgs(propertyName));
        }

        internal static PropertyInfo GetProperty(string propertyName)
        {
            PropertyInfo property;
            if (_Properties.TryGetValue(propertyName, out property))
            {
                return property;
            }

            if (_DynamicProperty != null)
                return _DynamicProperty;

            throw new InvalidOperationException($"The type '{typeof(T).FullName}' has no property named '{propertyName}'");
        }
    }
}
