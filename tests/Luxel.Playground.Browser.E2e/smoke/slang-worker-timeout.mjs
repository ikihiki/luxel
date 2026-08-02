import assert from 'node:assert/strict';

let generation = 0;
class FakeWorker {
  constructor() {
    this.generation = ++generation;
    this.listeners = new Map();
    this.terminated = false;
  }
  addEventListener(type, listener) { this.listeners.set(type, listener); }
  postMessage(message) {
    if (this.generation === 1) return;
    queueMicrotask(() => this.listeners.get('message')?.({ data: { id: message.id, result: { completion: true, recovered: true } } }));
  }
  terminate() { this.terminated = true; }
}
globalThis.Worker = FakeWorker;

const slang = await import(`../../../samples/LuxelPlaygroundBrowser/wwwroot/slang-browser.js?timeout-test=${Date.now()}`);
await assert.rejects(
  slang.compile(JSON.stringify({ requestId: 1, timeoutMs: 10 })),
  /timed out after 10 ms; the compiler worker was restarted/);
const recovered = await slang.capabilities();
assert.equal(recovered.recovered, true);
assert.equal(generation, 2, 'Expected the timed-out worker to be replaced.');
console.log('Slang worker timeout and recovery smoke test passed.');
