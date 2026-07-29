const objects = new Map();
let nextHandle = 1;

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
const gpuFormat = value => ["rgba8unorm", "bgra8unorm", "r32float", "depth32float"][value] ?? (() => { throw new Error(`Unsupported format ${value}.`); })();
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
  if (!globalThis.navigator?.gpu) throw new Error("navigator.gpu is unavailable; WebGPU requires a supported secure browser context.");
  const adapter = await navigator.gpu.requestAdapter({ powerPreference: "high-performance" });
  if (!adapter) throw new Error("No browser WebGPU adapter is available.");
  const limits = adapter.limits;
  if (limits.maxBindGroups < 2 || limits.maxBindingsPerBindGroup < 32 || limits.maxSampledTexturesPerShaderStage < 16 || limits.maxSamplersPerShaderStage < 16 || limits.maxBufferSize < 64 * 1024 * 1024 || limits.maxStorageBufferBindingSize < 64 * 1024 * 1024)
    throw new Error("The WebGPU adapter does not satisfy Luxel's fixed 64MiB arena, 2-group, 16-texture, 16-sampler ABI.");
  const device = await adapter.requestDevice();
  const backend = { adapter, device, queue: device.queue, errors: [], lost: null, serial: 0, completions: new Map(), submissions: new Map(), textures: Array(16), samplers: Array(16) };
  device.addEventListener("uncapturederror", event => backend.errors.push(event.error?.message ?? String(event.error)));
  device.lost.then(info => { backend.lost = `${info.reason}: ${info.message}`; }).catch(error => { backend.lost = String(error); });
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
  return JSON.stringify({ handle, name: `WebGPU / ${adapter.info?.description || adapter.info?.device || "browser adapter"}` });
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
  if (raster.depthTest || raster.depthWrite) descriptor.depthStencil = { format: gpuFormat(raster.depthFormat), depthWriteEnabled: raster.depthWrite, depthCompare: raster.depthTest ? "less" : "always" };
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
    backend.queue.writeTexture({ texture }, decode(dataBase64), { bytesPerRow: width * 4, rowsPerImage: height }, [width, height, 1]);
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
export function commandBeginRendering(commandHandle, colorHandle, depthHandle, r, g, b, a, clearDepth) {
  const command = get(commandHandle, "command"), color = get(colorHandle, "texture");
  if (color.backend !== command.backend) throw new Error("Foreign color texture."); endPass(command);
  const descriptor = { colorAttachments: [{ view: color.texture.createView(), clearValue: { r, g, b, a }, loadOp: "clear", storeOp: "store" }] };
  if (depthHandle) { const depth = get(depthHandle, "texture"); if (depth.backend !== command.backend) throw new Error("Foreign depth texture."); descriptor.depthStencilAttachment = { view: depth.texture.createView(), depthClearValue: clearDepth, depthLoadOp: "clear", depthStoreOp: "store" }; }
  command.renderPass = command.encoder.beginRenderPass(descriptor); if (command.graphicsPipeline) command.renderPass.setPipeline(command.graphicsPipeline);
  command.renderPass.setViewport(0, 0, color.width, color.height, 0, 1); command.renderPass.setScissorRect(0, 0, color.width, color.height);
}
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

const blitWgsl = `struct Info { stride:u32, width:u32, height:u32, unused:u32 } @group(0) @binding(0) var<storage,read> pixels:array<u32>; @group(0) @binding(1) var<uniform> info:Info; struct Out { @builtin(position) position:vec4f } @vertex fn vsMain(@builtin(vertex_index) i:u32)->Out { var p=array<vec2f,3>(vec2f(-1,-1),vec2f(3,-1),vec2f(-1,3)); var o:Out; o.position=vec4f(p[i],0,1); return o; } @fragment fn fsMain(@builtin(position) p:vec4f)->@location(0) vec4f { let x=min(u32(p.x),info.width-1); let y=min(u32(p.y),info.height-1); let v=pixels[y*info.stride+x]; return vec4f(f32(v&255u),f32((v>>8u)&255u),f32((v>>16u)&255u),f32((v>>24u)&255u))/255.0; }`;
export function createSurface(backendHandle, canvasToken, width, height) {
  const backend = get(backendHandle, "backend"), canvas = document.querySelector(canvasToken); if (!(canvas instanceof HTMLCanvasElement)) throw new Error(`Canvas not found: ${canvasToken}.`);
  const context = canvas.getContext("webgpu"), format = navigator.gpu.getPreferredCanvasFormat();
  const layout = backend.device.createBindGroupLayout({ entries: [{ binding:0, visibility:GPUShaderStage.FRAGMENT, buffer:{ type:"read-only-storage" } }, { binding:1, visibility:GPUShaderStage.FRAGMENT, buffer:{ type:"uniform" } }] });
  const module = backend.device.createShaderModule({ code: blitWgsl }), pipeline = backend.device.createRenderPipeline({ layout:backend.device.createPipelineLayout({bindGroupLayouts:[layout]}), vertex:{module,entryPoint:"vsMain"}, fragment:{module,entryPoint:"fsMain",targets:[{format}]}, primitive:{topology:"triangle-list"} });
  const info = backend.device.createBuffer({ size:16, usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST });
  const surface = { backend, canvas, context, format, layout, pipeline, info, width, height }; configureSurface(surface); return put("surface", surface);
}
function configureSurface(surface) { surface.canvas.width = surface.width; surface.canvas.height = surface.height; if (surface.width && surface.height) surface.context.configure({ device:surface.backend.device, format:surface.format, alphaMode:"premultiplied" }); else surface.context.unconfigure(); }
export function surfaceResize(surfaceHandle, width, height) { const surface=get(surfaceHandle,"surface"); surface.width=width; surface.height=height; configureSurface(surface); }
export function surfacePresent(surfaceHandle, sourceOffset, stride, width, height) { const s=get(surfaceHandle,"surface"), b=s.backend; if (!width||!height) return; b.queue.writeBuffer(s.info,0,new Uint32Array([stride,width,height,0])); const group=b.device.createBindGroup({layout:s.layout,entries:[{binding:0,resource:{buffer:b.arena,offset:sourceOffset,size:Math.max(4,((stride*(height-1)+width)*4+3)&~3)}},{binding:1,resource:{buffer:s.info}}]}); const e=b.device.createCommandEncoder(), p=e.beginRenderPass({colorAttachments:[{view:s.context.getCurrentTexture().createView(),loadOp:"clear",storeOp:"store",clearValue:{r:0,g:0,b:0,a:1}}]}); p.setPipeline(s.pipeline); p.setBindGroup(0,group); p.draw(3); p.end(); b.queue.submit([e.finish()]); }

export function release(kind, handle) {
  const names = [null,"texture","sampler","pipeline","command","surface"], name=names[kind], value=remove(handle,name);
  if (name === "texture") { if (value.texture.__luxelSlot !== undefined) value.backend.textures[value.texture.__luxelSlot]=undefined; value.texture.destroy(); }
  else if (name === "sampler" && value.sampler.__luxelSlot !== undefined) value.backend.samplers[value.sampler.__luxelSlot]=undefined;
  else if (name === "command" && !value.submitted) { value.rootBuffer.destroy(); for(const temp of value.temps) temp.destroy(); }
  else if (name === "surface") { value.context.unconfigure(); value.info.destroy(); }
}
export function disposeBackend(handle) { const backend=remove(handle,"backend"); for (const [objectHandle,entry] of [...objects]) if (entry.value?.backend===backend) objects.delete(objectHandle); backend.arena.destroy(); backend.fallbackTexture.destroy(); backend.device.destroy(); }
