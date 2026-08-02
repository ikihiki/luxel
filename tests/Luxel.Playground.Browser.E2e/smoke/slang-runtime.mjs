import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import * as slang from '../../../samples/LuxelPlaygroundBrowser/wwwroot/slang-browser-runtime.js';

const source = `[shader("compute")]
[numthreads(1, 1, 1)]
void main(uint3 tid : SV_DispatchThreadID) {}`;
const compilation = JSON.parse(await slang.compile(JSON.stringify({
  path: 'shader.slang',
  source,
  supportingFiles: {},
  programKind: 'compute',
  entryPoints: [{ name: 'main', stage: 'compute' }],
  defines: {}
})));
assert.equal(compilation.success, true, compilation.error);
assert.match(compilation.wgsl, /@compute/);

const includeSource = `#include "Includes/common.slangh"
#if FEATURE_VALUE != 42
#error FEATURE_VALUE was not defined
#endif
[shader("compute")]
[numthreads(1, 1, 1)]
void main(uint3 tid : SV_DispatchThreadID) { consume(includedValue()); }`;
const includeCompilation = JSON.parse(await slang.compile(JSON.stringify({
  path: 'Shaders/main.slang',
  source: includeSource,
  supportingFiles: {
    'Shaders/Includes/common.slangh': '#include "../Shared/value.slangh"\nvoid consume(float value) {}',
    'Shaders/Shared/value.slangh': 'float includedValue() { return 1.0; }'
  },
  programKind: 'compute',
  entryPoints: [{ name: 'main', stage: 'compute' }],
  defines: { FEATURE_VALUE: '42' }
})));
assert.equal(includeCompilation.success, true, includeCompilation.error);
assert.match(includeCompilation.wgsl, /@compute/);

const includeWorkspaceSource = `#include "Includes/common.slangh"
[shader("compute")]
[numthreads(1, 1, 1)]
void main(uint3 tid : SV_DispatchThreadID) { consume(includedValue()); }`;
const includeWorkspace = {
  revision: 4,
  files: [
    { id: 'root', path: 'Shaders/main.slang', language: 'slang', source: includeWorkspaceSource, version: 1 },
    { id: 'common', path: 'Shaders/Includes/common.slangh', language: 'slang', source: '#include "../Shared/value.slangh"\nvoid consume(float value) {}', version: 1 },
    { id: 'value', path: 'Shaders/Shared/value.slangh', language: 'slang', source: 'float includedValue() { return 1.0; }', version: 1 }
  ]
};
const includeAnalysis = await slang.analyzeWorkspace(includeWorkspace, includeWorkspace.files[0]);
assert.equal(includeAnalysis.diagnostics.length, 0, JSON.stringify(includeAnalysis.diagnostics));

const completionFile = { id: 'completion', path: 'completion.slang', language: 'slang', source: '\nvoid', version: 1 };
const completion = await slang.completeWorkspace({ revision: 1, files: [completionFile] }, completionFile, completionFile.source.length);
assert.ok(completion.items.some(item => item.label === 'void'), 'Expected Slang completion to include void.');

const hoverSource = 'struct Payload { float3 color; };\nvoid test(Payload value) { }';
const hoverFile = { id: 'hover', path: 'hover.slang', language: 'slang', source: hoverSource, version: 1 };
const hover = await slang.hoverWorkspace({ revision: 2, files: [hoverFile] }, hoverFile, hoverSource.lastIndexOf('Payload') + 2);
assert.match(hover?.markdown || '', /Payload/);

const diagnosticsSource = 'void broken( {';
const diagnosticsFile = { id: 'diagnostics', path: 'diagnostics.slang', language: 'slang', source: diagnosticsSource, version: 1 };
const analysis = await slang.analyzeWorkspace({ revision: 3, files: [diagnosticsFile] }, diagnosticsFile);
assert.ok(analysis.diagnostics.length > 0, 'Expected malformed Slang to produce diagnostics.');

const manifest = JSON.parse(await readFile(new URL('../../../samples/LuxelPlaygroundBrowser/wwwroot/slang/manifest.json', import.meta.url)));
for (const [file, expected] of Object.entries(manifest.files)) {
  const bytes = await readFile(new URL(`../../../samples/LuxelPlaygroundBrowser/wwwroot/slang/${file}`, import.meta.url));
  assert.equal(`sha256-${createHash('sha256').update(bytes).digest('hex')}`, expected);
}

console.log('Slang 2026.14 browser compiler and language-service smoke test passed.');
