using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DotNetCompose.Runtime.SlotTable.GapBuffer
{
    public class GapBufferSlotMap<T>
    {
        private const int DefaultCapacity = 16;
        public GapBufferSlotMap(int initialCapacity = DefaultCapacity) 
            : this(new GapBuffer<T>(initialCapacity))
        {
        }

        public GapBufferSlotMap(GapBuffer<T> source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _source.OnElementsMoved = OnElementsMoved;

            int initialCapacity = Math.Max(1, _source.Capacity);
            _handleToAddress = new int[initialCapacity];
            _addressToHandle = new int[initialCapacity];
            _generation = new int[initialCapacity];
            _active = new bool[initialCapacity];
            Array.Fill(_addressToHandle, -1);

            for (int i = 0; i < initialCapacity - 1; i++)
                _handleToAddress[i] = i + 1;
            _handleToAddress[initialCapacity - 1] = -1;
            _freeHead = 0;
        }

        public int Count => _count;

        private readonly GapBuffer<T> _source;

        private int[] _handleToAddress;
        private int[] _addressToHandle;
        private int[] _generation;
        private bool[] _active;

        private int _count;
        private int _freeHead = -1;

        public IEnumerable<T> GetEnumerable()
        {
            for (int i = 0; i < _source.Count; i++)
            {
                yield return _source[i];
            }
        }
        public GapBufferItemAnchor Insert(T item)
        {
            int physicalIndex = _source.Insert(_source.Count, item);
            return Track(physicalIndex);
        }

        public GapBufferItemAnchor Insert(int index, T item)
        {
            if (index < 0 || index > _source.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            int physicalIndex = _source.Insert(index, item);
            return Track(physicalIndex);
        }

        public GapBufferItemAnchor Track(int address)
        {
            ValidateAddress(address);
            EnsurePhysicalCapacity(address);
            int existing = _addressToHandle[address];
            if (existing >= 0)
                return new GapBufferItemAnchor(existing, _generation[existing]);

            int id = AllocateId();
            _generation[id]++;
            _active[id] = true;

            _handleToAddress[id] = address;
            _addressToHandle[address] = id;
            _count++;

            return new GapBufferItemAnchor(id, _generation[id]);
        }

        public T Get(GapBufferItemAnchor handle)
        {
            EnsureValid(handle);

            int address = _handleToAddress[handle.Id];
            return _source.GetAtAddress(address);
        }
        public ref T GetRef(GapBufferItemAnchor handle)
        {
            EnsureValid(handle);

            int address = _handleToAddress[handle.Id];
            return ref _source.GetAtAddressRef(address);
        }

        public void Set(GapBufferItemAnchor handle, T item)
        {
            EnsureValid(handle);    

            int address = _handleToAddress[handle.Id];
            _source.SetAtAddress(address, item);
        }


        public int IndexOf(GapBufferItemAnchor handle)
        {
            if (handle == GapBufferItemAnchor.Empty)
                return -1;
            EnsureValid(handle);
            return _source.AddressToIndex(_handleToAddress[handle.Id]);
        }

        public void Remove(GapBufferItemAnchor handle)
        {
            EnsureValid(handle);
            RemoveAtAddress(_handleToAddress[handle.Id]);
        }

        internal GapBufferItemAnchor AnchorAtAddress(int address)
        {
            ValidateAddress(address);
            if (address >= _addressToHandle.Length || _addressToHandle[address] < 0)
                throw new InvalidOperationException("The element has no anchor.");
            int id = _addressToHandle[address];
            return new GapBufferItemAnchor(id, _generation[id]);
        }

        internal void RemoveAtAddress(int address)
        {
            ValidateAddress(address);
            int id = address < _addressToHandle.Length ? _addressToHandle[address] : -1;
            if (id >= 0)
            {
                // Detach before moving the gap: the removed item must not move its handle.
                _addressToHandle[address] = -1;
                _active[id] = false;
                FreeId(id);
                _count--;
            }
            _source.RemoveAtAddress(address);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValidAnchor(GapBufferItemAnchor handle)
        {
            return handle.Generation > 0
                && handle.Id >= 0
                && handle.Id < _handleToAddress.Length
                && _active[handle.Id]
                && _generation[handle.Id] == handle.Generation;
        }

        private void OnElementsMoved(int fromAddress, int toAddress, int count)
        {
            if (fromAddress == toAddress) return;

            if (fromAddress < toAddress)
            {
                for (int i = count - 1; i >= 0; i--)
                    MoveAddress(fromAddress + i, toAddress + i);
            }
            else
            {
                for (int i = 0; i < count; i++)
                    MoveAddress(fromAddress + i, toAddress + i);
            }
        }

        private void MoveAddress(int oldPos, int newPos)
        {
            EnsurePhysicalCapacity(Math.Max(oldPos, newPos));

            int handleId = _addressToHandle[oldPos];
            _addressToHandle[oldPos] = -1;

            if (handleId != -1)
            {
                _handleToAddress[handleId] = newPos;
                _addressToHandle[newPos] = handleId;
            }
            else
            {
                _addressToHandle[newPos] = -1;
            }
        }

        private int AllocateId()
        {
            if (_freeHead != -1)
            {
                int id = _freeHead;
                _freeHead = _handleToAddress[id];
                return id;
            }

            int oldCapacity = _handleToAddress.Length;
            int newCapacity = oldCapacity * 2;

            Array.Resize(ref _handleToAddress, newCapacity);
            Array.Resize(ref _generation, newCapacity);
            Array.Resize(ref _active, newCapacity);

            for (int i = oldCapacity; i < newCapacity - 1; i++)
                _handleToAddress[i] = i + 1;
            _handleToAddress[newCapacity - 1] = -1;

            _freeHead = oldCapacity;
            int newId = _freeHead;
            _freeHead = _handleToAddress[newId];
            return newId;
        }

        private void FreeId(int id)
        {
            _handleToAddress[id] = _freeHead;
            _freeHead = id;
        }

        private void EnsurePhysicalCapacity(int address)
        {
            if (address >= _addressToHandle.Length)
            {
                int newCapacity = Math.Max(address + 1, _addressToHandle.Length * 2);
                int oldCapacity = _addressToHandle.Length;
                Array.Resize(ref _addressToHandle, newCapacity);
                Array.Fill(_addressToHandle, -1, oldCapacity, newCapacity - oldCapacity);
            }
        }

        private void ValidateAddress(int address)
        {
            int index = _source.AddressToIndex(address);
            if (address < 0 || index < 0 || index >= _source.Count || _source.AddressOf(index) != address)
                throw new ArgumentOutOfRangeException(nameof(address));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureValid(GapBufferItemAnchor handle)
        {
            if (!IsValidAnchor(handle))
                throw new InvalidOperationException("Stale or invalid handle.");
        }

    }
}
