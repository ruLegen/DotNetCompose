using System;

namespace DotNetCompose.Runtime.Composer
{
    internal class Operations
    {
        private const int InitialCapacity = 16;
        private const int MaxResizeAmount = 1024;

        internal Operation[] _opCodes = new Operation[InitialCapacity];
        internal int _opCodesSize;
        internal int[] _intArgs = new int[InitialCapacity];
        internal int _intArgsSize;
        internal object?[] _objectArgs = new object?[InitialCapacity];
        internal int _objectArgsSize;

        public int Size => _opCodesSize;
        public bool IsEmpty => _opCodesSize == 0;

        public void Clear()
        {
            _opCodesSize = 0;
            _intArgsSize = 0;
            Array.Clear(_objectArgs, 0, _objectArgsSize);
            _objectArgsSize = 0;
        }

        public void Push(Operation operation)
        {
            if (_opCodesSize >= _opCodes.Length)
                Resize(ref _opCodes, _opCodesSize);

            EnsureCapacity(ref _intArgs, _intArgsSize, _intArgsSize + operation.IntCount);
            EnsureCapacity(ref _objectArgs, _objectArgsSize, _objectArgsSize + operation.ObjectCount);

            _opCodes[_opCodesSize++] = operation;
            _intArgsSize += operation.IntCount;
            _objectArgsSize += operation.ObjectCount;
        }

        public void Push(Operation operation, Action<OperationArgWriter> args)
        {
            Push(operation);
            var writer = new OperationArgWriter(this, operation);
            args(writer);
        }

        public void Drain(IApplier<object> applier, SlotWriter slots, RememberManager rememberManager)
        {
            if (IsEmpty) return;
            var iterator = new OpIterator(this);
            do
            {
                var op = iterator.Operation;
                op.Execute(applier, slots, rememberManager,
                    param => iterator.GetInt(param),
                    param => iterator.GetObject(param));
            } while (iterator.Next());
            Clear();
        }

        public void DrainWithApplier(
            IApplier<object> applier,
            SlotWriter slots,
            RememberManager rememberManager)
        {
            Drain(applier, slots, rememberManager);
        }

        public struct OpIterator
        {
            private Operations _ops;
            private int _opIdx;
            private int _intIdx;
            private int _objIdx;

            internal OpIterator(Operations ops)
            {
                _ops = ops;
                _opIdx = 0;
                _intIdx = 0;
                _objIdx = 0;
            }

            public Operation Operation => _ops._opCodes[_opIdx];

            public int GetInt(int parameter) => _ops._intArgs[_intIdx + parameter];

            public object? GetObject(int parameter) => _ops._objectArgs[_objIdx + parameter];

            public bool Next()
            {
                var op = _ops._opCodes[_opIdx];
                _intIdx += op.IntCount;
                _objIdx += op.ObjectCount;
                _opIdx++;
                return _opIdx < _ops._opCodesSize;
            }
        }

        private static void Resize<T>(ref T[] array, int currentSize)
        {
            int newSize = Math.Min(currentSize + MaxResizeAmount, currentSize * 2);
            Array.Resize(ref array, Math.Max(newSize, currentSize + 1));
        }

        private static void EnsureCapacity(ref int[] array, int currentSize, int requiredSize)
        {
            if (requiredSize <= array.Length) return;
            Resize(ref array, currentSize);
        }

        private static void EnsureCapacity(ref object?[] array, int currentSize, int requiredSize)
        {
            if (requiredSize <= array.Length) return;
            Resize(ref array, currentSize);
        }
    }

    internal struct OperationArgWriter
    {
        private Operations _ops;
        private int _intOffset;
        private int _objOffset;

        internal OperationArgWriter(Operations ops, Operation op)
        {
            _ops = ops;
            _intOffset = ops._intArgsSize - op.IntCount;
            _objOffset = ops._objectArgsSize - op.ObjectCount;
        }

        public void SetInt(int parameter, int value)
        {
            _ops._intArgs[_intOffset + parameter] = value;
        }

        public void SetObject<T>(int parameter, T value)
        {
            _ops._objectArgs[_objOffset + parameter] = value;
        }
    }
}
