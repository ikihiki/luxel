struct Root { buffer_index: u32, value: u32, pad0: u32, pad1: u32 }
@group(0) @binding(0) var<storage, read_write> arena: array<u32>;
@group(0) @binding(1) var<uniform> root: Root;
@compute @workgroup_size(1)
fn main() { arena[root.buffer_index * 64u] = root.value; }
