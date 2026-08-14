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
  const context = requireObject(handle, "backend").context;
  await context.resume();
  return context.state;
}

export async function suspend(handle) {
  const context = requireObject(handle, "backend").context;
  await context.suspend();
  return context.state;
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
    queue: [], playing: false, nextStartTime: 0,
    pitch: 1, generation: 0,
  };
  const handle = add(voice);
  voice.handle = handle;
  backend.voices.add(handle);
  return handle;
}

function decodePcm(voice, pcm) {
  const data = pcm instanceof Uint8Array ? pcm : Uint8Array.from(pcm);
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

export function submitBuffer(handle, pcm, loop) {
  const voice = requireObject(handle, "voice");
  voice.queue.push({
    buffer: decodePcm(voice, pcm), loop,
    source: null, offset: 0, startTime: 0, endTime: 0,
  });
  if (voice.playing) scheduleQueue(voice);
}

function captureHeadOffset(voice) {
  if (!voice.queue.length) return;
  const item = voice.queue[0];
  if (!item.source || voice.backend.context.currentTime <= item.startTime) return;
  const elapsed = (voice.backend.context.currentTime - item.startTime) * voice.pitch;
  item.offset += elapsed;
  item.offset = item.loop && item.buffer.duration > 0
    ? item.offset % item.buffer.duration
    : Math.min(item.offset, item.buffer.duration);
}

function stopScheduledSources(voice) {
  voice.generation++;
  for (const item of voice.queue) {
    if (!item.source) continue;
    const source = item.source;
    item.source = null;
    source.onended = null;
    try { source.stop(); } catch { }
    source.disconnect();
  }
  voice.nextStartTime = 0;
}

function scheduleQueue(voice) {
  if (!voice.playing || !voice.queue.length) return;
  const context = voice.backend.context;
  let nextStart = Math.max(voice.nextStartTime, context.currentTime + 0.01);
  const generation = voice.generation;

  for (const item of voice.queue) {
    if (item.source) {
      if (item.loop) break;
      nextStart = Math.max(nextStart, item.endTime);
      continue;
    }

    const source = context.createBufferSource();
    source.buffer = item.buffer;
    source.loop = item.loop;
    source.playbackRate.value = voice.pitch;
    source.connect(voice.gain);
    item.source = source;
    item.startTime = nextStart;
    const remaining = Math.max(0, item.buffer.duration - item.offset);
    item.endTime = item.loop ? Number.POSITIVE_INFINITY : nextStart + remaining / voice.pitch;
    source.onended = () => {
      if (voice.generation !== generation || item.source !== source) return;
      item.source = null;
      item.offset = 0;
      const index = voice.queue.indexOf(item);
      if (index >= 0 && !item.loop) voice.queue.splice(index, 1);
      if (!voice.queue.length) {
        voice.playing = false;
        voice.nextStartTime = 0;
      }
    };
    source.start(nextStart, Math.min(item.offset, Math.max(0, item.buffer.duration - Number.EPSILON)));
    if (item.loop) {
      voice.nextStartTime = Number.POSITIVE_INFINITY;
      break;
    }
    nextStart = item.endTime;
    voice.nextStartTime = nextStart;
  }
}

export function play(handle) {
  const voice = requireObject(handle, "voice");
  if (!voice.queue.length) return;
  voice.playing = true;
  scheduleQueue(voice);
}

export function pause(handle) {
  const voice = requireObject(handle, "voice");
  if (!voice.playing) return;
  captureHeadOffset(voice);
  voice.playing = false;
  stopScheduledSources(voice);
  for (let i = 1; i < voice.queue.length; i++) voice.queue[i].offset = 0;
}

export function stop(handle) {
  const voice = requireObject(handle, "voice");
  voice.playing = false;
  stopScheduledSources(voice);
  voice.queue.length = 0;
}

export function setVolume(handle, volume) {
  const voice = requireObject(handle, "voice");
  voice.gain.gain.setValueAtTime(volume, voice.backend.context.currentTime);
}

export function setPitch(handle, pitch) {
  const voice = requireObject(handle, "voice");
  const restart = voice.playing && voice.queue.some(item => item.source);
  if (restart) captureHeadOffset(voice);
  if (restart) stopScheduledSources(voice);
  voice.pitch = pitch;
  if (restart) scheduleQueue(voice);
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
  stopScheduledSources(voice);
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
