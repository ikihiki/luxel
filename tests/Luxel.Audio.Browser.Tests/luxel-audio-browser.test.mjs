import assert from "node:assert/strict";
import test from "node:test";
import * as audio from "../../src/Luxel.Audio.Browser/wwwroot/luxel-audio-browser.js";

class MockAudioParam {
  constructor(value = 0) { this.value = value; }
  setValueAtTime(value) { this.value = value; }
}

class MockNode {
  connect(target) { this.target = target; return target; }
  disconnect() { this.disconnected = true; }
}

class MockBuffer {
  constructor(channels, frames, sampleRate) {
    this.duration = frames / sampleRate;
    this.channels = Array.from({ length: channels }, () => new Float32Array(frames));
  }
  getChannelData(channel) { return this.channels[channel]; }
}

class MockSource extends MockNode {
  constructor(context) {
    super();
    this.context = context;
    this.playbackRate = new MockAudioParam(1);
    this.loop = false;
    this.onended = null;
  }
  start(when, offset) {
    this.when = when;
    this.offset = offset;
    this.context.started.push(this);
  }
  stop() { this.stopped = true; }
  finish() { this.onended?.(); }
}

class MockAudioContext {
  static instances = [];
  constructor() {
    this.state = "suspended";
    this.currentTime = 0;
    this.destination = new MockNode();
    this.started = [];
    MockAudioContext.instances.push(this);
  }
  createGain() { const node = new MockNode(); node.gain = new MockAudioParam(1); return node; }
  createStereoPanner() { const node = new MockNode(); node.pan = new MockAudioParam(0); return node; }
  createBufferSource() { return new MockSource(this); }
  createBuffer(channels, frames, sampleRate) { return new MockBuffer(channels, frames, sampleRate); }
  async resume() { this.state = "running"; }
  async suspend() { this.state = "suspended"; }
  async close() { this.state = "closed"; }
}

globalThis.AudioContext = MockAudioContext;

const pcm16 = frames => new Uint8Array(frames * 2);

async function createVoice() {
  const backendInfo = JSON.parse(await audio.initialize());
  const context = MockAudioContext.instances.at(-1);
  const voice = audio.createVoice(backendInfo.handle, 100, 1, 16);
  return { backend: backendInfo.handle, context, voice };
}

test("reports observed lifecycle state", async () => {
  const { backend, context } = await createVoice();
  assert.equal(await audio.resume(backend), "running");
  assert.equal(context.state, "running");
  assert.equal(await audio.suspend(backend), "suspended");
  audio.disposeBackend(backend);
});

test("schedules queued buffers contiguously and drains them through ended events", async () => {
  const { backend, context, voice } = await createVoice();
  audio.submitBuffer(voice, pcm16(10), false);
  audio.submitBuffer(voice, pcm16(20), false);
  audio.play(voice);

  assert.equal(context.started.length, 2);
  assert.equal(context.started[0].when, 0.01);
  assert.equal(context.started[1].when, 0.11);
  assert.equal(audio.buffersQueued(voice), 2);

  context.started[0].finish();
  assert.equal(audio.buffersQueued(voice), 1);
  assert.equal(audio.isPlaying(voice), 1);
  context.started[1].finish();
  assert.equal(audio.buffersQueued(voice), 0);
  assert.equal(audio.isPlaying(voice), 0);
  audio.disposeBackend(backend);
});

test("pause and pitch changes rebuild one-shot nodes from the playback offset", async () => {
  const { backend, context, voice } = await createVoice();
  audio.submitBuffer(voice, pcm16(100), true);
  audio.play(voice);
  const first = context.started.at(-1);

  context.currentTime = 0.26;
  audio.pause(voice);
  assert.equal(first.stopped, true);
  assert.equal(audio.buffersQueued(voice), 1);

  audio.play(voice);
  const resumed = context.started.at(-1);
  assert.ok(Math.abs(resumed.offset - 0.25) < 1e-9);

  context.currentTime = 0.36;
  audio.setPitch(voice, 2);
  const repitched = context.started.at(-1);
  assert.equal(resumed.stopped, true);
  assert.ok(Math.abs(repitched.offset - 0.34) < 1e-9);
  assert.equal(repitched.playbackRate.value, 2);

  audio.stop(voice);
  assert.equal(audio.buffersQueued(voice), 0);
  assert.equal(audio.isPlaying(voice), 0);
  audio.disposeBackend(backend);
});
