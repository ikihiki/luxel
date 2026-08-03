namespace Luxel.Graphics;

/// <summary>Thread-safe fixed-capacity slot allocator with explicit recycling.</summary>
internal sealed class DescriptorSlotAllocator
{
    private readonly object _lock = new();
    private readonly Stack<uint> _free = new();
    private readonly bool[] _allocated;
    private uint _next;

    public DescriptorSlotAllocator(uint capacity)
    {
        if (capacity == 0 || capacity > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        Capacity = capacity;
        _allocated = new bool[capacity];
    }

    public uint Capacity { get; }

    public uint Allocate()
    {
        lock (_lock)
        {
            uint slot;
            if (_free.Count > 0)
            {
                slot = _free.Pop();
            }
            else
            {
                if (_next >= Capacity)
                    throw new InvalidOperationException($"Descriptor slot capacity ({Capacity}) was exhausted.");
                slot = _next++;
            }

            _allocated[slot] = true;
            return slot;
        }
    }

    public void Free(uint slot)
    {
        lock (_lock)
        {
            if (slot >= Capacity)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (!_allocated[slot])
                throw new InvalidOperationException($"Descriptor slot {slot} is not allocated.");

            _allocated[slot] = false;
            _free.Push(slot);
        }
    }
}
