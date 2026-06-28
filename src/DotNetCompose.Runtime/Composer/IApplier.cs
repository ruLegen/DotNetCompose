using System;

namespace DotNetCompose.Runtime.Composer
{
    public interface IApplier<TNode>
    {
        TNode Current { get; }

        void OnBeginChanges();
        void OnEndChanges();

        void Down(TNode node);

        void Up();

        void InsertTopDown(int index, TNode instance);

        void InsertBottomUp(int index, TNode instance);

        void Remove(int index, int count);

        void Move(int from, int to, int count);

        void Clear();

        void Apply(Action<TNode, object?> block, object? value);

        void Reuse() { }
    }
}
