using Luxel.Graphics.Abstraction;
using Luxel.Graphics.Vulkan.Interop;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Luxel.Graphics.Vulkan;

/// <summary>
/// Vulkan 1.3 バックエンド。「No Graphics API」設計の薄いラッパー。
/// 全パイプライン共通の固定レイアウト:
///   set 0 / binding 0 = bindless storage buffer 配列 (descriptor-indexing)
///   push 定数 (192B)  = ルート引数
/// シェーダは <c>g_buffers[index]</c> でバッファを参照する (D3D12 と同一の index モデル)。
/// </summary>
public sealed unsafe class VulkanBackend : IGpuBackend
{
    private const uint BindlessCapacity = 65536;
    private const uint PushConstantSize = 192;  // shadow map など mat4×2 を渡す用途で 128B では不足。
                                                // Vulkan の spec min は 128B、最新 GPU は 256B 対応 (RTX 4080 SUPER 等で確認済)。

    private readonly Vk _vk;
    private Instance _instance;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private uint _queueFamily;
    private PhysicalDeviceMemoryProperties _memProps;

    private Queue _queue;
    private DescriptorSetLayout _setLayout;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _descriptorSet;
    private PipelineLayout _pipelineLayout;
    private int _nextBindless = -1;        // binding 0: storage buffer (Interlocked.Increment で採番)
    private int _nextSampledImage = -1;    // binding 1: sampled image
    private int _nextSampler = -1;         // binding 2: sampler
    private readonly object _queueLock = new();   // queue submit/wait の直列化 (OneShotSubmit と MainQueue で共有)
    private readonly object _descLock = new();     // vkUpdateDescriptorSets (同一 set はホスト同期必須) の直列化

    private ExtDebugUtils? _debugUtils;
    private DebugUtilsMessengerEXT _messenger;
    private bool _disposed;
    private VulkanPresentationMode _presentation;
    private IVulkanWindowSurface? _windowSurfaceProvider;
    private KhrSurface? _bootstrapKhrSurface;
    private SurfaceKHR _bootstrapSurface;

    private VulkanBackend(Vk vk) => _vk = vk;

    public string Name { get; private set; } = "Vulkan";
    public GpuBackendKind Kind => GpuBackendKind.Vulkan;
    public IGpuBackendQueue MainQueue { get; private set; } = null!;
    public bool SupportsPresentation => _presentation != VulkanPresentationMode.Disabled;

    public static VulkanBackend Create(bool enableValidation = true)
        => Create(new VulkanBackendOptions { EnableValidation = enableValidation });

    public static VulkanBackend Create(VulkanBackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var backend = new VulkanBackend(Vk.GetApi());
        try
        {
            backend.Initialize(options);
            return backend;
        }
        catch
        {
            backend.Dispose();
            throw;
        }
    }

    private void Initialize(VulkanBackendOptions options)
    {
        _presentation = ResolvePresentationMode(options.Presentation);
        _windowSurfaceProvider = options.WindowSurface;
        if (_presentation == VulkanPresentationMode.Window && _windowSurfaceProvider is null)
            throw new ArgumentException("Window presentation requires VulkanBackendOptions.WindowSurface.", nameof(options));
        if (_presentation != VulkanPresentationMode.Window && _windowSurfaceProvider is not null)
            throw new ArgumentException("WindowSurface can only be supplied with VulkanPresentationMode.Window.", nameof(options));

        bool useValidation = options.EnableValidation && IsLayerAvailable("VK_LAYER_KHRONOS_validation");
        bool useDebugUtils = useValidation && IsInstanceExtensionAvailable(ExtDebugUtils.ExtensionName);

        ValidatePresentationSupport();
        CreateInstance(useValidation, useDebugUtils);
        if (useDebugUtils) SetupDebugMessenger();
        if (_presentation == VulkanPresentationMode.Window) CreateBootstrapSurface();
        PickPhysicalDevice();
        CreateDevice();
        CreateBindlessLayout();

        _vk.GetDeviceQueue(_device, _queueFamily, 0, out _queue);
        MainQueue = new VulkanQueue(_vk, _device, _queue, _queueFamily, _pipelineLayout, _descriptorSet, _queueLock);
    }

    // ---- Instance ------------------------------------------------------------

