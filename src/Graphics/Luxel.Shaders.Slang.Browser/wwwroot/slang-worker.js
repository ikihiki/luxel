import * as slang from "./slang-browser-runtime.js";

self.addEventListener("message", async event => {
  const message = event.data;
  if (!message || !Number.isSafeInteger(message.id) || typeof message.method !== "string" || !Array.isArray(message.args)) return;
  const operation = slang[message.method];
  if (typeof operation !== "function") {
    self.postMessage({ id: message.id, error: `Unknown Slang worker operation '${message.method}'.` });
    return;
  }
  try {
    self.postMessage({ id: message.id, result: await operation(...message.args) });
  } catch (error) {
    self.postMessage({ id: message.id, error: String(error?.message || error) });
  }
});
