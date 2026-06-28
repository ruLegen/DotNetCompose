using System;

namespace DotNetCompose.Runtime.Composer
{
    internal sealed class NoOpApplier : IApplier<object>
    {
        public static readonly NoOpApplier Instance = new();
        public object Current => null!;
        public void Down(object node) { }
        public void Up() { }
        public void Reuse() { }
        public void Apply(Action<object, object?> block, object? value) { }
        public void Remove(int index, int count) { }
        public void Move(int from, int to, int count) { }
        public void InsertTopDown(int index, object node) { }
        public void InsertBottomUp(int index, object node) { }
        public void Clear() { }
        public void OnBeginChanges() { }
        public void OnEndChanges() { }
    }
}
