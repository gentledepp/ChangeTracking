﻿using Castle.DynamicProxy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ChangeTracking
{
    internal sealed class ChangeTrackingCollectionInterceptor<T> : IInterceptor, IChangeTrackableCollection<T>, IInterceptorSettings where T : class
    {
        private ChangeTrackingBindingList<T> _WrappedTarget;
        internal IList<DeletedItem<T>> _DeletedItems;
        private static HashSet<string> _ImplementedMethods;
        private static HashSet<string> _BindingListImplementedMethods;
        private static HashSet<string> _IBindingListImplementedMethods;
        private readonly bool _MakeComplexPropertiesTrackable;
        private readonly bool _MakeCollectionPropertiesTrackable;

        public bool IsInitialized { get; set; }

        static ChangeTrackingCollectionInterceptor()
        {
            _ImplementedMethods = new HashSet<string>(typeof(ChangeTrackingCollectionInterceptor<T>).GetMethods(BindingFlags.Instance | BindingFlags.Public).Select(m => m.Name));
            _BindingListImplementedMethods = new HashSet<string>(typeof(ChangeTrackingBindingList<T>).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy).Select(m => m.Name));
            _IBindingListImplementedMethods = new HashSet<string>(typeof(ChangeTrackingBindingList<T>).GetInterfaceMap(typeof(System.ComponentModel.IBindingList)).TargetMethods.Where(mi => mi.IsPrivate).Select(mi => mi.Name.Substring(mi.Name.LastIndexOf('.') + 1)));
        }

        internal ChangeTrackingCollectionInterceptor(IList<T> target, bool makeComplexPropertiesTrackable, bool makeCollectionPropertiesTrackable)
        {
            _MakeComplexPropertiesTrackable = makeComplexPropertiesTrackable;
            _MakeCollectionPropertiesTrackable = makeCollectionPropertiesTrackable;
            for (int i = 0; i < target.Count; i++)
            {
                target[i] = target[i].AsTrackable(ChangeStatus.Unchanged, ItemCanceled, _MakeComplexPropertiesTrackable, _MakeCollectionPropertiesTrackable);
            }
            _WrappedTarget = new ChangeTrackingBindingList<T>(target, InsertItem, DeleteItem, ItemCanceled, _MakeComplexPropertiesTrackable, _MakeCollectionPropertiesTrackable);
            _DeletedItems = new List<DeletedItem<T>>();
        }

        public void Intercept(IInvocation invocation)
        {
            if (_ImplementedMethods.Contains(invocation.Method.Name))
            {
                invocation.ReturnValue = invocation.Method.Invoke(this, invocation.Arguments);
                return;
            }
            if (_BindingListImplementedMethods.Contains(invocation.Method.Name))
            {
                invocation.ReturnValue = invocation.Method.Invoke(_WrappedTarget, invocation.Arguments);
                return;
            }
            if (_IBindingListImplementedMethods.Contains(invocation.Method.Name))
            {
                invocation.ReturnValue = invocation.Method.Invoke(_WrappedTarget, invocation.Arguments);
                return;
            }
            invocation.Proceed();
        }

        private void DeleteItem(T item, int index)
        {
            var currentStatus = item.CastToIChangeTrackable().ChangeTrackingStatus;
            var manager = (IChangeTrackingManager)item;
            bool deleteSuccess = manager.Delete();
            if (deleteSuccess && currentStatus != ChangeStatus.Added)
            {
                _DeletedItems.Add(new DeletedItem<T>(item, index, currentStatus));
            }
        }

        private void InsertItem(int index, T item)
        {
            var deletedItem = _DeletedItems.FirstOrDefault(d => d.Item == item);
            if (deletedItem != null)
            {
                _DeletedItems.Remove(deletedItem);

                var manager = (IChangeTrackingManager)item;
                manager.UpdateStatus();
            }
        }

        private void ItemCanceled(T item)
        {
            _WrappedTarget.CancelNew(_WrappedTarget.IndexOf(item));
        }

        public IEnumerable<T> UnchangedItems
        {
            get { return _WrappedTarget.Cast<IChangeTrackable<T>>().Where(ct => ct.ChangeTrackingStatus == ChangeStatus.Unchanged).Cast<T>(); }
        }

        public IEnumerable<T> AddedItems
        {
            get { return _WrappedTarget.Cast<IChangeTrackable<T>>().Where(ct => ct.ChangeTrackingStatus == ChangeStatus.Added).Cast<T>(); }
        }

        public IEnumerable<T> ChangedItems
        {
            get { return _WrappedTarget.Cast<IChangeTrackable<T>>().Where(ct => ct.ChangeTrackingStatus == ChangeStatus.Changed).Cast<T>(); }
        }

        public IEnumerable<T> DeletedItems
        {
            get { return _DeletedItems.Select(i => i.Item); }
        }

        public bool UnDelete(T item)
        {
            var manager = (IChangeTrackingManager)item;
            bool unDeleteSuccess = manager.UnDelete();
            if (unDeleteSuccess)
            {
                var deletedItem = _DeletedItems.Single(d => d.Item == item);
                bool removeSuccess = _DeletedItems.Remove(deletedItem);
                if (removeSuccess)
                {
                    _WrappedTarget.Insert(deletedItem.Index, deletedItem.Item);
                    return true;
                }
            }
            return false;
        }

        public void AcceptChanges()
        {
            foreach (var item in _WrappedTarget.Cast<IChangeTrackable<T>>())
            {
                item.AcceptChanges();
                var editable = item as System.ComponentModel.IEditableObject;
                if (editable != null)
                {
                    editable.EndEdit();
                }
            }
            _DeletedItems.Clear();
        }

        public void RejectChanges()
        {
            AddedItems.ToList().ForEach(i => _WrappedTarget.Remove(i));
            foreach (var item in _WrappedTarget.Cast<IChangeTrackable<T>>())
            {
                item.RejectChanges();
            }
            foreach (var item in _DeletedItems.OrderBy(i => i.Index))
            {
                ((System.ComponentModel.IRevertibleChangeTracking)item.Item).RejectChanges();
                _WrappedTarget.Insert(item.Index, item.Item);
            }
            _DeletedItems.Clear();
        }

        public bool IsChanged
        {
            get
            {
                return ChangedItems.Any() || AddedItems.Any() || DeletedItems.Any();
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _WrappedTarget.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        internal class DeletedItem<T>
        {
            public DeletedItem(T item, int index, ChangeStatus previousStatus)
            {
                Item = item;
                Index = index;
                PreviousStatus = previousStatus;
            }
            public T Item { get; set; }
            public int Index { get; set; }
            public ChangeStatus PreviousStatus { get; set; }
        }
    }
}
