using System;
using System.Collections.Generic;

namespace DotNetCompose.Runtime.Composer
{
    public abstract class AbstractApplier<TNode> : IApplier<TNode>
    {
        private readonly Stack<TNode> _stack = new Stack<TNode>();
        private TNode _current;

        public TNode Root { get; }

        public TNode Current => _current;

        protected AbstractApplier(TNode root)
        {
            Root = root;
            _current = root;
        }

        public virtual void OnBeginChanges() { }
        public virtual void OnEndChanges() { }

        public void Down(TNode node)
        {
            _stack.Push(_current);
            _current = node;
        }

        public void Up()
        {
            _current = _stack.Pop();
        }

        public void Clear()
        {
            _stack.Clear();
            _current = Root;
            OnClear();
        }

        protected abstract void OnClear();

        public abstract void InsertTopDown(int index, TNode instance);
        public abstract void InsertBottomUp(int index, TNode instance);
        public abstract void Remove(int index, int count);
        public abstract void Move(int from, int to, int count);

        public virtual void Apply(Action<TNode, object?> block, object? value)
        {
            block(Current, value);
        }

        public virtual void Reuse() { }

        protected static void RemoveRange(List<TNode> list, int index, int count)
        {
            if (count == 1)
                list.RemoveAt(index);
            else
                list.RemoveRange(index, count);
        }

        protected static void MoveRange(List<TNode> list, int from, int to, int count)
        {
            int dest = from > to ? to : to - count;
            if (count == 1)
            {
                var fromEl = list[from];
                list.RemoveAt(from);
                list.Insert(dest, fromEl);
            }
            else
            {
                var sub = list.GetRange(from, count);
                list.RemoveRange(from, count);
                list.InsertRange(dest, sub);
            }
        }
    }
}
