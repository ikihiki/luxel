const objects = new Map();
let nextHandle = 1;
let diagnosticSequence = 0;
let latestDiagnostics = null;

const newDiagnostics = () => ({
  sequence: ++diagnosticSequence,
  timestamp: new Date().toISOString(),
  stage: "initialize",
  lastOperation: "initialize",
  backendHandle: null,
  requestAdapterOptions: { powerPreference: "high-performance" },
  adapter: null,
  device: { status: "not-requested", lost: null },
  uncapturedErrors: [],
  lastError: null,
  surface: null,
});
const touchDiagnostics = (diagnostics, stage, operation) => {
  diagnostics.sequence = ++diagnosticSequence;
  diagnostics.timestamp = new Date().toISOString();
  diagnostics.stage = stage;
  diagnostics.lastOperation = operation;
  latestDiagnostics = diagnostics;
};
const describeError = (error, source) => ({
  source,
  name: error?.name || error?.constructor?.name || "Error",
  message: error?.message || String(error),
  stack: error?.stack || null,
  timestamp: new Date().toISOString(),
});
const recordError = (diagnostics, error, source) => {
  diagnostics.lastError = describeError(error, source);
  touchDiagnostics(diagnostics, "error", source);
};
const adapterSnapshot = adapter => {
  const info = adapter.info || {};
  return {
    vendor: info.vendor || null,
    architecture: info.architecture || null,
    device: info.device || null,
    description: info.description || null,
    subgroupMinSize: info.subgroupMinSize ?? null,
    subgroupMaxSize: info.subgroupMaxSize ?? null,
    isFallbackAdapter: info.isFallbackAdapter ?? null,
    features: [...adapter.features].sort(),
    limits: {
      maxBindGroups: adapter.limits.maxBindGroups,
      maxBindingsPerBindGroup: adapter.limits.maxBindingsPerBindGroup,
      maxSampledTexturesPerShaderStage: adapter.limits.maxSampledTexturesPerShaderStage,
      maxSamplersPerShaderStage: adapter.limits.maxSamplersPerShaderStage,
      maxBufferSize: adapter.limits.maxBufferSize,
      maxStorageBufferBindingSize: adapter.limits.maxStorageBufferBindingSize,
    },
  };
};

const put = (kind, value) => { const handle = nextHandle++; objects.set(handle, { kind, value }); return handle; };
const get = (handle, kind) => {
  const entry = objects.get(handle);
  if (!entry || entry.kind !== kind) throw new Error(`Invalid or foreign ${kind} handle: ${handle}.`);
  return entry.value;
};
const remove = (handle, kind) => {
  const value = get(handle, kind);
  objects.delete(handle);
  return value;
};
const decode = base64 => {
  if (!base64) return new Uint8Array();
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
};
const encode = bytes => {
  let binary = "";
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk)
    binary += String.fromCharCode(...bytes.subarray(i, Math.min(bytes.length, i + chunk)));
  return btoa(binary);
};
const text = base64 => new TextDecoder().decode(decode(base64));
const gpuFormat = value => ["rgba8unorm", "bgra8unorm", "r32float", "depth32float", "rgba8unorm-srgb", "bgra8unorm-srgb", "r8unorm", "rg8unorm", "depth24plus-stencil8"][value] ?? (() => { throw new Error(`Unsupported format ${value}.`); })();
const gpuBytesPerPixel = value => [4, 4, 4, 4, 4, 4, 1, 2][value] ?? (() => { throw new Error(`Unsupported format ${value}.`); })();
const ensureHealthy = backend => {
  if (backend.lost) throw new Error(`WebGPU device was lost: ${backend.lost}`);
  if (backend.errors.length) throw new Error(`WebGPU validation error: ${backend.errors.shift()}`);
};
const endPass = command => {
  if (command.computePass) { command.computePass.end(); command.computePass = null; }
  if (command.renderPass) { command.renderPass.end(); command.renderPass = null; }
};