    private void CreateInstance(bool useValidation, bool useDebugUtils)
    {
        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)SilkMarshal.StringToPtr("Luxel"),
            ApplicationVersion = new Version32(1, 0, 0),
            PEngineName = (byte*)SilkMarshal.StringToPtr("Luxel"),
            EngineVersion = new Version32(1, 0, 0),
            ApiVersion = Vk.Version13,
        };

        var layers = new List<string>();
        if (useValidation) layers.Add("VK_LAYER_KHRONOS_validation");
        var extensions = new List<string>();
        if (useDebugUtils) extensions.Add(ExtDebugUtils.ExtensionName);
        if (_presentation == VulkanPresentationMode.Win32)
        {
            extensions.Add(KhrSurface.ExtensionName);
            extensions.Add(KhrWin32Surface.ExtensionName);
        }
        else if (_presentation == VulkanPresentationMode.Window)
        {
            extensions.AddRange(_windowSurfaceProvider!.RequiredInstanceExtensions);
        }
        extensions = extensions.Distinct(StringComparer.Ordinal).ToList();

        var createInfo = new InstanceCreateInfo
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledLayerCount = (uint)layers.Count,
            PpEnabledLayerNames = layers.Count > 0 ? (byte**)SilkMarshal.StringArrayToPtr(layers) : null,
            EnabledExtensionCount = (uint)extensions.Count,
            PpEnabledExtensionNames = extensions.Count > 0 ? (byte**)SilkMarshal.StringArrayToPtr(extensions) : null,
        };

        VkCheck.Ok(_vk.CreateInstance(in createInfo, null, out _instance), "vkCreateInstance");

        SilkMarshal.Free((nint)appInfo.PApplicationName);
        SilkMarshal.Free((nint)appInfo.PEngineName);
        if (createInfo.PpEnabledLayerNames != null) SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
        if (createInfo.PpEnabledExtensionNames != null) SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);
    }

    private void CreateBootstrapSurface()
    {
        if (!_vk.TryGetInstanceExtension(_instance, out KhrSurface khrSurface))
            throw new VulkanException("Failed to load VK_KHR_surface for window presentation.");
        _bootstrapKhrSurface = khrSurface;
        ulong handle = _windowSurfaceProvider!.CreateSurface(_instance.Handle);
        if (handle == 0) throw new VulkanException("The window surface provider returned a null VkSurfaceKHR.");
        _bootstrapSurface = new SurfaceKHR(handle);
    }

    private void SetupDebugMessenger()
    {
        if (!_vk.TryGetInstanceExtension(_instance, out ExtDebugUtils debugUtils)) return;
        _debugUtils = debugUtils;
        var info = new DebugUtilsMessengerCreateInfoEXT
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.WarningBitExt
                            | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
            MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt
                        | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt
                        | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
            PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback,
        };
        _debugUtils.CreateDebugUtilsMessenger(_instance, in info, null, out _messenger);
    }

    private static uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT severity,
        DebugUtilsMessageTypeFlagsEXT type, DebugUtilsMessengerCallbackDataEXT* data, void* user)
    {
        string msg = SilkMarshal.PtrToString((nint)data->PMessage) ?? "";
        Console.Error.WriteLine($"[VK {severity}] {msg}");
        return Vk.False;
    }

    // ---- Physical device -----------------------------------------------------

    private void PickPhysicalDevice()
    {
        uint count = 0;
        VkCheck.Ok(_vk.EnumeratePhysicalDevices(_instance, ref count, null), "vkEnumeratePhysicalDevices");
        if (count == 0) throw new VulkanException("Vulkan 対応の物理デバイスが見つかりません。");
        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* p = devices)
            VkCheck.Ok(_vk.EnumeratePhysicalDevices(_instance, ref count, p), "vkEnumeratePhysicalDevices");

        PhysicalDevice chosen = default;
        int bestScore = -1;
        foreach (var dev in devices)
        {
            if (_presentation != VulkanPresentationMode.Disabled &&
                !IsDeviceExtensionAvailable(dev, KhrSwapchain.ExtensionName)) continue;
            if (!TryFindGraphicsComputeQueue(dev, out _)) continue;
            _vk.GetPhysicalDeviceProperties(dev, out var props);
            int score = props.DeviceType == PhysicalDeviceType.DiscreteGpu ? 1000 : 100;
            if (score > bestScore) { bestScore = score; chosen = dev; }
        }
        if (bestScore < 0) throw new VulkanException("graphics+compute キューを持つデバイスがありません。");

        _physicalDevice = chosen;
        TryFindGraphicsComputeQueue(chosen, out _queueFamily);
        _vk.GetPhysicalDeviceMemoryProperties(chosen, out _memProps);

        _vk.GetPhysicalDeviceProperties(chosen, out var chosenProps);
        Name = $"Vulkan / {SilkMarshal.PtrToString((nint)chosenProps.DeviceName)}";
    }

    private bool TryFindGraphicsComputeQueue(PhysicalDevice dev, out uint family)
    {
        family = 0;
        uint count = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(dev, ref count, null);
        var props = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* p = props)
            _vk.GetPhysicalDeviceQueueFamilyProperties(dev, ref count, p);
        for (uint i = 0; i < count; i++)
        {
            var flags = props[i].QueueFlags;
            if ((flags & QueueFlags.GraphicsBit) == 0 || (flags & QueueFlags.ComputeBit) == 0) continue;
            if (_presentation == VulkanPresentationMode.Window)
            {
                VkCheck.Ok(_bootstrapKhrSurface!.GetPhysicalDeviceSurfaceSupport(dev, i, _bootstrapSurface, out Bool32 supported),
                    "vkGetPhysicalDeviceSurfaceSupportKHR");
                if (!supported) continue;
            }
            family = i;
            return true;
        }
        return false;
    }

    // ---- Logical device ------------------------------------------------------

    private void CreateDevice()
    {
        float priority = 1.0f;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = _queueFamily,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };

        var features13 = new PhysicalDeviceVulkan13Features
        {
            SType = StructureType.PhysicalDeviceVulkan13Features,
            DynamicRendering = true,
            Synchronization2 = true,
        };
        var features12 = new PhysicalDeviceVulkan12Features
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            BufferDeviceAddress = true,
            DescriptorIndexing = true,
            RuntimeDescriptorArray = true,
            DescriptorBindingPartiallyBound = true,
            DescriptorBindingVariableDescriptorCount = true,
            ShaderSampledImageArrayNonUniformIndexing = true,
            ShaderStorageImageArrayNonUniformIndexing = true,
            ShaderStorageBufferArrayNonUniformIndexing = true,
            DescriptorBindingSampledImageUpdateAfterBind = true,
            DescriptorBindingStorageImageUpdateAfterBind = true,
            DescriptorBindingStorageBufferUpdateAfterBind = true,
            PNext = &features13,
        };
        var features2 = new PhysicalDeviceFeatures2
        {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &features12,
        };

        string[] deviceExtensions = _presentation == VulkanPresentationMode.Disabled
            ? []
            : [KhrSwapchain.ExtensionName];
        var extensionNames = deviceExtensions.Length > 0
            ? (byte**)SilkMarshal.StringArrayToPtr(deviceExtensions)
            : null;
        var deviceInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            PNext = &features2,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueInfo,
            EnabledExtensionCount = (uint)deviceExtensions.Length,
            PpEnabledExtensionNames = extensionNames,
        };

        try
        {
            VkCheck.Ok(_vk.CreateDevice(_physicalDevice, in deviceInfo, null, out _device), "vkCreateDevice");
        }
        finally
        {
            if (extensionNames != null) SilkMarshal.Free((nint)extensionNames);
        }
    }

    public IGpuBackendSurface CreateSurface(nint windowHandle, uint width, uint height)
    {
        if (_presentation == VulkanPresentationMode.Disabled)
            throw new InvalidOperationException(
                "この Vulkan backend は headless mode で作成されているため presentation surface を作成できません。");
        if (windowHandle == 0) throw new ArgumentException("A non-zero native window handle is required.", nameof(windowHandle));

        if (_presentation == VulkanPresentationMode.Window)
        {
            if (_bootstrapSurface.Handle == 0)
                throw new InvalidOperationException("This Vulkan backend already transferred its window surface; only one surface is supported.");
            SurfaceKHR surface = _bootstrapSurface;
            _bootstrapSurface = default;
            return VulkanSurface.FromExisting(
                _vk, _instance, _physicalDevice, _device, _queue, _queueFamily, surface, width, height);
        }

        return VulkanSurface.FromWin32(
            _vk, _instance, _physicalDevice, _device, _queue, _queueFamily, windowHandle, width, height);
    }

    // ---- 固定 bindless レイアウト --------------------------------------------

    private void CreateBindlessLayout()
    {
        // set 0: binding 0 = storage buffer 配列, binding 1 = sampled image 配列, binding 2 = sampler 配列。
        const uint samplerCapacity = 1024;
        var bindings = stackalloc DescriptorSetLayoutBinding[3]
        {
            new() { Binding = 0, DescriptorType = DescriptorType.StorageBuffer, DescriptorCount = BindlessCapacity, StageFlags = ShaderStageFlags.All },
            new() { Binding = 1, DescriptorType = DescriptorType.SampledImage, DescriptorCount = BindlessCapacity, StageFlags = ShaderStageFlags.All },
            new() { Binding = 2, DescriptorType = DescriptorType.Sampler, DescriptorCount = samplerCapacity, StageFlags = ShaderStageFlags.All },
        };
        var flags = DescriptorBindingFlags.PartiallyBoundBit | DescriptorBindingFlags.UpdateAfterBindBit;
        var bindingFlags = stackalloc DescriptorBindingFlags[3] { flags, flags, flags };
        var flagsInfo = new DescriptorSetLayoutBindingFlagsCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = 3,
            PBindingFlags = bindingFlags,
        };
        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 3,
            PBindings = bindings,
            Flags = DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit,
            PNext = &flagsInfo,
        };
        VkCheck.Ok(_vk.CreateDescriptorSetLayout(_device, in layoutInfo, null, out _setLayout),
            "vkCreateDescriptorSetLayout");

        var poolSizes = stackalloc DescriptorPoolSize[3]
        {
            new() { Type = DescriptorType.StorageBuffer, DescriptorCount = BindlessCapacity },
            new() { Type = DescriptorType.SampledImage, DescriptorCount = BindlessCapacity },
            new() { Type = DescriptorType.Sampler, DescriptorCount = samplerCapacity },
        };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 3,
            PPoolSizes = poolSizes,
            Flags = DescriptorPoolCreateFlags.UpdateAfterBindBit,
        };
        VkCheck.Ok(_vk.CreateDescriptorPool(_device, in poolInfo, null, out _descriptorPool),
            "vkCreateDescriptorPool");

        var setLayout = _setLayout;
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout,
        };
        VkCheck.Ok(_vk.AllocateDescriptorSets(_device, in allocInfo, out _descriptorSet),
            "vkAllocateDescriptorSets");

        var range = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.All,
            Offset = 0,
            Size = PushConstantSize,
        };
        var pipelineLayoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &range,
        };
        VkCheck.Ok(_vk.CreatePipelineLayout(_device, in pipelineLayoutInfo, null, out _pipelineLayout),
            "vkCreatePipelineLayout");
    }

    // ---- gpuMalloc -----------------------------------------------------------

    public IGpuBackendBuffer CreateBuffer(ulong size, GpuMemoryKind kind)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.ShaderDeviceAddressBit
                  | BufferUsageFlags.StorageBufferBit
                  | BufferUsageFlags.TransferSrcBit
                  | BufferUsageFlags.TransferDstBit
                  | BufferUsageFlags.IndirectBufferBit,
            SharingMode = SharingMode.Exclusive,
        };
        VkCheck.Ok(_vk.CreateBuffer(_device, in bufferInfo, null, out var buffer), "vkCreateBuffer");

        _vk.GetBufferMemoryRequirements(_device, buffer, out var req);
        uint typeIndex = SelectMemoryType(req.MemoryTypeBits, kind, out bool hostVisible);

        var flagsInfo = new MemoryAllocateFlagsInfo
        {
            SType = StructureType.MemoryAllocateFlagsInfo,
            Flags = MemoryAllocateFlags.DeviceAddressBit,
        };
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = typeIndex,
            PNext = &flagsInfo,
        };
        VkCheck.Ok(_vk.AllocateMemory(_device, in allocInfo, null, out var memory), "vkAllocateMemory");
        VkCheck.Ok(_vk.BindBufferMemory(_device, buffer, memory, 0), "vkBindBufferMemory");

        void* mapped = null;
        if (hostVisible)
            VkCheck.Ok(_vk.MapMemory(_device, memory, 0, size, 0, ref mapped), "vkMapMemory");

        var addrInfo = new BufferDeviceAddressInfo
        {
            SType = StructureType.BufferDeviceAddressInfo,
            Buffer = buffer,
        };
        ulong address = _vk.GetBufferDeviceAddress(_device, in addrInfo);

        uint index = (uint)Interlocked.Increment(ref _nextBindless);
        WriteBindlessDescriptor(buffer, size, index);

        return new VulkanBuffer(_vk, _device, buffer, memory, size, address, index, mapped);
    }

    private void WriteBindlessDescriptor(Silk.NET.Vulkan.Buffer buffer, ulong size, uint index)
    {
        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = buffer,
            Offset = 0,
            Range = Vk.WholeSize,
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = 0,
            DstArrayElement = index,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageBuffer,
            PBufferInfo = &bufferInfo,
        };
        lock (_descLock) _vk.UpdateDescriptorSets(_device, 1, in write, 0, null);
    }

    private uint SelectMemoryType(uint typeBits, GpuMemoryKind kind, out bool hostVisible)
    {
        (MemoryPropertyFlags required, MemoryPropertyFlags preferred) = kind switch
        {
            GpuMemoryKind.HostMapped => (
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                MemoryPropertyFlags.DeviceLocalBit),
            GpuMemoryKind.DeviceLocal => (
                MemoryPropertyFlags.DeviceLocalBit, (MemoryPropertyFlags)0),
            GpuMemoryKind.HostCached => (
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCachedBit,
                (MemoryPropertyFlags)0),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        int idx = FindMemoryType(typeBits, required | preferred);
        if (idx < 0) idx = FindMemoryType(typeBits, required);
        if (idx < 0) throw new VulkanException($"適合するメモリタイプがありません (kind={kind})。");

        hostVisible = (_memProps.MemoryTypes[idx].PropertyFlags & MemoryPropertyFlags.HostVisibleBit) != 0;
        return (uint)idx;
    }

    private int FindMemoryType(uint typeBits, MemoryPropertyFlags flags)
    {
        for (int i = 0; i < _memProps.MemoryTypeCount; i++)
        {
            bool typeOk = (typeBits & (1u << i)) != 0;
            bool propsOk = (_memProps.MemoryTypes[i].PropertyFlags & flags) == flags;
            if (typeOk && propsOk) return i;
        }
        return -1;
    }

    // ---- Pipelines -----------------------------------------------------------

    public IGpuBackendPipeline CreateComputePipeline(ReadOnlySpan<byte> shaderBlob, string entryPoint)
    {
        ShaderModule module;
        fixed (byte* code = shaderBlob)
        {
            var moduleInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)shaderBlob.Length,
                PCode = (uint*)code,
            };
            VkCheck.Ok(_vk.CreateShaderModule(_device, in moduleInfo, null, out module), "vkCreateShaderModule");
        }

        nint namePtr = SilkMarshal.StringToPtr(entryPoint);
        try
        {
            var stage = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ComputeBit,
                Module = module,
                PName = (byte*)namePtr,
            };
            var createInfo = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = stage,
                Layout = _pipelineLayout,
            };
            VkCheck.Ok(_vk.CreateComputePipelines(_device, default, 1, in createInfo, null, out var pipeline),
                "vkCreateComputePipelines");
            return new VulkanPipeline(_vk, _device, pipeline, isCompute: true);
        }
        finally
        {
            SilkMarshal.Free(namePtr);
            _vk.DestroyShaderModule(_device, module, null);
        }
    }

    public IGpuBackendPipeline CreateGraphicsPipeline(
        ReadOnlySpan<byte> vsBlob, string vsEntry,
        ReadOnlySpan<byte> psBlob, string psEntry,
        GpuRasterDesc raster)
    {
        // Vulkan は vs/ps が同一 SPIR-V モジュール内の別エントリでよい。
        ShaderModule module;
        fixed (byte* code = vsBlob)
        {
            var moduleInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)vsBlob.Length,
                PCode = (uint*)code,
            };
            VkCheck.Ok(_vk.CreateShaderModule(_device, in moduleInfo, null, out module), "vkCreateShaderModule");
        }

        nint vsName = SilkMarshal.StringToPtr(vsEntry);
        nint psName = SilkMarshal.StringToPtr(psEntry);
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = module,
                PName = (byte*)vsName,
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = module,
                PName = (byte*)psName,
            };

            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
            };
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = raster.Topology == GpuPrimitiveTopology.TriangleStrip
                    ? PrimitiveTopology.TriangleStrip : PrimitiveTopology.TriangleList,
            };
            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1,
            };
            var rasterState = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = raster.CullMode switch
                {
                    GpuCullMode.Front => CullModeFlags.FrontBit,
                    GpuCullMode.Back => CullModeFlags.BackBit,
                    _ => CullModeFlags.None,
                },
                FrontFace = raster.FrontFace == GpuFrontFace.Clockwise
                    ? FrontFace.Clockwise : FrontFace.CounterClockwise,
                LineWidth = 1.0f,
            };
            var multisample = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
            };
            bool blend = raster.Blend == GpuBlendMode.AlphaBlend;
            var blendAttachment = new PipelineColorBlendAttachmentState
            {
                BlendEnable = blend,
                SrcColorBlendFactor = blend ? BlendFactor.SrcAlpha : BlendFactor.One,
                DstColorBlendFactor = blend ? BlendFactor.OneMinusSrcAlpha : BlendFactor.Zero,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = blend ? BlendFactor.OneMinusSrcAlpha : BlendFactor.Zero,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit
                               | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            };
            var blendState = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &blendAttachment,
            };
            var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicState = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates,
            };

            var depthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = raster.DepthTest,
                DepthWriteEnable = raster.DepthWrite,
                DepthCompareOp = CompareOp.LessOrEqual,
            };

            Format colorFormat = ToVkFormat(raster.ColorFormat);
            var renderingInfo = new PipelineRenderingCreateInfo
            {
                SType = StructureType.PipelineRenderingCreateInfo,
                ColorAttachmentCount = 1,
                PColorAttachmentFormats = &colorFormat,
                DepthAttachmentFormat = raster.DepthTest ? ToVkFormat(raster.DepthFormat) : Format.Undefined,
            };

            var createInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                PNext = &renderingInfo,
                StageCount = 2,
                PStages = stages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterState,
                PMultisampleState = &multisample,
                PDepthStencilState = &depthStencil,
                PColorBlendState = &blendState,
                PDynamicState = &dynamicState,
                Layout = _pipelineLayout,
            };
            VkCheck.Ok(_vk.CreateGraphicsPipelines(_device, default, 1, in createInfo, null, out var pipeline),
                "vkCreateGraphicsPipelines");
            return new VulkanPipeline(_vk, _device, pipeline, isCompute: false);
        }
        finally
        {
            SilkMarshal.Free(vsName);
            SilkMarshal.Free(psName);
            _vk.DestroyShaderModule(_device, module, null);
        }
    }

    public IGpuBackendTexture CreateRenderTarget(uint width, uint height, GpuFormat format)
    {
        Format vkFormat = ToVkFormat(format);
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = vkFormat,
            Extent = new Extent3D(width, height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit
                  | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        VkCheck.Ok(_vk.CreateImage(_device, in imageInfo, null, out var image), "vkCreateImage");

        _vk.GetImageMemoryRequirements(_device, image, out var req);
        int typeIdx = FindMemoryType(req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit);
        if (typeIdx < 0) throw new VulkanException("レンダーターゲット用 DEVICE_LOCAL メモリがありません。");
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = (uint)typeIdx,
        };
        VkCheck.Ok(_vk.AllocateMemory(_device, in allocInfo, null, out var memory), "vkAllocateMemory");
        VkCheck.Ok(_vk.BindImageMemory(_device, image, memory, 0), "vkBindImageMemory");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = vkFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        VkCheck.Ok(_vk.CreateImageView(_device, in viewInfo, null, out var view), "vkCreateImageView");

        return new VulkanTexture(_vk, _device, image, memory, view, width, height, format, vkFormat);
    }

    public IGpuBackendTexture CreateDepthTarget(uint width, uint height, GpuFormat format)
    {
        Format vkFormat = ToVkFormat(format);
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = vkFormat,
            Extent = new Extent3D(width, height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        VkCheck.Ok(_vk.CreateImage(_device, in imageInfo, null, out var image), "vkCreateImage(depth)");
        _vk.GetImageMemoryRequirements(_device, image, out var req);
        int typeIdx = FindMemoryType(req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = (uint)typeIdx,
        };
        VkCheck.Ok(_vk.AllocateMemory(_device, in allocInfo, null, out var memory), "vkAllocateMemory(depth)");
        VkCheck.Ok(_vk.BindImageMemory(_device, image, memory, 0), "vkBindImageMemory(depth)");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = vkFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1),
        };
        VkCheck.Ok(_vk.CreateImageView(_device, in viewInfo, null, out var view), "vkCreateImageView(depth)");

        return new VulkanTexture(_vk, _device, image, memory, view, width, height, format, vkFormat,
            0, ImageLayout.Undefined, ImageAspectFlags.DepthBit);
    }

    public IGpuBackendTexture CreateSampledTexture(uint width, uint height, GpuFormat format, ReadOnlySpan<byte> data)
    {
        Format vkFormat = ToVkFormat(format);
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = vkFormat,
            Extent = new Extent3D(width, height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        VkCheck.Ok(_vk.CreateImage(_device, in imageInfo, null, out var image), "vkCreateImage");
        _vk.GetImageMemoryRequirements(_device, image, out var req);
        int typeIdx = FindMemoryType(req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = (uint)typeIdx,
        };
        VkCheck.Ok(_vk.AllocateMemory(_device, in allocInfo, null, out var memory), "vkAllocateMemory");
        VkCheck.Ok(_vk.BindImageMemory(_device, image, memory, 0), "vkBindImageMemory");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = vkFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        VkCheck.Ok(_vk.CreateImageView(_device, in viewInfo, null, out var view), "vkCreateImageView");

        // staging へアップロードして image にコピー。
        (Silk.NET.Vulkan.Buffer staging, DeviceMemory stagingMem) = CreateStaging(data);
        OneShotSubmit(cb =>
        {
            RecordImageBarrier(cb, image, ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
                PipelineStageFlags2.TopOfPipeBit, 0,
                PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit);
            var region = new BufferImageCopy
            {
                BufferOffset = 0,
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D(width, height, 1),
            };
            _vk.CmdCopyBufferToImage(cb, staging, image, ImageLayout.TransferDstOptimal, 1, in region);
            RecordImageBarrier(cb, image, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit,
                PipelineStageFlags2.FragmentShaderBit | PipelineStageFlags2.ComputeShaderBit,
                AccessFlags2.ShaderReadBit);
        });
        _vk.DestroyBuffer(_device, staging, null);
        _vk.FreeMemory(_device, stagingMem, null);

        uint index = (uint)Interlocked.Increment(ref _nextSampledImage);
        var descImage = new DescriptorImageInfo { ImageView = view, ImageLayout = ImageLayout.ShaderReadOnlyOptimal };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = 1,
            DstArrayElement = index,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.SampledImage,
            PImageInfo = &descImage,
        };
        lock (_descLock) _vk.UpdateDescriptorSets(_device, 1, in write, 0, null);

        return new VulkanTexture(_vk, _device, image, memory, view, width, height, format, vkFormat,
            index, ImageLayout.ShaderReadOnlyOptimal);
    }

    public IGpuBackendSampler CreateSampler(GpuSamplerFilter filter, GpuSamplerAddress address = GpuSamplerAddress.Clamp)
    {
        Filter f = filter == GpuSamplerFilter.Linear ? Filter.Linear : Filter.Nearest;
        SamplerAddressMode addr = address == GpuSamplerAddress.Repeat
            ? SamplerAddressMode.Repeat : SamplerAddressMode.ClampToEdge;
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = f,
            MinFilter = f,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = addr,
            AddressModeV = addr,
            AddressModeW = addr,
            MaxLod = 1.0f,
        };
        VkCheck.Ok(_vk.CreateSampler(_device, in info, null, out var sampler), "vkCreateSampler");

        uint index = (uint)Interlocked.Increment(ref _nextSampler);
        var descSampler = new DescriptorImageInfo { Sampler = sampler };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = 2,
            DstArrayElement = index,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.Sampler,
            PImageInfo = &descSampler,
        };
        lock (_descLock) _vk.UpdateDescriptorSets(_device, 1, in write, 0, null);

        return new VulkanSampler(_vk, _device, sampler, index);
    }

    private (Silk.NET.Vulkan.Buffer, DeviceMemory) CreateStaging(ReadOnlySpan<byte> data)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = (ulong)data.Length,
            Usage = BufferUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
        };
        VkCheck.Ok(_vk.CreateBuffer(_device, in bufferInfo, null, out var buffer), "vkCreateBuffer(staging)");
        _vk.GetBufferMemoryRequirements(_device, buffer, out var req);
        int typeIdx = FindMemoryType(req.MemoryTypeBits,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = (uint)typeIdx,
        };
        VkCheck.Ok(_vk.AllocateMemory(_device, in allocInfo, null, out var memory), "vkAllocateMemory(staging)");
        VkCheck.Ok(_vk.BindBufferMemory(_device, buffer, memory, 0), "vkBindBufferMemory(staging)");
        void* mapped = null;
        VkCheck.Ok(_vk.MapMemory(_device, memory, 0, (ulong)data.Length, 0, ref mapped), "vkMapMemory(staging)");
        data.CopyTo(new Span<byte>(mapped, data.Length));
        _vk.UnmapMemory(_device, memory);
        return (buffer, memory);
    }

    private void OneShotSubmit(Action<CommandBuffer> record)
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _queueFamily,
            Flags = CommandPoolCreateFlags.TransientBit,
        };
        VkCheck.Ok(_vk.CreateCommandPool(_device, in poolInfo, null, out var pool), "vkCreateCommandPool(oneshot)");
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        VkCheck.Ok(_vk.AllocateCommandBuffers(_device, in allocInfo, out CommandBuffer cb), "vkAllocateCommandBuffers(oneshot)");
        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        VkCheck.Ok(_vk.BeginCommandBuffer(cb, in begin), "vkBeginCommandBuffer(oneshot)");
        record(cb);
        VkCheck.Ok(_vk.EndCommandBuffer(cb), "vkEndCommandBuffer(oneshot)");
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cb,
        };
        // per-call フェンスにし submit だけロック → GPU 完了待ちはロック外 (並列アップロード維持)。
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        VkCheck.Ok(_vk.CreateFence(_device, in fenceInfo, null, out Fence fence), "vkCreateFence(oneshot)");
        lock (_queueLock)
            VkCheck.Ok(_vk.QueueSubmit(_queue, 1, in submit, fence), "vkQueueSubmit(oneshot)");
        VkCheck.Ok(_vk.WaitForFences(_device, 1, in fence, true, ulong.MaxValue), "vkWaitForFences(oneshot)");
        _vk.DestroyFence(_device, fence, null);
        _vk.DestroyCommandPool(_device, pool, null);
    }

    private void RecordImageBarrier(CommandBuffer cb, Image image, ImageLayout oldLayout, ImageLayout newLayout,
        PipelineStageFlags2 srcStage, AccessFlags2 srcAccess, PipelineStageFlags2 dstStage, AccessFlags2 dstAccess)
    {
        var barrier = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = srcStage,
            SrcAccessMask = srcAccess,
            DstStageMask = dstStage,
            DstAccessMask = dstAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        var dep = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier,
        };
        _vk.CmdPipelineBarrier2(cb, in dep);
    }

    internal static Format ToVkFormat(GpuFormat format) => format switch
    {
        GpuFormat.Rgba8Unorm => Format.R8G8B8A8Unorm,
        GpuFormat.Bgra8Unorm => Format.B8G8R8A8Unorm,
        GpuFormat.R32Float => Format.R32Sfloat,
        GpuFormat.D32Float => Format.D32Sfloat,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    // ---- availability helpers ------------------------------------------------

    private static VulkanPresentationMode ResolvePresentationMode(VulkanPresentationMode mode)
        => mode switch
        {
            VulkanPresentationMode.Auto => OperatingSystem.IsWindows()
                ? VulkanPresentationMode.Win32
                : VulkanPresentationMode.Disabled,
            VulkanPresentationMode.Win32 when !OperatingSystem.IsWindows() =>
                throw new PlatformNotSupportedException("Win32 Vulkan presentation は Windows でのみ利用できます。"),
            _ => mode,
        };

    private void ValidatePresentationSupport()
    {
        IEnumerable<string> required = _presentation switch
        {
            VulkanPresentationMode.Win32 => [KhrSurface.ExtensionName, KhrWin32Surface.ExtensionName],
            VulkanPresentationMode.Window => _windowSurfaceProvider!.RequiredInstanceExtensions,
            _ => [],
        };
        foreach (string extension in required.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(extension))
                throw new VulkanException("The window surface provider returned an empty Vulkan instance extension name.");
            if (!IsInstanceExtensionAvailable(extension))
                throw new VulkanException($"Required Vulkan instance extension is unavailable: {extension}");
        }
    }

    private bool IsLayerAvailable(string name)
    {
        uint count = 0;
        _vk.EnumerateInstanceLayerProperties(ref count, null);
        var props = new LayerProperties[count];
        fixed (LayerProperties* p = props)
            _vk.EnumerateInstanceLayerProperties(ref count, p);
        for (int i = 0; i < props.Length; i++)
        {
            fixed (byte* n = props[i].LayerName)
                if (SilkMarshal.PtrToString((nint)n) == name) return true;
        }
        return false;
    }

    private bool IsInstanceExtensionAvailable(string name)
    {
        uint count = 0;
        _vk.EnumerateInstanceExtensionProperties((byte*)null, ref count, null);
        var props = new ExtensionProperties[count];
        fixed (ExtensionProperties* p = props)
            _vk.EnumerateInstanceExtensionProperties((byte*)null, ref count, p);
        for (int i = 0; i < props.Length; i++)
        {
            fixed (byte* n = props[i].ExtensionName)
                if (SilkMarshal.PtrToString((nint)n) == name) return true;
        }
        return false;
    }

    private bool IsDeviceExtensionAvailable(PhysicalDevice device, string name)
    {
        uint count = 0;
        VkCheck.Ok(_vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref count, null),
            "vkEnumerateDeviceExtensionProperties(count)");
        var properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* values = properties)
            VkCheck.Ok(_vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref count, values),
                "vkEnumerateDeviceExtensionProperties(properties)");
        for (int i = 0; i < properties.Length; i++)
        {
            fixed (byte* extensionName = properties[i].ExtensionName)
            {
                if (SilkMarshal.PtrToString((nint)extensionName) == name) return true;
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_pipelineLayout.Handle != 0) _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
        if (_descriptorPool.Handle != 0) _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
        if (_setLayout.Handle != 0) _vk.DestroyDescriptorSetLayout(_device, _setLayout, null);
        if (_device.Handle != 0) _vk.DestroyDevice(_device, null);
        if (_bootstrapSurface.Handle != 0 && _bootstrapKhrSurface is not null)
            _bootstrapKhrSurface.DestroySurface(_instance, _bootstrapSurface, null);
        _bootstrapSurface = default;
        _bootstrapKhrSurface?.Dispose();
        _bootstrapKhrSurface = null;
        if (_debugUtils is not null && _messenger.Handle != 0)
            _debugUtils.DestroyDebugUtilsMessenger(_instance, _messenger, null);
        _debugUtils?.Dispose();
        if (_instance.Handle != 0) _vk.DestroyInstance(_instance, null);
        _vk.Dispose();
    }
}
