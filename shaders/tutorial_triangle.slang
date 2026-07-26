// Learn/Rendering/FirstTriangle: vertex pulling shared by Vulkan (SPIR-V) and D3D12 (DXIL).

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct DrawArgs { uint vertexBufferIndex; };
[[vk::push_constant]] DrawArgs g_args;

struct Vertex
{
    float4 position;
    float4 color;
};

struct VSOut
{
    float4 position : SV_Position;
    float4 color : COLOR0;
};

[shader("vertex")]
VSOut vsMain(uint vertexId : SV_VertexID)
{
    Vertex vertex = g_buffers[g_args.vertexBufferIndex].Load<Vertex>(vertexId * 32);
    VSOut output;
    output.position = vertex.position;
    output.color = vertex.color;
    return output;
}

[shader("pixel")]
float4 psMain(VSOut input) : SV_Target
{
    return input.color;
}