function createResourceBindGroup(backend) {
  const entries = [];
  for (let i = 0; i < 16; i++) entries.push({ binding: i, resource: (backend.textures[i] ?? backend.fallbackTexture).createView() });
  for (let i = 0; i < 16; i++) entries.push({ binding: 16 + i, resource: backend.samplers[i] ?? backend.fallbackSampler });
  return backend.device.createBindGroup({ layout: backend.resourceLayout, entries });
}

export async function initialize() {
  const diagnostics = newDiagnostics();
  latestDiagnostics = diagnostics;
  try {
    if (!globalThis.navigator?.gpu) throw new Error("navigator.gpu is unavailable; WebGPU requires a supported secure browser context.");
    touchDiagnostics(diagnostics, "request-adapter", "navigator.gpu.requestAdapter");
    const adapter = await navigator.gpu.requestAdapter(diagnostics.requestAdapterOptions);
    if (!adapter) throw new Error("No browser WebGPU adapter is available.");
    diagnostics.adapter = adapterSnapshot(adapter);
    touchDiagnostics(diagnostics, "adapter-ready", "validate-adapter-limits");
    const limits = adapter.limits;
    if (limits.maxBindGroups < 2 || limits.maxBindingsPerBindGroup < 32 || limits.maxSampledTexturesPerShaderStage < 16 || limits.maxSamplersPerShaderStage < 16 || limits.maxBufferSize < 64 * 1024 * 1024 || limits.maxStorageBufferBindingSize < 64 * 1024 * 1024)
      throw new Error("The WebGPU adapter does not satisfy Luxel's fixed 64MiB arena, 2-group, 16-texture, 16-sampler ABI.");
    diagnostics.device.status = "requesting";
    touchDiagnostics(diagnostics, "request-device", "adapter.requestDevice");
    const device = await adapter.requestDevice();
    diagnostics.device.status = "ready";
    touchDiagnostics(diagnostics, "device-ready", "create-backend-resources");
    const backend = { adapter, device, diagnostics, queue: device.queue, errors: [], lifecycleEvents: [], lost: null, serial: 0, completions: new Map(), submissions: new Map(), textures: Array(16), samplers: Array(16) };
    device.addEventListener("uncapturederror", event => {
      const error = describeError(event.error, "device.uncapturederror");
      backend.errors.push(error.message);
      backend.lifecycleEvents.push({ type: "validation", severity: "error", reason: error.name, message: error.message });
      diagnostics.uncapturedErrors.push(error);
      if (diagnostics.uncapturedErrors.length > 32) diagnostics.uncapturedErrors.shift();
      diagnostics.lastError = error;
      touchDiagnostics(diagnostics, "device-error", "device.uncapturederror");
    });
    device.lost.then(info => {
      backend.lost = `${info.reason}: ${info.message}`;
      const disposed = diagnostics.device.status === "disposed";
      diagnostics.device.status = disposed ? "disposed" : "lost";
      diagnostics.device.lost = { reason: info.reason, message: info.message, timestamp: new Date().toISOString(), expected: disposed };
      backend.lifecycleEvents.push({ type: "lost", reason: info.reason, message: info.message, expected: disposed });
      touchDiagnostics(diagnostics, disposed ? "disposed" : "device-lost", disposed ? "device.destroy" : "device.lost");
    }).catch(error => {
      backend.lost = String(error);
      diagnostics.device.status = "lost";
      diagnostics.device.lost = describeError(error, "device.lost");
      recordError(diagnostics, error, "device.lost");
    });
    backend.arena = device.createBuffer({ size: 64 * 1024 * 1024, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST });
    backend.fallbackTexture = device.createTexture({ size: [1, 1, 1], format: "rgba8unorm", usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST });
    device.queue.writeTexture({ texture: backend.fallbackTexture }, new Uint8Array([255, 255, 255, 255]), { bytesPerRow: 4 }, [1, 1, 1]);
    backend.fallbackSampler = device.createSampler({ magFilter: "nearest", minFilter: "nearest", mipmapFilter: "nearest" });
    backend.resourceLayout = device.createBindGroupLayout({ entries: [
      ...Array.from({ length: 16 }, (_, binding) => ({ binding, visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT | GPUShaderStage.COMPUTE, texture: { sampleType: "float", viewDimension: "2d" } })),
      ...Array.from({ length: 16 }, (_, i) => ({ binding: 16 + i, visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT | GPUShaderStage.COMPUTE, sampler: { type: "filtering" } }))
    ] });
    const makeLayout = (type, visibility) => device.createBindGroupLayout({ entries: [
      { binding: 0, visibility, buffer: { type, minBindingSize: 4 } },
      { binding: 1, visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT | GPUShaderStage.COMPUTE, buffer: { type: "uniform", hasDynamicOffset: true, minBindingSize: 256 } }
    ] });
    backend.computeLayout = makeLayout("storage", GPUShaderStage.COMPUTE);
    backend.graphicsLayout = makeLayout("read-only-storage", GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT);
    backend.computePipelineLayout = device.createPipelineLayout({ bindGroupLayouts: [backend.computeLayout, backend.resourceLayout] });
    backend.graphicsPipelineLayout = device.createPipelineLayout({ bindGroupLayouts: [backend.graphicsLayout, backend.resourceLayout] });
    const handle = put("backend", backend);
    diagnostics.backendHandle = handle;
    touchDiagnostics(diagnostics, "ready", "initialize-complete");
    return JSON.stringify({ handle, name: `WebGPU / ${adapter.info?.description || adapter.info?.device || "browser adapter"}` });
  } catch (error) {
    diagnostics.device.status = diagnostics.device.status === "requesting" ? "request-failed" : diagnostics.device.status;
    recordError(diagnostics, error, diagnostics.lastOperation || "initialize");
    throw error;
  }
}

