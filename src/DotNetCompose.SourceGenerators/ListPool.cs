using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace DotNetCompose.SourceGenerators
{
    struct ListPoolObject<T> : IDisposable, IList<T>
    {
        public ListPoolObject(List<T> list)
        {
            _list = list;
            _disposed = false;
        }
        [EditorBrowsable(EditorBrowsableState.Never)]
        public List<T> List => _list;

        private List<T> _list;

        public int Count => ((ICollection<T>)_list).Count;

        public bool IsReadOnly => ((ICollection<T>)_list).IsReadOnly;

        public T this[int index]
        {
            get => ((IList<T>)_list)[index];
            set => ((IList<T>)_list)[index] = value;
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            ListPool<T>.Return(this);
        }

        #region IList
        public int IndexOf(T item)
        {
            return ((IList<T>)_list).IndexOf(item);
        }

        public void AddRange(IEnumerable<T> values)
        {
            if (values == null || !values.Any())
                return;
            _list.AddRange(values);
        }
        public void Insert(int index, T item)
        {
            ((IList<T>)_list).Insert(index, item);
        }

        public void RemoveAt(int index)
        {
            ((IList<T>)_list).RemoveAt(index);
        }

        public void Add(T item)
        {
            ((ICollection<T>)_list).Add(item);
        }

        public void Clear()
        {
            ((ICollection<T>)_list).Clear();
        }

        public bool Contains(T item)
        {
            return ((ICollection<T>)_list).Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            ((ICollection<T>)_list).CopyTo(array, arrayIndex);
        }

        public bool Remove(T item)
        {
            return ((ICollection<T>)_list).Remove(item);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)_list).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_list).GetEnumerator();
        }
        #endregion
    }
    internal static class ListPool<T>
    {
        private static ObjectPool<List<T>> _listPool = ObjectPool.Create<List<T>>();

        public static ListPoolObject<T> Get()
        {
            List<T> list = _listPool.Get();
            return new ListPoolObject<T>(list);
        }

        public static void Return(ListPoolObject<T> obj)
        {
            obj.List.Clear();
            _listPool.Return(obj.List);
        }
    }
}
