// Web Audio MVP. Streaming via AudioWorklet/SharedArrayBuffer is intentionally deferred;
// this module owns decoded PCM clip buffers and a small main-thread queue per voice.
const objects = new Map();
let nextHandle = 1;

const add = value => { const handle = nextHandle++; objects.set(handle, value); return handle; };
const requireObject = (handle, kind) => {
  const value = objects.get(handle);
  if (!value || value.kind !== kind) throw new Error(`invalid ${kind} handle ${handle}`);
  return value;
};
const bytes = base64 => {
  const binary = atob(base64);
  const result = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) result[i] = binary.charCodeAt(i);
  return result;
};

export async function initialize() {
  const AudioContextType = globalThis.AudioContext || globalThis.webkitAudioContext;
  if (!AudioContextType) throw new Error("Web Audio is not available in this browser.");
  const context = new AudioContextType();
  const master = context.createGain();
  master.connect(context.destination);
  const backend = { kind: "backend", context, master, voices: new Set() };
  const handle = add(backend);
  return JSON.stringify({ handle, state: context.state });
}

export async function resume(handle) {
  await requireObject(handle, "backend").context.resume();
}

export async function suspend(handle) {
  await requireObject(handle, "backend").context.suspend();
}

export function setMasterVolume(handle, volume) {
  const backend = requireObject(handle, "backend");
  backend.master.gain.setValueAtTime(volume, backend.context.currentTime);
}

export function createVoice(backendHandle, sampleRate, channels, bitsPerSample) {
  if (bitsPerSample !== 16 || (channels !== 1 && channels !== 2)) throw new Error("unsupported PCM format");
  const backend = requireObject(backendHandle, "backend");
  const gain = backend.context.createGain();
  const pan = backend.context.createStereoPanner();
  gain.connect(pan);
  pan.connect(backend.master);
  const voice = {
    kind: "voice", backend, sampleRate, channels, gain, pan,
    queue: [], source: null, playing: false, offset: 0, startedAt: 0,
    pitch: 1, generation: 0,
  };
  const handle = add(voice);
  voice.handle = handle;
  backend.voices.add(handle);
  return handle;
}

function decodePcm(voice, base64) {
  const data = bytes(base64);
  const frames = data.length / (voice.channels * 2);
  const buffer = voice.backend.context.createBuffer(voice.channels, frames, voice.sampleRate);
  const view = new DataView(data.buffer, data.byteOffset, data.byteLength);
  for (let channel = 0; channel < voice.channels; channel++) {
    const output = buffer.getChannelData(channel);
    for (let frame = 0; frame < frames; frame++) {
      const sample = view.getInt16((frame * voice.channels + channel) * 2, true);
      output[frame] = sample < 0 ? sample / 32768 : sample / 32767;
    }
  }
  return buffer;
}

export function submitBuffer(handle, pcmBase64, loop) {
  const voice = requireObject(handle, "voice");
  voice.queue.push({ buffer: decodePcm(voice, pcmBase64), loop });
  if (voice.playing && !voice.source) startHead(voice);
}

function captureOffset(voice) {
  if (!voice.source || !voice.queue.length) return;
  const duration = voice.queue[0].buffer.duration;
  const elapsed = Math.max(0, voice.backend.context.currentTime - voice.startedAt) * voice.pitch;
  voice.offset += elapsed;
  voice.offset = voice.queue[0].loop && duration > 0 ? voice.offset % duration : Math.min(voice.offset, duration);
}

function stopSource(voice) {
  if (!voice.source) return;
  voice.generation++;
  const source = voice.source;
  voice.source = null;
  source.onended = null;
  try { source.stop(); } catch { }
  source.disconnect();
}

function startHead(voice) {
  if (!voice.playing || !voice.queue.length || voice.source) return;
  const item = voice.queue[0];
  const source = voice.backend.context.createBufferSource();
  source.buffer = item.buffer;
  source.loop = item.loop;
  source.playbackRate.value = voice.pitch;
  source.connect(voice.gain);
  const generation = ++voice.generation;
  voice.source = source;
  voice.startedAt = voice.backend.context.currentTime;
  source.onended = () => {
    if (voice.generation !== generation || voice.source !== source) return;
    voice.source = null;
    voice.offset = 0;
    if (!item.loop) voice.queue.shift();
    if (!voice.queue.length) voice.playing = false;
    else startHead(voice);
  };
  source.start(0, Math.min(voice.offset, Math.max(0, item.buffer.duration - Number.EPSILON)));
}

export function play(handle) {
  const voice = requireObject(handle, "voice");
  if (!voice.queue.length) return;
  voice.playing = true;
  startHead(voice);
}

export function pause(handle) {
  const voice = requireObject(handle, "voice");
  if (!voice.playing) return;
  captureOffset(voice);
  voice.playing = false;
  stopSource(voice);
}

export function stop(handle) {
  const voice = requireObject(handle, "voice");
  voice.playing = false;
  stopSource(voice);
  voice.queue.length = 0;
  voice.offset = 0;
}

export function setVolume(handle, volume) {
  const voice = requireObject(handle, "voice");
  voice.gain.gain.setValueAtTime(volume, voice.backend.context.currentTime);
}

export function setPitch(handle, pitch) {
  const voice = requireObject(handle, "voice");
  const restart = voice.playing && !!voice.source;
  if (restart) captureOffset(voice);
  if (restart) stopSource(voice);
  voice.pitch = pitch;
  if (restart) startHead(voice);
}

export function setPan(handle, pan) {
  const voice = requireObject(handle, "voice");
  voice.pan.pan.setValueAtTime(pan, voice.backend.context.currentTime);
}

export function isPlaying(handle) {
  return requireObject(handle, "voice").playing ? 1 : 0;
}

export function buffersQueued(handle) {
  return requireObject(handle, "voice").queue.length;
}

export function disposeVoice(handle) {
  const voice = objects.get(handle);
  if (!voice || voice.kind !== "voice") return;
  voice.playing = false;
  stopSource(voice);
  voice.queue.length = 0;
  voice.gain.disconnect();
  voice.pan.disconnect();
  voice.backend.voices.delete(handle);
  objects.delete(handle);
}

export function disposeBackend(handle) {
  const backend = objects.get(handle);
  if (!backend || backend.kind !== "backend") return;
  for (const voiceHandle of [...backend.voices]) disposeVoice(voiceHandle);
  backend.master.disconnect();
  void backend.context.close();
  objects.delete(handle);
}