export function getDiagnostics(backendHandle = 0) {
  let diagnostics = latestDiagnostics;
  if (backendHandle > 0) {
    const entry = objects.get(backendHandle);
    if (entry?.kind === "backend") diagnostics = entry.value.diagnostics;
  }
  return JSON.stringify(diagnostics || newDiagnostics());
}

export function drainLifecycleEvents(backendHandle) {
  const backend = get(backendHandle, "backend");
  const events = backend.lifecycleEvents.splice(0);
  return JSON.stringify(events);
}

export function recordDiagnosticsError(backendHandle, source, name, message, stack) {
  let diagnostics = latestDiagnostics;
  if (backendHandle > 0) {
    const entry = objects.get(backendHandle);
    if (entry?.kind === "backend") diagnostics = entry.value.diagnostics;
  }
  diagnostics ||= newDiagnostics();
  diagnostics.lastError = { source, name, message, stack: stack || null, timestamp: new Date().toISOString() };
  touchDiagnostics(diagnostics, "error", source);
}

export function createComputePipeline(backendHandle, wgslBase64, entryPoint) {
  const backend = get(backendHandle, "backend"); ensureHealthy(backend);
  const module = backend.device.createShaderModule({ code: text(wgslBase64) });
  return put("pipeline", { backend, compute: true, pipeline: backend.device.createComputePipeline({ layout: backend.computePipelineLayout, compute: { module, entryPoint } }) });
}

