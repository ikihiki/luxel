(() => {
  "use strict";
  const vs = new URL("vendor/monaco/vs", document.baseURI).href.replace(/\/$/, "");
  window.MonacoEnvironment = { getWorkerUrl: () => `${vs}/editor/editor.worker.js` };
  window.LuxelMonacoReady = new Promise((resolve, reject) => {
    if (typeof window.require !== "function") { reject(new Error("Monaco AMD loader is unavailable.")); return; }
    window.require.config({ paths: { vs } });
    window.require(["vs/editor/editor.main"], () => resolve(window.monaco), reject);
  });
})();