using System;

namespace DotNetCompose.Runtime.SlotTable.GapBuffer
{
    public class GapBufferSlotMap<T>
    {
        private readonly GapBuffer<T> _source;

        private int[] _handleToPhysical;
        private int[] _physicalToHandle;
        private int[] _generation;

        private int _nextNewId;
        private int _count;
        private int _freeHead = -1;

        private const int DefaultCapacity = 16;

        public GapBufferSlotMap(GapBuffer<T> source, int initialCapacity = DefaultCapacity)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _source.OnElementsMoved = OnElementsMoved;

            _handleToPhysical = new int[initialCapacity];
            _physicalToHandle = new int[initialCapacity];
            _generation = new int[initialCapacity];

            for (int i = 0; i < initialCapacity - 1; i++)
                _handleToPhysical[i] = i + 1;
            _handleToPhysical[initialCapacity - 1] = -1;
            _freeHead = 0;
        }

        public int Count => _count;

        public GapBufferItemAnchor Insert(T item)
        {
            int physicalIndex = _source.Insert(_source.Count, item);
            return Register(physicalIndex);
        }

        public GapBufferItemAnchor Insert(int position, T item)
        {
            int physicalIndex = _source.Insert(position, item);
            return Register(physicalIndex);
        }

        public GapBufferItemAnchor Register(int physicalIndex)
        {
            int id = AllocateId();
            _generation[id]++;

            _handleToPhysical[id] = physicalIndex;
            EnsurePhysicalCapacity(physicalIndex);
            _physicalToHandle[physicalIndex] = id;
            _count++;

            return new GapBufferItemAnchor(id, _generation[id]);
        }

        public T Get(GapBufferItemAnchor handle)
        {
            if (!handle.IsValid)
                throw new ArgumentException("Invalid handle: generation is 0.", nameof(handle));
            if (handle.Id >= _handleToPhysical.Length || _generation[handle.Id] != handle.Generation)
                throw new InvalidOperationException("Stale or invalid handle.");

            int physicalIndex = _handleToPhysical[handle.Id];
            return _source.GetAtPhysical(physicalIndex);
        }

        public void Set(GapBufferItemAnchor handle, T item)
        {
            if (!handle.IsValid)
                throw new ArgumentException("Invalid handle: generation is 0.", nameof(handle));
            if (handle.Id >= _handleToPhysical.Length || _generation[handle.Id] != handle.Generation)
                throw new InvalidOperationException("Stale or invalid handle.");

            int physicalIndex = _handleToPhysical[handle.Id];
            _source.SetAtPhysical(physicalIndex, item);
        }

        public void Remove(GapBufferItemAnchor handle)
        {
            if (!handle.IsValid || handle.Id >= _handleToPhysical.Length)
                throw new ArgumentException("Invalid handle.", nameof(handle));
            if (_generation[handle.Id] != handle.Generation)
                throw new InvalidOperationException("Stale handle.");

            int physicalIndex = _handleToPhysical[handle.Id];
            _source.RemoveAtPhysical(physicalIndex);

            _generation[handle.Id]++;
            FreeId(handle.Id);
            _count--;
        }

        public bool IsValidAnchor(GapBufferItemAnchor handle)
        {
            return handle.IsValid
                && handle.Id < _handleToPhysical.Length
                && _generation[handle.Id] == handle.Generation;
        }

        private void OnElementsMoved(int fromPhysical, int toPhysical, int count)
        {
            if (fromPhysical == toPhysical) return;

            if (fromPhysical < toPhysical)
            {
                for (int i = count - 1; i >= 0; i--)
                    MovePhysical(fromPhysical + i, toPhysical + i);
            }
            else
            {
                for (int i = 0; i < count; i++)
                    MovePhysical(fromPhysical + i, toPhysical + i);
            }
        }

        private void MovePhysical(int oldPos, int newPos)
        {
            EnsurePhysicalCapacity(Math.Max(oldPos, newPos));

            int handleId = _physicalToHandle[oldPos];
            _physicalToHandle[oldPos] = -1;

            if (handleId != -1)
            {
                _handleToPhysical[handleId] = newPos;
                _physicalToHandle[newPos] = handleId;
            }
            else
            {
                _physicalToHandle[newPos] = -1;
            }
        }

        private int AllocateId()
        {
            if (_freeHead != -1)
            {
                int id = _freeHead;
                _freeHead = _handleToPhysical[id];
                return id;
            }

            int newCapacity = _handleToPhysical.Length * 2;
            Array.Resize(ref _handleToPhysical, newCapacity);
            Array.Resize(ref _physicalToHandle, newCapacity);
            Array.Resize(ref _generation, newCapacity);

            for (int i = _nextNewId; i < newCapacity - 1; i++)
                _handleToPhysical[i] = i + 1;
            _handleToPhysical[newCapacity - 1] = -1;
            _freeHead = _nextNewId;

            int newId = _freeHead;
            _freeHead = _handleToPhysical[newId];
            _nextNewId = newId + 1;
            return newId;
        }

        private void FreeId(int id)
        {
            _handleToPhysical[id] = _freeHead;
            _freeHead = id;
        }

        private void EnsurePhysicalCapacity(int physicalIndex)
        {
            if (physicalIndex >= _physicalToHandle.Length)
            {
                int newCapacity = Math.Max(physicalIndex + 1, _physicalToHandle.Length * 2);
                Array.Resize(ref _handleToPhysical, newCapacity);
                Array.Resize(ref _physicalToHandle, newCapacity);
                Array.Resize(ref _generation, newCapacity);
            }
        }
    }
}
