import { dotnet } from "./_framework/dotnet.js";
import * as audio from "./luxel-audio-browser.js";

const status = document.getElementById("status");
const host = { setStatus: message => status.textContent = message };
const runtime = await dotnet.create();
runtime.setModuleImports("./luxel-audio-browser.js", audio);
runtime.setModuleImports("luxel-audio-sample-host", host);
const exports = await runtime.getAssemblyExports("LuxelAudioBrowser.dll");
const program = exports?.LuxelAudioBrowser?.Program || exports?.Program;
await runtime.runMain();

for (const id of ["frequency", "volume", "pan", "pitch"]) {
  const input = document.getElementById(id);
  const output = input.nextElementSibling;
  const update = () => output.value = id === "frequency" ? input.value : Number(input.value).toFixed(2);
  input.addEventListener("input", update); update();
}
const invoke = async action => {
  try { status.textContent = await action(); }
  catch (error) { status.textContent = error?.stack || String(error); }
};
document.getElementById("enable").addEventListener("click", () => invoke(() => program.EnableAudio()));
document.getElementById("play").addEventListener("click", () => invoke(() => program.PlayTone(
  Number(document.getElementById("frequency").value),
  Number(document.getElementById("volume").value),
  Number(document.getElementById("pan").value),
  Number(document.getElementById("pitch").value),
  document.getElementById("loop").checked)));
document.getElementById("pause").addEventListener("click", () => invoke(() => program.PauseTone()));
document.getElementById("resume").addEventListener("click", () => invoke(() => program.ResumeTone()));
document.getElementById("stop").addEventListener("click", () => invoke(() => program.StopTone()));
