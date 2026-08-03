namespace Luxel.Tests;

public sealed class DescriptorSlotAllocatorTests
{
    [Fact]
    public void Allocate_ThrowsDeterministicallyAtCapacity()
    {
        var allocator = new DescriptorSlotAllocator(2);

        Assert.Equal(0u, allocator.Allocate());
        Assert.Equal(1u, allocator.Allocate());

        var error = Assert.Throws<InvalidOperationException>(() => allocator.Allocate());
        Assert.Equal("Descriptor slot capacity (2) was exhausted.", error.Message);
    }

    [Fact]
    public void Free_MakesSlotReusableWithoutRecyclingLiveSlots()
    {
        var allocator = new DescriptorSlotAllocator(3);
        uint first = allocator.Allocate();
        uint second = allocator.Allocate();
        uint third = allocator.Allocate();

        allocator.Free(second);

        Assert.Equal(second, allocator.Allocate());
        Assert.Throws<InvalidOperationException>(() => allocator.Allocate());
        Assert.NotEqual(first, second);
        Assert.NotEqual(third, second);
    }

    [Fact]
    public void Free_RejectsDoubleReturn()
    {
        var allocator = new DescriptorSlotAllocator(1);
        uint slot = allocator.Allocate();

        allocator.Free(slot);

        Assert.Throws<InvalidOperationException>(() => allocator.Free(slot));
    }
}