export function createGraphicsPipeline(backendHandle, vsBase64, vsEntry, psBase64, psEntry, rasterJson) {
  const backend = get(backendHandle, "backend"); ensureHealthy(backend);
  const raster = JSON.parse(rasterJson);
  const vs = backend.device.createShaderModule({ code: text(vsBase64) });
  const ps = backend.device.createShaderModule({ code: text(psBase64) });
  const primitive = { topology: raster.topology === 1 ? "triangle-strip" : "triangle-list", cullMode: ["none", "front", "back"][raster.cullMode], frontFace: raster.frontFace === 1 ? "cw" : "ccw" };
  const target = { format: gpuFormat(raster.colorFormat) };
  if (raster.blend === 1) target.blend = { color: { srcFactor: "src-alpha", dstFactor: "one-minus-src-alpha" }, alpha: { srcFactor: "one", dstFactor: "one-minus-src-alpha" } };
  const descriptor = { layout: backend.graphicsPipelineLayout, vertex: { module: vs, entryPoint: vsEntry }, fragment: { module: ps, entryPoint: psEntry, targets: [target] }, primitive };
  if (raster.depthFormat >= 0) {
    const compares = ["never", "less", "equal", "less-equal", "greater", "not-equal", "greater-equal", "always"];
    const ops = ["keep", "zero", "replace", "increment-clamp", "decrement-clamp", "invert", "increment-wrap", "decrement-wrap"];
    const face = value => ({ compare: compares[value.compare], failOp: ops[value.fail], depthFailOp: ops[value.depthFail], passOp: ops[value.pass] });
    descriptor.depthStencil = { format: gpuFormat(raster.depthFormat), depthWriteEnabled: raster.depthWrite, depthCompare: raster.depthTest ? compares[raster.depthCompare] : "always", stencilFront: face(raster.stencilFront), stencilBack: face(raster.stencilBack), stencilReadMask: raster.stencilReadMask, stencilWriteMask: raster.stencilWriteMask };
  }
  return put("pipeline", { backend, compute: false, pipeline: backend.device.createRenderPipeline(descriptor) });
}

export function createTexture(backendHandle, width, height, formatValue, usageValue, bindlessIndex, dataBase64) {
  const backend = get(backendHandle, "backend"); ensureHealthy(backend);
  let usage;
  if (usageValue === 0) usage = GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC;
  else if (usageValue === 1) usage = GPUTextureUsage.RENDER_ATTACHMENT;
  else usage = GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST;
  const texture = backend.device.createTexture({ size: [width, height, 1], format: gpuFormat(formatValue), usage });
  if (usageValue === 2) {
    const slot = bindlessIndex;
    if (slot < 0 || slot >= 16 || backend.textures[slot]) { texture.destroy(); throw new Error(`Invalid or occupied sampled texture slot ${slot}.`); }
    backend.textures[slot] = texture;
    backend.queue.writeTexture({ texture }, decode(dataBase64), { bytesPerRow: width * gpuBytesPerPixel(formatValue), rowsPerImage: height }, [width, height, 1]);
    texture.__luxelSlot = slot;
  }
  return put("texture", { backend, texture, format: gpuFormat(formatValue), width, height });
}

export function createSampler(backendHandle, filterValue, addressValue, bindlessIndex) {
  const backend = get(backendHandle, "backend"); ensureHealthy(backend);
  const mode = addressValue === 1 ? "repeat" : "clamp-to-edge";
  const filter = filterValue === 1 ? "linear" : "nearest";
  const sampler = backend.device.createSampler({ addressModeU: mode, addressModeV: mode, addressModeW: mode, magFilter: filter, minFilter: filter, mipmapFilter: filter });
  const slot = bindlessIndex;
  if (slot < 0 || slot >= 16 || backend.samplers[slot]) throw new Error(`Invalid or occupied sampler slot ${slot}.`);
  backend.samplers[slot] = sampler; sampler.__luxelSlot = slot;
  return put("sampler", { backend, sampler });
}

