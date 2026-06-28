using System;

namespace DotNetCompose.Runtime.Snapshots.Utils
{
    internal class SnapshotDoubleIndexHeap
    {
        private int _size;
        private long[] _values = new long[InitialCapacity];
        private int[] _index = new int[InitialCapacity];
        private int[] _handles = new int[InitialCapacity];
        private int _firstFreeHandle;

        private const int InitialCapacity = 16;

        public SnapshotDoubleIndexHeap()
        {
            for (int i = 0; i < InitialCapacity; i++)
                _handles[i] = i + 1;
        }

        public long LowestOrDefault(long defaultValue = 0)
        {
            return _size > 0 ? _values[0] : defaultValue;
        }

        public int Add(long value)
        {
            Ensure(_size + 1);
            int i = _size++;
            int handle = AllocateHandle();
            _values[i] = value;
            _index[i] = handle;
            _handles[handle] = i;
            ShiftUp(i);
            return handle;
        }

        public void Remove(int handle)
        {
            int i = _handles[handle];
            Swap(i, _size - 1);
            _size--;
            ShiftUp(i);
            ShiftDown(i);
            FreeHandle(handle);
        }

        private void ShiftUp(int idx)
        {
            long value = _values[idx];
            int current = idx;
            while (current > 0)
            {
                int parent = ((current + 1) >> 1) - 1;
                if (_values[parent] > value)
                {
                    Swap(parent, current);
                    current = parent;
                    continue;
                }
                break;
            }
        }

        private void ShiftDown(int idx)
        {
            int half = _size >> 1;
            int current = idx;
            while (current < half)
            {
                int right = (current + 1) << 1;
                int left = right - 1;
                if (right < _size && _values[right] < _values[left])
                {
                    if (_values[right] < _values[current])
                    {
                        Swap(right, current);
                        current = right;
                    }
                    else return;
                }
                else if (_values[left] < _values[current])
                {
                    Swap(left, current);
                    current = left;
                }
                else return;
            }
        }

        private void Swap(int a, int b)
        {
            long tempValue = _values[a];
            _values[a] = _values[b];
            _values[b] = tempValue;

            int tempIndex = _index[a];
            _index[a] = _index[b];
            _index[b] = tempIndex;

            _handles[_index[a]] = a;
            _handles[_index[b]] = b;
        }

        private void Ensure(int atLeast)
        {
            int capacity = _values.Length;
            if (atLeast <= capacity) return;
            int newCapacity = capacity * 2;
            Array.Resize(ref _values, newCapacity);
            Array.Resize(ref _index, newCapacity);
        }

        private int AllocateHandle()
        {
            int capacity = _handles.Length;
            if (_firstFreeHandle >= capacity)
            {
                int newCapacity = capacity * 2;
                var newHandles = new int[newCapacity];
                Array.Copy(_handles, newHandles, capacity);
                for (int i = capacity; i < newCapacity; i++)
                    newHandles[i] = i + 1;
                _handles = newHandles;
            }
            int handle = _firstFreeHandle;
            _firstFreeHandle = _handles[_firstFreeHandle];
            return handle;
        }

        private void FreeHandle(int handle)
        {
            _handles[handle] = _firstFreeHandle;
            _firstFreeHandle = handle;
        }
    }
}
