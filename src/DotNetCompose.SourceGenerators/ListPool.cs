using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.SourceGenerators
{
    struct ListPoolObject<T> : IDisposable
    {
        public ListPoolObject(List<T> list)
        {
            List = list;
            _disposed = false;
        }
        public List<T> List { get; }
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            
        }
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