export function createCommandBuffer(backendHandle) {
  const backend = get(backendHandle, "backend"); ensureHealthy(backend);
  const rootBuffer = backend.device.createBuffer({ size: 64 * 1024, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
  const command = { backend, encoder: backend.device.createCommandEncoder(), rootBuffer, rootWrites: [], rootOffset: 0, currentRootOffset: 0, temps: [], computePass: null, renderPass: null, graphicsPipeline: null, finished: null };
  command.computeGroup = backend.device.createBindGroup({ layout: backend.computeLayout, entries: [{ binding: 0, resource: { buffer: backend.arena } }, { binding: 1, resource: { buffer: rootBuffer, size: 256 } }] });
  command.graphicsGroup = backend.device.createBindGroup({ layout: backend.graphicsLayout, entries: [{ binding: 0, resource: { buffer: backend.arena } }, { binding: 1, resource: { buffer: rootBuffer, size: 256 } }] });
  command.resourceGroup = createResourceBindGroup(backend);
  return put("command", command);
}

export function commandSetComputePipeline(commandHandle, pipelineHandle) {
  const command = get(commandHandle, "command"), pipeline = get(pipelineHandle, "pipeline");
  if (pipeline.backend !== command.backend || !pipeline.compute) throw new Error("Foreign or non-compute pipeline.");
  endPass(command); command.computePass = command.encoder.beginComputePass(); command.computePass.setPipeline(pipeline.pipeline);
}
export function commandSetGraphicsPipeline(commandHandle, pipelineHandle) {
  const command = get(commandHandle, "command"), pipeline = get(pipelineHandle, "pipeline");
  if (pipeline.backend !== command.backend || pipeline.compute) throw new Error("Foreign or non-graphics pipeline.");
  if (command.computePass) { command.computePass.end(); command.computePass = null; }
  command.graphicsPipeline = pipeline.pipeline; if (command.renderPass) command.renderPass.setPipeline(pipeline.pipeline);
}
export function commandSetRootConstants(commandHandle, dataBase64) {
  const command = get(commandHandle, "command"), bytes = decode(dataBase64);
  if (bytes.length > 192 || command.rootOffset + 256 > 64 * 1024) throw new Error("Root constant buffer exhausted or root data exceeds 192 bytes.");
  const padded = new Uint8Array(256); padded.set(bytes); command.rootWrites.push([command.rootOffset, padded]); command.currentRootOffset = command.rootOffset; command.rootOffset += 256;
}
export function commandDispatch(commandHandle, x, y, z) {
  const command = get(commandHandle, "command"); if (!command.computePass) throw new Error("No compute pass is active.");
  command.computePass.setBindGroup(0, command.computeGroup, [command.currentRootOffset]); command.computePass.setBindGroup(1, command.resourceGroup); command.computePass.dispatchWorkgroups(x, y, z);
}
export function commandBeginRendering(commandHandle, colorHandle, depthHandle, r, g, b, a, clearDepth, clearStencil) {
  const command = get(commandHandle, "command"), color = get(colorHandle, "texture");
  if (color.backend !== command.backend) throw new Error("Foreign color texture."); endPass(command);
  const descriptor = { colorAttachments: [{ view: color.texture.createView(), clearValue: { r, g, b, a }, loadOp: "clear", storeOp: "store" }] };
  if (depthHandle) { const depth = get(depthHandle, "texture"); if (depth.backend !== command.backend) throw new Error("Foreign depth texture."); descriptor.depthStencilAttachment = { view: depth.texture.createView(), depthClearValue: clearDepth, depthLoadOp: "clear", depthStoreOp: "store" }; if (depth.format === "depth24plus-stencil8") Object.assign(descriptor.depthStencilAttachment, { stencilClearValue: clearStencil, stencilLoadOp: "clear", stencilStoreOp: "store" }); }
  command.renderPass = command.encoder.beginRenderPass(descriptor); if (command.graphicsPipeline) command.renderPass.setPipeline(command.graphicsPipeline);
}
export function commandSetStencilReference(commandHandle, reference) { const command=get(commandHandle,"command"); if(!command.renderPass) throw new Error("No render pass is active."); command.renderPass.setStencilReference(reference); }
export function commandSetViewport(commandHandle,x,y,width,height,minDepth,maxDepth) { const command=get(commandHandle,"command"); if(!command.renderPass) throw new Error("No render pass is active."); command.renderPass.setViewport(x,y,width,height,minDepth,maxDepth); }
export function commandSetScissor(commandHandle,x,y,width,height) { const command=get(commandHandle,"command"); if(!command.renderPass) throw new Error("No render pass is active."); command.renderPass.setScissorRect(x,y,width,height); }
export function commandEndRendering(commandHandle) { const command = get(commandHandle, "command"); if (!command.renderPass) throw new Error("No render pass is active."); command.renderPass.end(); command.renderPass = null; }
export function commandDraw(commandHandle, vertexCount, instanceCount) { const command = get(commandHandle, "command"); if (!command.renderPass) throw new Error("No render pass is active."); command.renderPass.setBindGroup(0, command.graphicsGroup, [command.currentRootOffset]); command.renderPass.setBindGroup(1, command.resourceGroup); command.renderPass.draw(vertexCount, instanceCount); }
export function commandCopyTextureToBuffer(commandHandle, textureHandle, destinationOffset, bytesPerRow, width, height) { const command = get(commandHandle, "command"), texture = get(textureHandle, "texture"); endPass(command); const layout = { buffer: command.backend.arena, offset: destinationOffset }; if (height > 1) { layout.bytesPerRow = bytesPerRow; layout.rowsPerImage = height; } command.encoder.copyTextureToBuffer({ texture: texture.texture }, layout, [width, height, 1]); }
export function commandCopyBufferToBuffer(commandHandle, sourceOffset, destinationOffset, bytes) { const command = get(commandHandle, "command"); endPass(command); const temp=command.backend.device.createBuffer({size:bytes,usage:GPUBufferUsage.COPY_SRC|GPUBufferUsage.COPY_DST}); command.temps.push(temp); command.encoder.copyBufferToBuffer(command.backend.arena,sourceOffset,temp,0,bytes); command.encoder.copyBufferToBuffer(temp,0,command.backend.arena,destinationOffset,bytes); }
export function commandBarrier(commandHandle) { endPass(get(commandHandle, "command")); }
export function commandFinish(commandHandle) { const command = get(commandHandle, "command"); endPass(command); command.finished = command.encoder.finish(); }
export function uploadArena(backendHandle, offset, dataBase64) { const backend = get(backendHandle, "backend"); ensureHealthy(backend); backend.queue.writeBuffer(backend.arena, offset, decode(dataBase64)); }
export function submit(backendHandle, commandHandle) { const backend = get(backendHandle, "backend"), command = get(commandHandle, "command"); if (command.backend !== backend || !command.finished) throw new Error("Foreign or unfinished command buffer."); for (const [offset, bytes] of command.rootWrites) backend.queue.writeBuffer(command.rootBuffer, offset, bytes); backend.queue.submit([command.finished]); const serial = ++backend.serial; backend.completions.set(serial, backend.queue.onSubmittedWorkDone()); backend.submissions.set(serial, command); command.submitted = true; return serial; }

async function readbacks(backend, requestsJson) {
  const requests = JSON.parse(requestsJson); if (!requests.length) { ensureHealthy(backend); return ""; }
  const total = requests.reduce((sum, item) => sum + item.size, 0), offsets = []; let cursor = 0;
  const allocationSize = requests.reduce((sum, item) => sum + ((item.size + 3) & ~3), 0);
  const readback = backend.device.createBuffer({ size: Math.max(4, allocationSize), usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
  const encoder = backend.device.createCommandEncoder();
  for (const request of requests) { offsets.push(cursor); const size = (request.size + 3) & ~3; encoder.copyBufferToBuffer(backend.arena, request.offset, readback, cursor, size); cursor += size; }
  backend.queue.submit([encoder.finish()]); await backend.queue.onSubmittedWorkDone(); await readback.mapAsync(GPUMapMode.READ);
  const mapped = new Uint8Array(readback.getMappedRange()), packed = new Uint8Array(total); cursor = 0;
  for (let i = 0, output = 0; i < requests.length; i++) { packed.set(mapped.subarray(offsets[i], offsets[i] + requests[i].size), output); output += requests[i].size; }
  readback.unmap(); readback.destroy(); ensureHealthy(backend); return encode(packed);
}
export async function complete(backendHandle, serial, requestsJson) { const backend = get(backendHandle, "backend"), completion = backend.completions.get(serial); if (!completion) throw new Error(`Unknown submission serial ${serial}.`); await completion; backend.completions.delete(serial); const command=backend.submissions.get(serial); backend.submissions.delete(serial); if(command){command.rootBuffer.destroy(); for(const temp of command.temps) temp.destroy();} return await readbacks(backend, requestsJson); }
export async function waitIdle(backendHandle, requestsJson) { const backend = get(backendHandle, "backend"); await backend.queue.onSubmittedWorkDone(); for(const command of backend.submissions.values()){command.rootBuffer.destroy(); for(const temp of command.temps) temp.destroy();} backend.submissions.clear(); backend.completions.clear(); return await readbacks(backend, requestsJson); }

const blitWgsl = `struct Info { stride:u32, width:u32, height:u32, targetWidth:u32, targetHeight:u32, unused0:u32, unused1:u32, unused2:u32 } @group(0) @binding(0) var<storage,read> pixels:array<u32>; @group(0) @binding(1) var<uniform> info:Info; struct Out { @builtin(position) position:vec4f } @vertex fn vsMain(@builtin(vertex_index) i:u32)->Out { var p=array<vec2f,3>(vec2f(-1,-1),vec2f(3,-1),vec2f(-1,3)); var o:Out; o.position=vec4f(p[i],0,1); return o; } @fragment fn fsMain(@builtin(position) p:vec4f)->@location(0) vec4f { let x=min(u32(p.x*f32(info.width)/f32(info.targetWidth)),info.width-1); let y=min(u32(p.y*f32(info.height)/f32(info.targetHeight)),info.height-1); let v=pixels[y*info.stride+x]; return vec4f(f32(v&255u),f32((v>>8u)&255u),f32((v>>16u)&255u),f32((v>>24u)&255u))/255.0; }`;
export function createSurface(backendHandle, canvasToken, width, height) {
  const backend = get(backendHandle, "backend"), diagnostics = backend.diagnostics;
  touchDiagnostics(diagnostics, "surface-create", "canvas.getContext(webgpu)");
  try {
    const canvas = document.querySelector(canvasToken); if (!(canvas instanceof HTMLCanvasElement)) throw new Error(`Canvas not found: ${canvasToken}.`);
    const context = canvas.getContext("webgpu"); if (!context) throw new Error("Canvas WebGPU context is unavailable.");
    const format = navigator.gpu.getPreferredCanvasFormat();
    const layout = backend.device.createBindGroupLayout({ entries: [{ binding:0, visibility:GPUShaderStage.FRAGMENT, buffer:{ type:"read-only-storage" } }, { binding:1, visibility:GPUShaderStage.FRAGMENT, buffer:{ type:"uniform" } }] });
    const module = backend.device.createShaderModule({ code: blitWgsl }), pipeline = backend.device.createRenderPipeline({ layout:backend.device.createPipelineLayout({bindGroupLayouts:[layout]}), vertex:{module,entryPoint:"vsMain"}, fragment:{module,entryPoint:"fsMain",targets:[{format}]}, primitive:{topology:"triangle-list"} });
    const info = backend.device.createBuffer({ size:32, usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST });
    const surface = { backend, canvas, context, format, layout, pipeline, info, width, height };
    diagnostics.surface = { canvasToken, format, alphaMode: "premultiplied", width, height, configured: false, presentCount: 0 };
    configureSurface(surface);
    touchDiagnostics(diagnostics, "surface-ready", "createSurface");
    return put("surface", surface);
  } catch (error) {
    recordError(diagnostics, error, "createSurface");
    throw error;
  }
}
function configureSurface(surface) {
  const diagnostics = surface.backend.diagnostics;
  touchDiagnostics(diagnostics, "surface-configure", "GPUCanvasContext.configure");
  try {
    if (surface.canvas.width !== surface.width) surface.canvas.width = surface.width;
    if (surface.canvas.height !== surface.height) surface.canvas.height = surface.height;
    if (surface.width && surface.height) surface.context.configure({ device:surface.backend.device, format:surface.format, alphaMode:"premultiplied" }); else surface.context.unconfigure();
    Object.assign(diagnostics.surface, { width: surface.width, height: surface.height, configured: Boolean(surface.width && surface.height) });
  } catch (error) {
    recordError(diagnostics, error, "GPUCanvasContext.configure");
    throw error;
  }
}
export function surfaceResize(surfaceHandle, width, height) { const surface=get(surfaceHandle,"surface"); if (surface.width===width && surface.height===height) return; surface.width=width; surface.height=height; configureSurface(surface); }
export function surfacePresent(surfaceHandle, sourceOffset, stride, width, height) {
  const s=get(surfaceHandle,"surface"), b=s.backend, diagnostics=b.diagnostics; if (!width||!height||!s.width||!s.height) return;
  touchDiagnostics(diagnostics, "surface-present", "GPUCanvasContext.getCurrentTexture");
  try {
    b.queue.writeBuffer(s.info,0,new Uint32Array([stride,width,height,s.width,s.height,0,0,0]));
    const group=b.device.createBindGroup({layout:s.layout,entries:[{binding:0,resource:{buffer:b.arena,offset:sourceOffset,size:Math.max(4,((stride*(height-1)+width)*4+3)&~3)}},{binding:1,resource:{buffer:s.info}}]});
    const e=b.device.createCommandEncoder(), p=e.beginRenderPass({colorAttachments:[{view:s.context.getCurrentTexture().createView(),loadOp:"clear",storeOp:"store",clearValue:{r:0,g:0,b:0,a:1}}]});
    p.setPipeline(s.pipeline); p.setBindGroup(0,group); p.draw(3); p.end(); b.queue.submit([e.finish()]);
    diagnostics.surface.presentCount += 1;
    touchDiagnostics(diagnostics, "surface-presented", "queue.submit(surface)");
  } catch (error) {
    recordError(diagnostics, error, "surfacePresent");
    throw error;
  }
}

export function release(kind, handle) {
  const names = [null,"texture","sampler","pipeline","command","surface"], name=names[kind], value=remove(handle,name);
  if (name === "texture") { if (value.texture.__luxelSlot !== undefined) value.backend.textures[value.texture.__luxelSlot]=undefined; value.texture.destroy(); }
  else if (name === "sampler" && value.sampler.__luxelSlot !== undefined) value.backend.samplers[value.sampler.__luxelSlot]=undefined;
  else if (name === "command" && !value.submitted) { value.rootBuffer.destroy(); for(const temp of value.temps) temp.destroy(); }
  else if (name === "surface") { value.context.unconfigure(); value.info.destroy(); }
}
export function disposeBackend(handle) { const backend=remove(handle,"backend"); for (const [objectHandle,entry] of [...objects]) if (entry.value?.backend===backend) objects.delete(objectHandle); backend.lifecycleEvents.push({ type: "lost", reason: "destroyed", message: "device.destroy()", expected: true }); backend.diagnostics.device.status="disposed"; touchDiagnostics(backend.diagnostics,"disposed","disposeBackend"); backend.arena.destroy(); backend.fallbackTexture.destroy(); backend.device.destroy(); return JSON.stringify(backend.lifecycleEvents.splice(0)); }
