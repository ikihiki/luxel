//! Licensed to the .NET Foundation under one or more agreements.
//! The .NET Foundation licenses this file to you under the MIT license.

var e=!1;const t=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,4,1,96,0,0,3,2,1,0,10,8,1,6,0,6,64,25,11,11])),o=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,15,1,13,0,65,1,253,15,65,2,253,15,253,128,2,11])),n=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,10,1,8,0,65,0,253,15,253,98,11])),r=Symbol.for("wasm promise_control");function i(e,t){let o=null;const n=new Promise((function(n,r){o={isDone:!1,promise:null,resolve:t=>{o.isDone||(o.isDone=!0,n(t),e&&e())},reject:e=>{o.isDone||(o.isDone=!0,r(e),t&&t())}}}));o.promise=n;const i=n;return i[r]=o,{promise:i,promise_control:o}}function s(e){return e[r]}function a(e){e&&function(e){return void 0!==e[r]}(e)||Be(!1,"Promise is not controllable")}const l="__mono_message__",c=["debug","log","trace","warn","info","error"],d="MONO_WASM: ";let u,f,m,g,p,h;function w(e){g=e}function b(e){if(Pe.diagnosticTracing){const t="function"==typeof e?e():e;console.debug(d+t)}}function y(e,...t){console.info(d+e,...t)}function v(e,...t){console.info(e,...t)}function E(e,...t){console.warn(d+e,...t)}function _(e,...t){if(t&&t.length>0&&t[0]&&"object"==typeof t[0]){if(t[0].silent)return;if(t[0].toString)return void console.error(d+e,t[0].toString())}console.error(d+e,...t)}function x(e,t,o){return function(...n){try{let r=n[0];if(void 0===r)r="undefined";else if(null===r)r="null";else if("function"==typeof r)r=r.toString();else if("string"!=typeof r)try{r=JSON.stringify(r)}catch(e){r=r.toString()}t(o?JSON.stringify({method:e,payload:r,arguments:n.slice(1)}):[e+r,...n.slice(1)])}catch(e){m.error(`proxyConsole failed: ${e}`)}}}function j(e,t,o){f=t,g=e,m={...t};const n=`${o}/console`.replace("https://","wss://").replace("http://","ws://");u=new WebSocket(n),u.addEventListener("error",A),u.addEventListener("close",S),function(){for(const e of c)f[e]=x(`console.${e}`,T,!0)}()}function R(e){let t=30;const o=()=>{u?0==u.bufferedAmount||0==t?(e&&v(e),function(){for(const e of c)f[e]=x(`console.${e}`,m.log,!1)}(),u.removeEventListener("error",A),u.removeEventListener("close",S),u.close(1e3,e),u=void 0):(t--,globalThis.setTimeout(o,100)):e&&m&&m.log(e)};o()}function T(e){u&&u.readyState===WebSocket.OPEN?u.send(e):m.log(e)}function A(e){m.error(`[${g}] proxy console websocket error: ${e}`,e)}function S(e){m.debug(`[${g}] proxy console websocket closed: ${e}`,e)}function D(){Pe.preferredIcuAsset=O(Pe.config);let e="invariant"==Pe.config.globalizationMode;if(!e)if(Pe.preferredIcuAsset)Pe.diagnosticTracing&&b("ICU data archive(s) available, disabling invariant mode");else{if("custom"===Pe.config.globalizationMode||"all"===Pe.config.globalizationMode||"sharded"===Pe.config.globalizationMode){const e="invariant globalization mode is inactive and no ICU data archives are available";throw _(`ERROR: ${e}`),new Error(e)}Pe.diagnosticTracing&&b("ICU data archive(s) not available, using invariant globalization mode"),e=!0,Pe.preferredIcuAsset=null}const t="DOTNET_SYSTEM_GLOBALIZATION_INVARIANT",o=Pe.config.environmentVariables;if(void 0===o[t]&&e&&(o[t]="1"),void 0===o.TZ)try{const e=Intl.DateTimeFormat().resolvedOptions().timeZone||null;e&&(o.TZ=e)}catch(e){y("failed to detect timezone, will fallback to UTC")}}function O(e){var t;if((null===(t=e.resources)||void 0===t?void 0:t.icu)&&"invariant"!=e.globalizationMode){const t=e.applicationCulture||(ke?globalThis.navigator&&globalThis.navigator.languages&&globalThis.navigator.languages[0]:Intl.DateTimeFormat().resolvedOptions().locale),o=e.resources.icu;let n=null;if("custom"===e.globalizationMode){if(o.length>=1)return o[0].name}else t&&"all"!==e.globalizationMode?"sharded"===e.globalizationMode&&(n=function(e){const t=e.split("-")[0];return"en"===t||["fr","fr-FR","it","it-IT","de","de-DE","es","es-ES"].includes(e)?"icudt_EFIGS.dat":["zh","ko","ja"].includes(t)?"icudt_CJK.dat":"icudt_no_CJK.dat"}(t)):n="icudt.dat";if(n)for(let e=0;e<o.length;e++){const t=o[e];if(t.virtualPath===n)return t.name}}return e.globalizationMode="invariant",null}(new Date).valueOf();const C=class{constructor(e){this.url=e}toString(){return this.url}};async function k(e,t){try{const o="function"==typeof globalThis.fetch;if(Se){const n=e.startsWith("file://");if(!n&&o)return globalThis.fetch(e,t||{credentials:"same-origin"});p||(h=Ne.require("url"),p=Ne.require("fs")),n&&(e=h.fileURLToPath(e));const r=await p.promises.readFile(e);return{ok:!0,headers:{length:0,get:()=>null},url:e,arrayBuffer:()=>r,json:()=>JSON.parse(r),text:()=>{throw new Error("NotImplementedException")}}}if(o)return globalThis.fetch(e,t||{credentials:"same-origin"});if("function"==typeof read)return{ok:!0,url:e,headers:{length:0,get:()=>null},arrayBuffer:()=>new Uint8Array(read(e,"binary")),json:()=>JSON.parse(read(e,"utf8")),text:()=>read(e,"utf8")}}catch(t){return{ok:!1,url:e,status:500,headers:{length:0,get:()=>null},statusText:"ERR28: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t},text:()=>{throw t}}}throw new Error("No fetch implementation available")}function I(e){return"string"!=typeof e&&Be(!1,"url must be a string"),!M(e)&&0!==e.indexOf("./")&&0!==e.indexOf("../")&&globalThis.URL&&globalThis.document&&globalThis.document.baseURI&&(e=new URL(e,globalThis.document.baseURI).toString()),e}const U=/^[a-zA-Z][a-zA-Z\d+\-.]*?:\/\//,P=/[a-zA-Z]:[\\/]/;function M(e){return Se||Ie?e.startsWith("/")||e.startsWith("\\")||-1!==e.indexOf("///")||P.test(e):U.test(e)}let L,N=0;const $=[],z=[],W=new Map,F={"js-module-threads":!0,"js-module-runtime":!0,"js-module-dotnet":!0,"js-module-native":!0,"js-module-diagnostics":!0},B={...F,"js-module-library-initializer":!0},V={...F,dotnetwasm:!0,heap:!0,manifest:!0},q={...B,manifest:!0},H={...B,dotnetwasm:!0},J={dotnetwasm:!0,symbols:!0},Z={...B,dotnetwasm:!0,symbols:!0},Q={symbols:!0};function G(e){return!("icu"==e.behavior&&e.name!=Pe.preferredIcuAsset)}function K(e,t,o){null!=t||(t=[]),Be(1==t.length,`Expect to have one ${o} asset in resources`);const n=t[0];return n.behavior=o,X(n),e.push(n),n}function X(e){V[e.behavior]&&W.set(e.behavior,e)}function Y(e){Be(V[e],`Unknown single asset behavior ${e}`);const t=W.get(e);if(t&&!t.resolvedUrl)if(t.resolvedUrl=Pe.locateFile(t.name),F[t.behavior]){const e=ge(t);e?("string"!=typeof e&&Be(!1,"loadBootResource response for 'dotnetjs' type should be a URL string"),t.resolvedUrl=e):t.resolvedUrl=ce(t.resolvedUrl,t.behavior)}else if("dotnetwasm"!==t.behavior)throw new Error(`Unknown single asset behavior ${e}`);return t}function ee(e){const t=Y(e);return Be(t,`Single asset for ${e} not found`),t}let te=!1;async function oe(){if(!te){te=!0,Pe.diagnosticTracing&&b("mono_download_assets");try{const e=[],t=[],o=(e,t)=>{!Z[e.behavior]&&G(e)&&Pe.expected_instantiated_assets_count++,!H[e.behavior]&&G(e)&&(Pe.expected_downloaded_assets_count++,t.push(se(e)))};for(const t of $)o(t,e);for(const e of z)o(e,t);Pe.allDownloadsQueued.promise_control.resolve(),Promise.all([...e,...t]).then((()=>{Pe.allDownloadsFinished.promise_control.resolve()})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),await Pe.runtimeModuleLoaded.promise;const n=async e=>{const t=await e;if(t.buffer){if(!Z[t.behavior]){t.buffer&&"object"==typeof t.buffer||Be(!1,"asset buffer must be array-like or buffer-like or promise of these"),"string"!=typeof t.resolvedUrl&&Be(!1,"resolvedUrl must be string");const e=t.resolvedUrl,o=await t.buffer,n=new Uint8Array(o);pe(t),await Ue.beforeOnRuntimeInitialized.promise,Ue.instantiate_asset(t,e,n)}}else J[t.behavior]?("symbols"===t.behavior&&(await Ue.instantiate_symbols_asset(t),pe(t)),J[t.behavior]&&++Pe.actual_downloaded_assets_count):(t.isOptional||Be(!1,"Expected asset to have the downloaded buffer"),!H[t.behavior]&&G(t)&&Pe.expected_downloaded_assets_count--,!Z[t.behavior]&&G(t)&&Pe.expected_instantiated_assets_count--)},r=[],i=[];for(const t of e)r.push(n(t));for(const e of t)i.push(n(e));Promise.all(r).then((()=>{Ce||Ue.coreAssetsInMemory.promise_control.resolve()})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),Promise.all(i).then((async()=>{Ce||(await Ue.coreAssetsInMemory.promise,Ue.allAssetsInMemory.promise_control.resolve())})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e}))}catch(e){throw Pe.err("Error in mono_download_assets: "+e),e}}}let ne=!1;function re(){if(ne)return;ne=!0;const e=Pe.config,t=[];if(e.assets)for(const t of e.assets)"object"!=typeof t&&Be(!1,`asset must be object, it was ${typeof t} : ${t}`),"string"!=typeof t.behavior&&Be(!1,"asset behavior must be known string"),"string"!=typeof t.name&&Be(!1,"asset name must be string"),t.resolvedUrl&&"string"!=typeof t.resolvedUrl&&Be(!1,"asset resolvedUrl could be string"),t.hash&&"string"!=typeof t.hash&&Be(!1,"asset resolvedUrl could be string"),t.pendingDownload&&"object"!=typeof t.pendingDownload&&Be(!1,"asset pendingDownload could be object"),t.isCore?$.push(t):z.push(t),X(t);else if(e.resources){const o=e.resources;o.wasmNative||Be(!1,"resources.wasmNative must be defined"),o.jsModuleNative||Be(!1,"resources.jsModuleNative must be defined"),o.jsModuleRuntime||Be(!1,"resources.jsModuleRuntime must be defined"),K(z,o.wasmNative,"dotnetwasm"),K(t,o.jsModuleNative,"js-module-native"),K(t,o.jsModuleRuntime,"js-module-runtime"),o.jsModuleDiagnostics&&K(t,o.jsModuleDiagnostics,"js-module-diagnostics");const n=(e,t,o)=>{const n=e;n.behavior=t,o?(n.isCore=!0,$.push(n)):z.push(n)};if(o.coreAssembly)for(let e=0;e<o.coreAssembly.length;e++)n(o.coreAssembly[e],"assembly",!0);if(o.assembly)for(let e=0;e<o.assembly.length;e++)n(o.assembly[e],"assembly",!o.coreAssembly);if(0!=e.debugLevel&&Pe.isDebuggingSupported()){if(o.corePdb)for(let e=0;e<o.corePdb.length;e++)n(o.corePdb[e],"pdb",!0);if(o.pdb)for(let e=0;e<o.pdb.length;e++)n(o.pdb[e],"pdb",!o.corePdb)}if(e.loadAllSatelliteResources&&o.satelliteResources)for(const e in o.satelliteResources)for(let t=0;t<o.satelliteResources[e].length;t++){const r=o.satelliteResources[e][t];r.culture=e,n(r,"resource",!o.coreAssembly)}if(o.coreVfs)for(let e=0;e<o.coreVfs.length;e++)n(o.coreVfs[e],"vfs",!0);if(o.vfs)for(let e=0;e<o.vfs.length;e++)n(o.vfs[e],"vfs",!o.coreVfs);const r=O(e);if(r&&o.icu)for(let e=0;e<o.icu.length;e++){const t=o.icu[e];t.name===r&&n(t,"icu",!1)}if(o.wasmSymbols)for(let e=0;e<o.wasmSymbols.length;e++)n(o.wasmSymbols[e],"symbols",!1)}if(e.appsettings)for(let t=0;t<e.appsettings.length;t++){const o=e.appsettings[t],n=he(o);"appsettings.json"!==n&&n!==`appsettings.${e.applicationEnvironment}.json`||z.push({name:o,behavior:"vfs",cache:"no-cache",useCredentials:!0})}e.assets=[...$,...z,...t]}async function ie(e){const t=await se(e);return await t.pendingDownloadInternal.response,t.buffer}async function se(e){try{return await ae(e)}catch(t){if(!Pe.enableDownloadRetry)throw t;if(Ie||Se)throw t;if(e.pendingDownload&&e.pendingDownloadInternal==e.pendingDownload)throw t;if(e.resolvedUrl&&-1!=e.resolvedUrl.indexOf("file://"))throw t;if(t&&404==t.status)throw t;e.pendingDownloadInternal=void 0,await Pe.allDownloadsQueued.promise;try{return Pe.diagnosticTracing&&b(`Retrying download '${e.name}'`),await ae(e)}catch(t){return e.pendingDownloadInternal=void 0,await new Promise((e=>globalThis.setTimeout(e,100))),Pe.diagnosticTracing&&b(`Retrying download (2) '${e.name}' after delay`),await ae(e)}}}async function ae(e){for(;L;)await L.promise;try{++N,N==Pe.maxParallelDownloads&&(Pe.diagnosticTracing&&b("Throttling further parallel downloads"),L=i());const t=await async function(e){if(e.pendingDownload&&(e.pendingDownloadInternal=e.pendingDownload),e.pendingDownloadInternal&&e.pendingDownloadInternal.response)return e.pendingDownloadInternal.response;if(e.buffer){const t=await e.buffer;return e.resolvedUrl||(e.resolvedUrl="undefined://"+e.name),e.pendingDownloadInternal={url:e.resolvedUrl,name:e.name,response:Promise.resolve({ok:!0,arrayBuffer:()=>t,json:()=>JSON.parse(new TextDecoder("utf-8").decode(t)),text:()=>{throw new Error("NotImplementedException")},headers:{get:()=>{}}})},e.pendingDownloadInternal.response}const t=e.loadRemote&&Pe.config.remoteSources?Pe.config.remoteSources:[""];let o;for(let n of t){n=n.trim(),"./"===n&&(n="");const t=le(e,n);e.name===t?Pe.diagnosticTracing&&b(`Attempting to download '${t}'`):Pe.diagnosticTracing&&b(`Attempting to download '${t}' for ${e.name}`);try{e.resolvedUrl=t;const n=fe(e);if(e.pendingDownloadInternal=n,o=await n.response,!o||!o.ok)continue;return o}catch(e){o||(o={ok:!1,url:t,status:0,statusText:""+e});continue}}const n=e.isOptional||e.name.match(/\.pdb$/)&&Pe.config.ignorePdbLoadErrors;if(o||Be(!1,`Response undefined ${e.name}`),!n){const t=new Error(`download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`);throw t.status=o.status,t}y(`optional download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`)}(e);return t?(J[e.behavior]||(e.buffer=await t.arrayBuffer(),++Pe.actual_downloaded_assets_count),e):e}finally{if(--N,L&&N==Pe.maxParallelDownloads-1){Pe.diagnosticTracing&&b("Resuming more parallel downloads");const e=L;L=void 0,e.promise_control.resolve()}}}function le(e,t){let o;return null==t&&Be(!1,`sourcePrefix must be provided for ${e.name}`),e.resolvedUrl?o=e.resolvedUrl:(o=""===t?"assembly"===e.behavior||"pdb"===e.behavior?e.name:"resource"===e.behavior&&e.culture&&""!==e.culture?`${e.culture}/${e.name}`:e.name:t+e.name,o=ce(Pe.locateFile(o),e.behavior)),o&&"string"==typeof o||Be(!1,"attemptUrl need to be path or url string"),o}function ce(e,t){return Pe.modulesUniqueQuery&&q[t]&&(e+=Pe.modulesUniqueQuery),e}let de=0;const ue=new Set;function fe(e){try{e.resolvedUrl||Be(!1,"Request's resolvedUrl must be set");const t=function(e){let t=e.resolvedUrl;if(Pe.loadBootResource){const o=ge(e);if(o instanceof Promise)return o;"string"==typeof o&&(t=o)}const o={};return e.cache?o.cache=e.cache:Pe.config.disableNoCacheFetch||(o.cache="no-cache"),e.useCredentials?o.credentials="include":!Pe.config.disableIntegrityCheck&&e.hash&&(o.integrity=e.hash),Pe.fetch_like(t,o)}(e),o={name:e.name,url:e.resolvedUrl,response:t};return ue.add(e.name),o.response.then((()=>{"assembly"==e.behavior&&Pe.loadedAssemblies.push(e.name),de++,Pe.onDownloadResourceProgress&&Pe.onDownloadResourceProgress(de,ue.size)})),o}catch(t){const o={ok:!1,url:e.resolvedUrl,status:500,statusText:"ERR29: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t}};return{name:e.name,url:e.resolvedUrl,response:Promise.resolve(o)}}}const me={resource:"assembly",assembly:"assembly",pdb:"pdb",icu:"globalization",vfs:"configuration",manifest:"manifest",dotnetwasm:"dotnetwasm","js-module-dotnet":"dotnetjs","js-module-native":"dotnetjs","js-module-runtime":"dotnetjs","js-module-threads":"dotnetjs"};function ge(e){var t;if(Pe.loadBootResource){const o=null!==(t=e.hash)&&void 0!==t?t:"",n=e.resolvedUrl,r=me[e.behavior];if(r){const t=Pe.loadBootResource(r,e.name,n,o,e.behavior);return"string"==typeof t?I(t):t}}}function pe(e){e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null}function he(e){let t=e.lastIndexOf("/");return t>=0&&t++,e.substring(t)}async function we(e){e&&await Promise.all((null!=e?e:[]).map((e=>async function(e){try{const t=e.name;if(!e.moduleExports){const o=ce(Pe.locateFile(t),"js-module-library-initializer");Pe.diagnosticTracing&&b(`Attempting to import '${o}' for ${e}`),e.moduleExports=await import(/*! webpackIgnore: true */o)}Pe.libraryInitializers.push({scriptName:t,exports:e.moduleExports})}catch(t){E(`Failed to import library initializer '${e}': ${t}`)}}(e))))}async function be(e,t){if(!Pe.libraryInitializers)return;const o=[];for(let n=0;n<Pe.libraryInitializers.length;n++){const r=Pe.libraryInitializers[n];r.exports[e]&&o.push(ye(r.scriptName,e,(()=>r.exports[e](...t))))}await Promise.all(o)}async function ye(e,t,o){try{await o()}catch(o){throw E(`Failed to invoke '${t}' on library initializer '${e}': ${o}`),Xe(1,o),o}}function ve(e,t){if(e===t)return e;const o={...t};return void 0!==o.assets&&o.assets!==e.assets&&(o.assets=[...e.assets||[],...o.assets||[]]),void 0!==o.resources&&(o.resources=_e(e.resources||{assembly:[],jsModuleNative:[],jsModuleRuntime:[],wasmNative:[]},o.resources)),void 0!==o.environmentVariables&&(o.environmentVariables={...e.environmentVariables||{},...o.environmentVariables||{}}),void 0!==o.runtimeOptions&&o.runtimeOptions!==e.runtimeOptions&&(o.runtimeOptions=[...e.runtimeOptions||[],...o.runtimeOptions||[]]),Object.assign(e,o)}function Ee(e,t){if(e===t)return e;const o={...t};return o.config&&(e.config||(e.config={}),o.config=ve(e.config,o.config)),Object.assign(e,o)}function _e(e,t){if(e===t)return e;const o={...t};return void 0!==o.coreAssembly&&(o.coreAssembly=[...e.coreAssembly||[],...o.coreAssembly||[]]),void 0!==o.assembly&&(o.assembly=[...e.assembly||[],...o.assembly||[]]),void 0!==o.lazyAssembly&&(o.lazyAssembly=[...e.lazyAssembly||[],...o.lazyAssembly||[]]),void 0!==o.corePdb&&(o.corePdb=[...e.corePdb||[],...o.corePdb||[]]),void 0!==o.pdb&&(o.pdb=[...e.pdb||[],...o.pdb||[]]),void 0!==o.jsModuleWorker&&(o.jsModuleWorker=[...e.jsModuleWorker||[],...o.jsModuleWorker||[]]),void 0!==o.jsModuleNative&&(o.jsModuleNative=[...e.jsModuleNative||[],...o.jsModuleNative||[]]),void 0!==o.jsModuleDiagnostics&&(o.jsModuleDiagnostics=[...e.jsModuleDiagnostics||[],...o.jsModuleDiagnostics||[]]),void 0!==o.jsModuleRuntime&&(o.jsModuleRuntime=[...e.jsModuleRuntime||[],...o.jsModuleRuntime||[]]),void 0!==o.wasmSymbols&&(o.wasmSymbols=[...e.wasmSymbols||[],...o.wasmSymbols||[]]),void 0!==o.wasmNative&&(o.wasmNative=[...e.wasmNative||[],...o.wasmNative||[]]),void 0!==o.icu&&(o.icu=[...e.icu||[],...o.icu||[]]),void 0!==o.satelliteResources&&(o.satelliteResources=function(e,t){if(e===t)return e;for(const o in t)e[o]=[...e[o]||[],...t[o]||[]];return e}(e.satelliteResources||{},o.satelliteResources||{})),void 0!==o.modulesAfterConfigLoaded&&(o.modulesAfterConfigLoaded=[...e.modulesAfterConfigLoaded||[],...o.modulesAfterConfigLoaded||[]]),void 0!==o.modulesAfterRuntimeReady&&(o.modulesAfterRuntimeReady=[...e.modulesAfterRuntimeReady||[],...o.modulesAfterRuntimeReady||[]]),void 0!==o.extensions&&(o.extensions={...e.extensions||{},...o.extensions||{}}),void 0!==o.vfs&&(o.vfs=[...e.vfs||[],...o.vfs||[]]),Object.assign(e,o)}function xe(){const e=Pe.config;if(e.environmentVariables=e.environmentVariables||{},e.runtimeOptions=e.runtimeOptions||[],e.resources=e.resources||{assembly:[],jsModuleNative:[],jsModuleWorker:[],jsModuleRuntime:[],wasmNative:[],vfs:[],satelliteResources:{}},e.assets){Pe.diagnosticTracing&&b("config.assets is deprecated, use config.resources instead");for(const t of e.assets){const o={};switch(t.behavior){case"assembly":o.assembly=[t];break;case"pdb":o.pdb=[t];break;case"resource":o.satelliteResources={},o.satelliteResources[t.culture]=[t];break;case"icu":o.icu=[t];break;case"symbols":o.wasmSymbols=[t];break;case"vfs":o.vfs=[t];break;case"dotnetwasm":o.wasmNative=[t];break;case"js-module-threads":o.jsModuleWorker=[t];break;case"js-module-runtime":o.jsModuleRuntime=[t];break;case"js-module-native":o.jsModuleNative=[t];break;case"js-module-diagnostics":o.jsModuleDiagnostics=[t];break;case"js-module-dotnet":break;default:throw new Error(`Unexpected behavior ${t.behavior} of asset ${t.name}`)}_e(e.resources,o)}}e.debugLevel,e.applicationEnvironment||(e.applicationEnvironment="Production"),e.applicationCulture&&(e.environmentVariables.LANG=`${e.applicationCulture}.UTF-8`),Ue.diagnosticTracing=Pe.diagnosticTracing=!!e.diagnosticTracing,Ue.waitForDebugger=e.waitForDebugger,Pe.maxParallelDownloads=e.maxParallelDownloads||Pe.maxParallelDownloads,Pe.enableDownloadRetry=void 0!==e.enableDownloadRetry?e.enableDownloadRetry:Pe.enableDownloadRetry}let je=!1;async function Re(e){var t;if(je)return void await Pe.afterConfigLoaded.promise;let o;try{if(e.configSrc||Pe.config&&0!==Object.keys(Pe.config).length&&(Pe.config.assets||Pe.config.resources)||(e.configSrc="dotnet.boot.js"),o=e.configSrc,je=!0,o&&(Pe.diagnosticTracing&&b("mono_wasm_load_config"),await async function(e){const t=e.configSrc,o=Pe.locateFile(t);let n=null;void 0!==Pe.loadBootResource&&(n=Pe.loadBootResource("manifest",t,o,"","manifest"));let r,i=null;if(n)if("string"==typeof n)n.includes(".json")?(i=await s(I(n)),r=await Ae(i)):r=(await import(I(n))).config;else{const e=await n;"function"==typeof e.json?(i=e,r=await Ae(i)):r=e.config}else o.includes(".json")?(i=await s(ce(o,"manifest")),r=await Ae(i)):r=(await import(ce(o,"manifest"))).config;function s(e){return Pe.fetch_like(e,{method:"GET",credentials:"include",cache:"no-cache"})}Pe.config.applicationEnvironment&&(r.applicationEnvironment=Pe.config.applicationEnvironment),ve(Pe.config,r)}(e)),xe(),await we(null===(t=Pe.config.resources)||void 0===t?void 0:t.modulesAfterConfigLoaded),await be("onRuntimeConfigLoaded",[Pe.config]),e.onConfigLoaded)try{await e.onConfigLoaded(Pe.config,Le),xe()}catch(e){throw _("onConfigLoaded() failed",e),e}xe(),Pe.afterConfigLoaded.promise_control.resolve(Pe.config)}catch(t){const n=`Failed to load config file ${o} ${t} ${null==t?void 0:t.stack}`;throw Pe.config=e.config=Object.assign(Pe.config,{message:n,error:t,isError:!0}),Xe(1,new Error(n)),t}}function Te(){return!!globalThis.navigator&&(Pe.isChromium||Pe.isFirefox)}async function Ae(e){const t=Pe.config,o=await e.json();t.applicationEnvironment||o.applicationEnvironment||(o.applicationEnvironment=e.headers.get("Blazor-Environment")||e.headers.get("DotNet-Environment")||void 0),o.environmentVariables||(o.environmentVariables={});const n=e.headers.get("DOTNET-MODIFIABLE-ASSEMBLIES");n&&(o.environmentVariables.DOTNET_MODIFIABLE_ASSEMBLIES=n);const r=e.headers.get("ASPNETCORE-BROWSER-TOOLS");return r&&(o.environmentVariables.__ASPNETCORE_BROWSER_TOOLS=r),o}"function"!=typeof importScripts||globalThis.onmessage||(globalThis.dotnetSidecar=!0);const Se="object"==typeof process&&"object"==typeof process.versions&&"string"==typeof process.versions.node,De="function"==typeof importScripts,Oe=De&&"undefined"!=typeof dotnetSidecar,Ce=De&&!Oe,ke="object"==typeof window||De&&!Se,Ie=!ke&&!Se;let Ue={},Pe={},Me={},Le={},Ne={},$e=!1;const ze={},We={config:ze},Fe={mono:{},binding:{},internal:Ne,module:We,loaderHelpers:Pe,runtimeHelpers:Ue,diagnosticHelpers:Me,api:Le};function Be(e,t){if(e)return;const o="Assert failed: "+("function"==typeof t?t():t),n=new Error(o);_(o,n),Ue.nativeAbort(n)}function Ve(){return void 0!==Pe.exitCode}function qe(){return Ue.runtimeReady&&!Ve()}function He(){Ve()&&Be(!1,`.NET runtime already exited with ${Pe.exitCode} ${Pe.exitReason}. You can use runtime.runMain() which doesn't exit the runtime.`),Ue.runtimeReady||Be(!1,".NET runtime didn't start yet. Please call dotnet.create() first.")}function Je(){ke&&(globalThis.addEventListener("unhandledrejection",et),globalThis.addEventListener("error",tt))}let Ze,Qe;function Ge(e){Qe&&Qe(e),Xe(e,Pe.exitReason)}function Ke(e){Ze&&Ze(e||Pe.exitReason),Xe(1,e||Pe.exitReason)}function Xe(t,o){var n,r;const i=o&&"object"==typeof o;t=i&&"number"==typeof o.status?o.status:void 0===t?-1:t;const s=i&&"string"==typeof o.message?o.message:""+o;(o=i?o:Ue.ExitStatus?function(e,t){const o=new Ue.ExitStatus(e);return o.message=t,o.toString=()=>t,o}(t,s):new Error("Exit with code "+t+" "+s)).status=t,o.message||(o.message=s);const a=""+(o.stack||(new Error).stack);try{Object.defineProperty(o,"stack",{get:()=>a})}catch(e){}const l=!!o.silent;if(o.silent=!0,Ve())Pe.diagnosticTracing&&b("mono_exit called after exit");else{try{We.onAbort==Ke&&(We.onAbort=Ze),We.onExit==Ge&&(We.onExit=Qe),ke&&(globalThis.removeEventListener("unhandledrejection",et),globalThis.removeEventListener("error",tt)),Ue.runtimeReady?(Ue.jiterpreter_dump_stats&&Ue.jiterpreter_dump_stats(!1),0===t&&(null===(n=Pe.config)||void 0===n?void 0:n.interopCleanupOnExit)&&Ue.forceDisposeProxies(!0,!0),e&&0!==t&&(null===(r=Pe.config)||void 0===r||r.dumpThreadsOnNonZeroExit)):(Pe.diagnosticTracing&&b(`abort_startup, reason: ${o}`),function(e){Pe.allDownloadsQueued.promise_control.reject(e),Pe.allDownloadsFinished.promise_control.reject(e),Pe.afterConfigLoaded.promise_control.reject(e),Pe.wasmCompilePromise.promise_control.reject(e),Pe.runtimeModuleLoaded.promise_control.reject(e),Ue.dotnetReady&&(Ue.dotnetReady.promise_control.reject(e),Ue.afterInstantiateWasm.promise_control.reject(e),Ue.beforePreInit.promise_control.reject(e),Ue.afterPreInit.promise_control.reject(e),Ue.afterPreRun.promise_control.reject(e),Ue.beforeOnRuntimeInitialized.promise_control.reject(e),Ue.afterOnRuntimeInitialized.promise_control.reject(e),Ue.afterPostRun.promise_control.reject(e))}(o))}catch(e){E("mono_exit A failed",e)}try{l||(function(e,t){if(0!==e&&t){const e=Ue.ExitStatus&&t instanceof Ue.ExitStatus?b:_;"string"==typeof t?e(t):(void 0===t.stack&&(t.stack=(new Error).stack+""),t.message?e(Ue.stringify_as_error_with_stack?Ue.stringify_as_error_with_stack(t.message+"\n"+t.stack):t.message+"\n"+t.stack):e(JSON.stringify(t)))}!Ce&&Pe.config&&(Pe.config.logExitCode?Pe.config.forwardConsoleLogsToWS?R("WASM EXIT "+e):v("WASM EXIT "+e):Pe.config.forwardConsoleLogsToWS&&R())}(t,o),function(e){if(ke&&!Ce&&Pe.config&&Pe.config.appendElementOnExit&&document){const t=document.createElement("label");t.id="tests_done",0!==e&&(t.style.background="red"),t.innerHTML=""+e,document.body.appendChild(t)}}(t))}catch(e){E("mono_exit B failed",e)}Pe.exitCode=t,Pe.exitReason||(Pe.exitReason=o),!Ce&&Ue.runtimeReady&&We.runtimeKeepalivePop()}if(Pe.config&&Pe.config.asyncFlushOnExit&&0===t)throw(async()=>{try{await async function(){try{const e=await import(/*! webpackIgnore: true */"process"),t=e=>new Promise(((t,o)=>{e.on("error",o),e.end("","utf8",t)})),o=t(e.stderr),n=t(e.stdout);let r;const i=new Promise((e=>{r=setTimeout((()=>e("timeout")),1e3)}));await Promise.race([Promise.all([n,o]),i]),clearTimeout(r)}catch(e){_(`flushing std* streams failed: ${e}`)}}()}finally{Ye(t,o)}})(),o;Ye(t,o)}function Ye(e,t){if(Ue.runtimeReady&&Ue.nativeExit)try{Ue.nativeExit(e)}catch(e){!Ue.ExitStatus||e instanceof Ue.ExitStatus||E("set_exit_code_and_quit_now failed: "+e.toString())}if(0!==e||!ke)throw Se&&Ne.process?Ne.process.exit(e):Ue.quit&&Ue.quit(e,t),t}function et(e){ot(e,e.reason,"rejection")}function tt(e){ot(e,e.error,"error")}function ot(e,t,o){e.preventDefault();try{t||(t=new Error("Unhandled "+o)),void 0===t.stack&&(t.stack=(new Error).stack),t.stack=t.stack+"",t.silent||(_("Unhandled error:",t),Xe(1,t))}catch(e){}}!function(e){if($e)throw new Error("Loader module already loaded");$e=!0,Ue=e.runtimeHelpers,Pe=e.loaderHelpers,Me=e.diagnosticHelpers,Le=e.api,Ne=e.internal,Object.assign(Le,{INTERNAL:Ne,invokeLibraryInitializers:be}),Object.assign(e.module,{config:ve(ze,{environmentVariables:{}})});const r={mono_wasm_bindings_is_ready:!1,config:e.module.config,diagnosticTracing:!1,nativeAbort:e=>{throw e||new Error("abort")},nativeExit:e=>{throw new Error("exit:"+e)}},l={gitHash:"f7d90799ce4ef09a0bb257852a57248d2a8fb8dd",config:e.module.config,diagnosticTracing:!1,maxParallelDownloads:16,enableDownloadRetry:!0,_loaded_files:[],loadedFiles:[],loadedAssemblies:[],libraryInitializers:[],workerNextNumber:1,actual_downloaded_assets_count:0,actual_instantiated_assets_count:0,expected_downloaded_assets_count:0,expected_instantiated_assets_count:0,afterConfigLoaded:i(),allDownloadsQueued:i(),allDownloadsFinished:i(),wasmCompilePromise:i(),runtimeModuleLoaded:i(),loadingWorkers:i(),is_exited:Ve,is_runtime_running:qe,assert_runtime_running:He,mono_exit:Xe,createPromiseController:i,getPromiseController:s,assertIsControllablePromise:a,mono_download_assets:oe,resolve_single_asset_path:ee,setup_proxy_console:j,set_thread_prefix:w,installUnhandledErrorHandler:Je,retrieve_asset_download:ie,invokeLibraryInitializers:be,isDebuggingSupported:Te,exceptions:t,simd:n,relaxedSimd:o};Object.assign(Ue,r),Object.assign(Pe,l)}(Fe);let nt,rt,it,st=!1,at=!1;async function lt(e){if(!at){if(at=!0,ke&&Pe.config.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&j("main",globalThis.console,globalThis.location.origin),We||Be(!1,"Null moduleConfig"),Pe.config||Be(!1,"Null moduleConfig.config"),"function"==typeof e){const t=e(Fe.api);if(t.ready)throw new Error("Module.ready couldn't be redefined.");Object.assign(We,t),Ee(We,t)}else{if("object"!=typeof e)throw new Error("Can't use moduleFactory callback of createDotnetRuntime function.");Ee(We,e)}await async function(e){if(Se){const e=await import(/*! webpackIgnore: true */"process"),t=14;if(e.versions.node.split(".")[0]<t)throw new Error(`NodeJS at '${e.execPath}' has too low version '${e.versions.node}', please use at least ${t}. See also https://aka.ms/dotnet-wasm-features`)}const t=/*! webpackIgnore: true */import.meta.url,o=t.indexOf("?");var n;if(o>0&&(Pe.modulesUniqueQuery=t.substring(o)),Pe.scriptUrl=t.replace(/\\/g,"/").replace(/[?#].*/,""),Pe.scriptDirectory=(n=Pe.scriptUrl).slice(0,n.lastIndexOf("/"))+"/",Pe.locateFile=e=>"URL"in globalThis&&globalThis.URL!==C?new URL(e,Pe.scriptDirectory).toString():M(e)?e:Pe.scriptDirectory+e,Pe.fetch_like=k,Pe.out=console.log,Pe.err=console.error,Pe.onDownloadResourceProgress=e.onDownloadResourceProgress,ke&&globalThis.navigator){const e=globalThis.navigator,t=e.userAgentData&&e.userAgentData.brands;t&&t.length>0?Pe.isChromium=t.some((e=>"Google Chrome"===e.brand||"Microsoft Edge"===e.brand||"Chromium"===e.brand)):e.userAgent&&(Pe.isChromium=e.userAgent.includes("Chrome"),Pe.isFirefox=e.userAgent.includes("Firefox"))}Ne.require=Se?await import(/*! webpackIgnore: true */"module").then((e=>e.createRequire(/*! webpackIgnore: true */import.meta.url))):Promise.resolve((()=>{throw new Error("require not supported")})),void 0===globalThis.URL&&(globalThis.URL=C)}(We)}}async function ct(e){return await lt(e),Ze=We.onAbort,Qe=We.onExit,We.onAbort=Ke,We.onExit=Ge,We.ENVIRONMENT_IS_PTHREAD?async function(){(function(){const e=new MessageChannel,t=e.port1,o=e.port2;t.addEventListener("message",(e=>{var n,r;n=JSON.parse(e.data.config),r=JSON.parse(e.data.monoThreadInfo),st?Pe.diagnosticTracing&&b("mono config already received"):(ve(Pe.config,n),Ue.monoThreadInfo=r,xe(),Pe.diagnosticTracing&&b("mono config received"),st=!0,Pe.afterConfigLoaded.promise_control.resolve(Pe.config),ke&&n.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&Pe.setup_proxy_console("worker-idle",console,globalThis.location.origin)),t.close(),o.close()}),{once:!0}),t.start(),self.postMessage({[l]:{monoCmd:"preload",port:o}},[o])})(),await Pe.afterConfigLoaded.promise,function(){const e=Pe.config;e.assets||Be(!1,"config.assets must be defined");for(const t of e.assets)X(t),Q[t.behavior]&&z.push(t)}(),setTimeout((async()=>{try{await oe()}catch(e){Xe(1,e)}}),0);const e=dt(),t=await Promise.all(e);return await ut(t),We}():async function(){var e;await Re(We),re();const t=dt();(async function(){try{const e=ee("dotnetwasm");await se(e),e&&e.pendingDownloadInternal&&e.pendingDownloadInternal.response||Be(!1,"Can't load dotnet.native.wasm");const t=await e.pendingDownloadInternal.response,o=t.headers&&t.headers.get?t.headers.get("Content-Type"):void 0;let n;if("function"==typeof WebAssembly.compileStreaming&&"application/wasm"===o)n=await WebAssembly.compileStreaming(t);else{ke&&"application/wasm"!==o&&E('WebAssembly resource does not have the expected content type "application/wasm", so falling back to slower ArrayBuffer instantiation.');const e=await t.arrayBuffer();Pe.diagnosticTracing&&b("instantiate_wasm_module buffered"),n=Ie?await Promise.resolve(new WebAssembly.Module(e)):await WebAssembly.compile(e)}e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null,Pe.wasmCompilePromise.promise_control.resolve(n)}catch(e){Pe.wasmCompilePromise.promise_control.reject(e)}})(),setTimeout((async()=>{try{D(),await oe()}catch(e){Xe(1,e)}}),0);const o=await Promise.all(t);return await ut(o),await Ue.dotnetReady.promise,await we(null===(e=Pe.config.resources)||void 0===e?void 0:e.modulesAfterRuntimeReady),await be("onRuntimeReady",[Fe.api]),Le}()}function dt(){const e=ee("js-module-runtime"),t=ee("js-module-native");if(nt&&rt)return[nt,rt,it];"object"==typeof e.moduleExports?nt=e.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${e.resolvedUrl}' for ${e.name}`),nt=import(/*! webpackIgnore: true */e.resolvedUrl)),"object"==typeof t.moduleExports?rt=t.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${t.resolvedUrl}' for ${t.name}`),rt=import(/*! webpackIgnore: true */t.resolvedUrl));const o=Y("js-module-diagnostics");return o&&("object"==typeof o.moduleExports?it=o.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${o.resolvedUrl}' for ${o.name}`),it=import(/*! webpackIgnore: true */o.resolvedUrl))),[nt,rt,it]}async function ut(e){const{initializeExports:t,initializeReplacements:o,configureRuntimeStartup:n,configureEmscriptenStartup:r,configureWorkerStartup:i,setRuntimeGlobals:s,passEmscriptenInternals:a}=e[0],{default:l}=e[1],c=e[2];s(Fe),t(Fe),c&&c.setRuntimeGlobals(Fe),await n(We),Pe.runtimeModuleLoaded.promise_control.resolve(),l((e=>(Object.assign(We,{ready:e.ready,__dotnet_runtime:{initializeReplacements:o,configureEmscriptenStartup:r,configureWorkerStartup:i,passEmscriptenInternals:a}}),We))).catch((e=>{if(e.message&&e.message.toLowerCase().includes("out of memory"))throw new Error(".NET runtime has failed to start, because too much memory was requested. Please decrease the memory by adjusting EmccMaximumHeapSize. See also https://aka.ms/dotnet-wasm-features");throw e}))}const ft=new class{withModuleConfig(e){try{return Ee(We,e),this}catch(e){throw Xe(1,e),e}}withOnConfigLoaded(e){try{return Ee(We,{onConfigLoaded:e}),this}catch(e){throw Xe(1,e),e}}withConsoleForwarding(){try{return ve(ze,{forwardConsoleLogsToWS:!0}),this}catch(e){throw Xe(1,e),e}}withExitOnUnhandledError(){try{return ve(ze,{exitOnUnhandledError:!0}),Je(),this}catch(e){throw Xe(1,e),e}}withAsyncFlushOnExit(){try{return ve(ze,{asyncFlushOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withExitCodeLogging(){try{return ve(ze,{logExitCode:!0}),this}catch(e){throw Xe(1,e),e}}withElementOnExit(){try{return ve(ze,{appendElementOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withInteropCleanupOnExit(){try{return ve(ze,{interopCleanupOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withDumpThreadsOnNonZeroExit(){try{return ve(ze,{dumpThreadsOnNonZeroExit:!0}),this}catch(e){throw Xe(1,e),e}}withWaitingForDebugger(e){try{return ve(ze,{waitForDebugger:e}),this}catch(e){throw Xe(1,e),e}}withInterpreterPgo(e,t){try{return ve(ze,{interpreterPgo:e,interpreterPgoSaveDelay:t}),ze.runtimeOptions?ze.runtimeOptions.push("--interp-pgo-recording"):ze.runtimeOptions=["--interp-pgo-recording"],this}catch(e){throw Xe(1,e),e}}withConfig(e){try{return ve(ze,e),this}catch(e){throw Xe(1,e),e}}withConfigSrc(e){try{return e&&"string"==typeof e||Be(!1,"must be file path or URL"),Ee(We,{configSrc:e}),this}catch(e){throw Xe(1,e),e}}withVirtualWorkingDirectory(e){try{return e&&"string"==typeof e||Be(!1,"must be directory path"),ve(ze,{virtualWorkingDirectory:e}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariable(e,t){try{const o={};return o[e]=t,ve(ze,{environmentVariables:o}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariables(e){try{return e&&"object"==typeof e||Be(!1,"must be dictionary object"),ve(ze,{environmentVariables:e}),this}catch(e){throw Xe(1,e),e}}withDiagnosticTracing(e){try{return"boolean"!=typeof e&&Be(!1,"must be boolean"),ve(ze,{diagnosticTracing:e}),this}catch(e){throw Xe(1,e),e}}withDebugging(e){try{return null!=e&&"number"==typeof e||Be(!1,"must be number"),ve(ze,{debugLevel:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArguments(...e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ve(ze,{applicationArguments:e}),this}catch(e){throw Xe(1,e),e}}withRuntimeOptions(e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ze.runtimeOptions?ze.runtimeOptions.push(...e):ze.runtimeOptions=e,this}catch(e){throw Xe(1,e),e}}withMainAssembly(e){try{return ve(ze,{mainAssemblyName:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArgumentsFromQuery(){try{if(!globalThis.window)throw new Error("Missing window to the query parameters from");if(void 0===globalThis.URLSearchParams)throw new Error("URLSearchParams is supported");const e=new URLSearchParams(globalThis.window.location.search).getAll("arg");return this.withApplicationArguments(...e)}catch(e){throw Xe(1,e),e}}withApplicationEnvironment(e){try{return ve(ze,{applicationEnvironment:e}),this}catch(e){throw Xe(1,e),e}}withApplicationCulture(e){try{return ve(ze,{applicationCulture:e}),this}catch(e){throw Xe(1,e),e}}withResourceLoader(e){try{return Pe.loadBootResource=e,this}catch(e){throw Xe(1,e),e}}async download(){try{await async function(){lt(We),await Re(We),re(),D(),oe(),await Pe.allDownloadsFinished.promise}()}catch(e){throw Xe(1,e),e}}async create(){try{return this.instance||(this.instance=await async function(){return await ct(We),Fe.api}()),this.instance}catch(e){throw Xe(1,e),e}}async run(){try{return We.config||Be(!1,"Null moduleConfig.config"),this.instance||await this.create(),this.instance.runMainAndExit()}catch(e){throw Xe(1,e),e}}},mt=Xe,gt=ct;Ie||"function"==typeof globalThis.URL||Be(!1,"This browser/engine doesn't support URL API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),"function"!=typeof globalThis.BigInt64Array&&Be(!1,"This browser/engine doesn't support BigInt64Array API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),ft.withConfig(/*json-start*/{
  "mainAssemblyName": "GalleryBrowser",
  "resources": {
    "hash": "sha256-1Ni93pkdUlu51oL0iGMoY9wOJLSzNcV4B7CfYYc0eRM=",
    "jsModuleNative": [
      {
        "name": "dotnet.native.9a08jzbty2.js"
      }
    ],
    "jsModuleRuntime": [
      {
        "name": "dotnet.runtime.web2r9gqbh.js"
      }
    ],
    "wasmNative": [
      {
        "name": "dotnet.native.a3f5hq3cwf.wasm",
        "hash": "sha256-Wps/OcJm9KLKo08hKRaRasZHX7GSsBqfPL3HvSXvCz0=",
        "cache": "force-cache"
      }
    ],
    "icu": [
      {
        "virtualPath": "icudt_CJK.dat",
        "name": "icudt_CJK.tjcz0u77k5.dat",
        "hash": "sha256-SZLtQnRc0JkwqHab0VUVP7T3uBPSeYzxzDnpxPpUnHk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "icudt_EFIGS.dat",
        "name": "icudt_EFIGS.tptq2av103.dat",
        "hash": "sha256-8fItetYY8kQ0ww6oxwTLiT3oXlBwHKumbeP2pRF4yTc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "icudt_no_CJK.dat",
        "name": "icudt_no_CJK.lfu7j35m59.dat",
        "hash": "sha256-L7sV7NEYP37/Qr2FPCePo5cJqRgTXRwGHuwF5Q+0Nfs=",
        "cache": "force-cache"
      }
    ],
    "coreAssembly": [
      {
        "virtualPath": "System.Private.CoreLib.wasm",
        "name": "System.Private.CoreLib.21sgs6w606.wasm",
        "hash": "sha256-5uVoVR+SzrM3AKUzDg9gaUmubjJQ6twyNesAw3izM+c=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.JavaScript.wasm",
        "name": "System.Runtime.InteropServices.JavaScript.zldjyej0op.wasm",
        "hash": "sha256-p1WU8HgIalta3+n0AXKxn/rLuk7o6n+wRPXrf00ag8o=",
        "cache": "force-cache"
      }
    ],
    "assembly": [
      {
        "virtualPath": "BepuPhysics.wasm",
        "name": "BepuPhysics.mjlb7q1kyc.wasm",
        "hash": "sha256-0mC+PhqtQW1avKYceeCd8tso/nsWb3AYOYqtt2Fs6sM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "BepuUtilities.wasm",
        "name": "BepuUtilities.5ny0wbkaci.wasm",
        "hash": "sha256-n2SWWQKDdY+ACay8rp4L3nJ2Mq3HEnPqu15gUitGnN4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Friflo.Engine.ECS.wasm",
        "name": "Friflo.Engine.ECS.wxyoul6lhw.wasm",
        "hash": "sha256-HEc3aUUxOWf1hQNvxVaJlucFepmg1IbLUICjiKNXCYw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Friflo.Json.Burst.wasm",
        "name": "Friflo.Json.Burst.l661b93e3b.wasm",
        "hash": "sha256-c8uRyAKrojT0R6TWrA1aRFyCa9oYkP0fV6vc18r6WgE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Friflo.Json.Fliox.Annotation.wasm",
        "name": "Friflo.Json.Fliox.Annotation.rnixaw97zh.wasm",
        "hash": "sha256-SQnxuOvIOlzHUjjDxQsmRE7XwqrE+kI/QhIMm0zWqwg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Friflo.Json.Fliox.wasm",
        "name": "Friflo.Json.Fliox.kekxasouhe.wasm",
        "hash": "sha256-dOAfKo5XCB2LAq8l7HBJ/X0H0NE8n9GuK1hqHQOFN6w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "GalleryBrowser.wasm",
        "name": "GalleryBrowser.qvgso5dg8a.wasm",
        "hash": "sha256-ip/HcH07ChH4JkCMNPUhw7rsqlgh/bi/q454Cneu1dA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "HarfBuzzSharp.wasm",
        "name": "HarfBuzzSharp.ez5l8urr9q.wasm",
        "hash": "sha256-dj7cVX2w0eXm88Nf53goNDmdtpiWS0JBQlalgs3V2Fo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Animation.wasm",
        "name": "Luxel.Animation.6c2do330yq.wasm",
        "hash": "sha256-FoE3BTad4EBDdC6KI3dz/YmK7fBAtcOajbWhuJJ+/bw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Animation.ThreeD.wasm",
        "name": "Luxel.Animation.ThreeD.hhu0muwby3.wasm",
        "hash": "sha256-VWjQ0bg9dwJWqQ96mMBQY04aXa77ORqsUlXTEnW1sgE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Animation.TwoD.wasm",
        "name": "Luxel.Animation.TwoD.iqnip855eb.wasm",
        "hash": "sha256-Vk4gFfvHix/KFEbs3B7U9EU/NusLzVeUTI9HQny9PAo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Animation.UI.wasm",
        "name": "Luxel.Animation.UI.rwsci15bq1.wasm",
        "hash": "sha256-WyMXSRrIP0owtk33GZnCnjEh6AVUo1Ehf/6fRByVLMc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.AssetRuntime.wasm",
        "name": "Luxel.AssetRuntime.ugmzw0vffn.wasm",
        "hash": "sha256-gtOuBXdPBM/LFRSQW8+uv6l7pzykq7HJn0U1FuSOkmU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Assets.Gltf.wasm",
        "name": "Luxel.Assets.Gltf.p4o2i8imxp.wasm",
        "hash": "sha256-VwceGfnx6j+3qFDNiwpQc1hZ5iMhut74eeXJc1olO44=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Assets.wasm",
        "name": "Luxel.Assets.nrrye71osq.wasm",
        "hash": "sha256-7eTsJoZBgbTwRKKW94LWfUMlJFJ5+3JjbLrXWt+dX3U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.AssetsGpu.wasm",
        "name": "Luxel.AssetsGpu.4w8gwgvsp7.wasm",
        "hash": "sha256-/B6vPvXk5i+ogleTn1x5SZGgWLFS9wgESGUsy5ElOIs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Audio.wasm",
        "name": "Luxel.Audio.1xjn025mro.wasm",
        "hash": "sha256-ldyls36/LkNM6aL0cxEgKmF8x4E/wnO70oX/a+kLvH4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Audio.Browser.wasm",
        "name": "Luxel.Audio.Browser.phngy8lkp6.wasm",
        "hash": "sha256-E1OtNGJmBPH1Uggxb4vxSbkXqnPH/AncCWZj1h+GkFY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Controls.wasm",
        "name": "Luxel.Controls.zshuu07yzz.wasm",
        "hash": "sha256-o4OzLegXKZpCJ+GqnmBUCileOdWISSCP32Azd4n6My0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Diagnostics.wasm",
        "name": "Luxel.Diagnostics.rjogqid282.wasm",
        "hash": "sha256-ue2ZgV2X4tfxLsgrGEelE7ZTbpBeAjfT0L5JYL4U+HQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Diagram.wasm",
        "name": "Luxel.Diagram.5regdkbssc.wasm",
        "hash": "sha256-sVuOeH+ieyRBbUN/lzoXghEY4+rtNjSQMM1knaSR6L0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Document.wasm",
        "name": "Luxel.Document.5mi0o6l8v1.wasm",
        "hash": "sha256-w2G3eI3GhQn5N6zvX7Ai8CqFhOBtZRRoyWKuFDBjZzg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Ecs.wasm",
        "name": "Luxel.Ecs.zucagslc10.wasm",
        "hash": "sha256-ZVCcnCH+Y2KZeGUyAv2VUAyKEz7jcyFCtQuHAy9Gc7Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.Browser.wasm",
        "name": "Luxel.Gallery.Browser.wypqujfrva.wasm",
        "hash": "sha256-+L9XRlvobU9uotjoHk9f6n8wpLs/1Ne8cBvgx3ykIc0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.DocKit.wasm",
        "name": "Luxel.Gallery.DocKit.9ky8hbkn4k.wasm",
        "hash": "sha256-+R3YLfOCcJtaZLcqn3aho0VHGuZPhTL2hGhivAmy4uU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.Resources.Stories.wasm",
        "name": "Luxel.Gallery.Resources.Stories.vn6c67m53b.wasm",
        "hash": "sha256-u47+NQy3ss4oz3Pe72yPerg5Dw9UqNf5GiBjKFaCt3k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.Stories.CoreUi.wasm",
        "name": "Luxel.Gallery.Stories.CoreUi.b54fds517h.wasm",
        "hash": "sha256-QZA57uugOJ3ht38y9tHQ3Ppu4FBRUXQrvvixHHMUcLs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.UI.wasm",
        "name": "Luxel.Gallery.UI.7n5pmhrusy.wasm",
        "hash": "sha256-LS1f99aXiUygoui6iHhIT0+5HqADk600sZWBDPVXxR8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.wasm",
        "name": "Luxel.Gallery.jsv2yyb96d.wasm",
        "hash": "sha256-cyfTDLlYNl0LLwdeBmYlQcRnYY2YElUPjY5+C+feOfk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.RenderGraph.wasm",
        "name": "Luxel.Graphics.RenderGraph.yerfymjb6u.wasm",
        "hash": "sha256-iqJ1t+VZLV9xem1csWbxl29Fdgjq4HmXZi9Y+ZgtCp0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.TwoD.wasm",
        "name": "Luxel.Graphics.TwoD.1xun4mg0tr.wasm",
        "hash": "sha256-u2e0aDsmc3mJ/0JPTUuWGvQzBnp8b8thqxCTRaGMekU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.WebGPU.Browser.wasm",
        "name": "Luxel.Graphics.WebGPU.Browser.vhnwwtuccl.wasm",
        "hash": "sha256-DS2hatDuGv7dhgyL+2+1UpeIE89x76vKOYZ30kfp20Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.wasm",
        "name": "Luxel.Graphics.aybv8puoxc.wasm",
        "hash": "sha256-vsUn9BjteYvw80hwTj3OLVvbT/1pnq0w4ajDnx7Qel4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Highlight.TextMate.wasm",
        "name": "Luxel.Highlight.TextMate.t38y4kxtwt.wasm",
        "hash": "sha256-d/55cERyl8TWH4niCewwBnpHxxm6u+kIrV409HHPivM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Input.wasm",
        "name": "Luxel.Input.wmcvrepra9.wasm",
        "hash": "sha256-SGsJKx+XF1SL8H2g6b37fRctPNTLxM0bZy1GC7foChA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.MathText.wasm",
        "name": "Luxel.MathText.1epnc1l8h7.wasm",
        "hash": "sha256-SWBayPCXSrQu23AxkSzCOhAVoOSk/SNWVCk+/K2dQg0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Mathematics.wasm",
        "name": "Luxel.Mathematics.ckftu3qnr0.wasm",
        "hash": "sha256-9OF/cxPjJhvUdU4Eur54iOABPanAdVBLUCL1V7atsbg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.NodeGraph.wasm",
        "name": "Luxel.NodeGraph.rhg2ykb9ha.wasm",
        "hash": "sha256-AR7jB1C0Cqu6jMOjSHq/sD8iEJv+/uJzvSfiIH3q6Gg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Particles.wasm",
        "name": "Luxel.Particles.9s11t3kd07.wasm",
        "hash": "sha256-SWj6bEDdDb8U17oKSuQyeffyBQdsQILkiBDYj2/9+0c=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Particles.TwoD.wasm",
        "name": "Luxel.Particles.TwoD.skhc194sxq.wasm",
        "hash": "sha256-/Ij25FtofBcz5M7bkfy9K0FMJEJOp1TXHlNwgQttXRg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Particles.UI.wasm",
        "name": "Luxel.Particles.UI.s3tla3qptf.wasm",
        "hash": "sha256-mJzUNoRnHkE2IUQO0uwrQ+QHxiUdn6xwhk81ZsKbVGA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Physics.Gizmos.wasm",
        "name": "Luxel.Physics.Gizmos.vlhavsdwbi.wasm",
        "hash": "sha256-XUn17G0cKRTxjpGAfRjWPEJ7K79VwgJ/Ygy6m9AGkg4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Physics.wasm",
        "name": "Luxel.Physics.qa48mcq39r.wasm",
        "hash": "sha256-wq2wOTbOhFQSly5DCTvrKmspufUKk7cOlLjr/lKvhzI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Platform.Web.wasm",
        "name": "Luxel.Platform.Web.88ci1kv2m8.wasm",
        "hash": "sha256-mBB/daxo1V2vKrnFYFKIyp1e9vWe9kTSc1Ayowdoo7U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Platform.wasm",
        "name": "Luxel.Platform.stznvi2yzx.wasm",
        "hash": "sha256-4EPrkUHbrMjpbotK0H/af+MBICw4mIlTbpE/mkEPwxQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Resources.wasm",
        "name": "Luxel.Resources.hw3ddz6gmg.wasm",
        "hash": "sha256-AU4fACSzXpZF/mLwKcU5rel0ldVSCxxtjGQrVZ2W32s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.SceneEdit.wasm",
        "name": "Luxel.SceneEdit.ggtnhrtaoa.wasm",
        "hash": "sha256-4EH3gqXCJ38iDiTJiOnn4T10j+wWP0v7JuDWWWR5kXg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Shaders.Slang.Browser.wasm",
        "name": "Luxel.Shaders.Slang.Browser.kucdgue5qc.wasm",
        "hash": "sha256-tBXpY2Fd6YDu6MGI2zTa1cxWCGQzooHcKFAUfjevgJA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Shaders.wasm",
        "name": "Luxel.Shaders.fav351ujpm.wasm",
        "hash": "sha256-JyoBrpXfEoINLDCU/fTNmBS7Xta2ykhoY4IkS5p3//s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Typography.wasm",
        "name": "Luxel.Typography.640xzt5x47.wasm",
        "hash": "sha256-KlrRdGPQ+RTGVnVIp64vrydQFX93HaGxCdHyLSG8M7Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Typography.TwoD.wasm",
        "name": "Luxel.Typography.TwoD.wj9qw9com7.wasm",
        "hash": "sha256-JKkcvaOY1OeReh7a67WLU4wNRTuNR+b4wgvO12LfnxI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.UI.wasm",
        "name": "Luxel.UI.4xze2k7qtg.wasm",
        "hash": "sha256-WK9bcoyyf+9Yg/fJuZSEwkYwuj18p/3Y4jmhQl2g6Us=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.UI.Tailwind.wasm",
        "name": "Luxel.UI.Tailwind.qiemptyqtu.wasm",
        "hash": "sha256-/h/jvzOxd43XlDDgBXZWRSDrOUgS3GVWQv5GXpGM0WI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Workbench.wasm",
        "name": "Luxel.Workbench.8tj5n68i1z.wasm",
        "hash": "sha256-jEKHaHP4Rq9WQ5fG/EqB/vgFWrcK6Ky8uuJZf7HM97E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Markdig.wasm",
        "name": "Markdig.ato3fn3juu.wasm",
        "hash": "sha256-XrsZGktyAakARIo+IPOpkBODiKrhtyzTXNj2m8vj390=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.wasm",
        "name": "Microsoft.AspNetCore.Components.qrucgo1utl.wasm",
        "hash": "sha256-xiaeQ+fFcIROAm3E8pNAWPRAmDknS7aUbT9zWfDGqj8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.Web.wasm",
        "name": "Microsoft.AspNetCore.Components.Web.5jjx151qiq.wasm",
        "hash": "sha256-37ldMKgcgefoNcl4V36hyXVlrrwdtCeY9lXYz1zTJwg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.WebAssembly.wasm",
        "name": "Microsoft.AspNetCore.Components.WebAssembly.rgdgfixfdq.wasm",
        "hash": "sha256-1ZA4DHOAbJD4mtdepxu8Stt6x1lVIR2Y1a76DDNVLzI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.Abstractions.wasm",
        "name": "Microsoft.Extensions.Configuration.Abstractions.g80u52ij8n.wasm",
        "hash": "sha256-sKlc0ljsYj0miYkKYc4QscrvF85zLpABlEUAnWgTstw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.Json.wasm",
        "name": "Microsoft.Extensions.Configuration.Json.ixktlrimdt.wasm",
        "hash": "sha256-NjAIjIQlITJK5e/1cNjeMLBSuW2yXbc3mfYPnlQU0WU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.wasm",
        "name": "Microsoft.Extensions.Configuration.w4ckz9zukx.wasm",
        "hash": "sha256-cPFBZhC9IYk0VBEu3Lu7BDc7/DepxvRfPEGW46+Dh+U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.DependencyInjection.wasm",
        "name": "Microsoft.Extensions.DependencyInjection.b9mcrj62ry.wasm",
        "hash": "sha256-Uf2OoF4AD/nB8bEP9+Vtasx3otZscbwOarFh3Uu7bQI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.DependencyInjection.Abstractions.wasm",
        "name": "Microsoft.Extensions.DependencyInjection.Abstractions.e18ou0n3l7.wasm",
        "hash": "sha256-skzYsNrGldAdHKOJNwc9s73thCX14hNNomANgaVJJpk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Logging.Abstractions.wasm",
        "name": "Microsoft.Extensions.Logging.Abstractions.q5lf7goo7p.wasm",
        "hash": "sha256-IsB818a0ML3W928+6hvD6zfYniRbLOJ3jm0tUSoXRuc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Logging.wasm",
        "name": "Microsoft.Extensions.Logging.vqiumbbk2y.wasm",
        "hash": "sha256-mbNEmfqRHcXa1eXIsnSfoKUSC1/ZdphrzRhl3iZFUjU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Options.wasm",
        "name": "Microsoft.Extensions.Options.vv2otl9x5w.wasm",
        "hash": "sha256-m6U6IejoGN9kj03uejeZ4lWXA2JFVnVJzp4JAPF+Pzw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Primitives.wasm",
        "name": "Microsoft.Extensions.Primitives.dte9muiaw7.wasm",
        "hash": "sha256-v55sXcWCo8zowOy6pyFF7ZM89gAGofylJwlSicoolHg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.JSInterop.WebAssembly.wasm",
        "name": "Microsoft.JSInterop.WebAssembly.5ydn64ly88.wasm",
        "hash": "sha256-Xcxd+z/8v1MjYI/6IsNgJ46ifV38aKmiGyz4RR+L0ms=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.JSInterop.wasm",
        "name": "Microsoft.JSInterop.aam46o80z1.wasm",
        "hash": "sha256-LhAUF8Tpz+bcltrI+injcHeUlSV6t3E4erPOkiy8lVQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "NAudio.Core.wasm",
        "name": "NAudio.Core.9sc8axkd2f.wasm",
        "hash": "sha256-ueDJ61TNYywE5dT/HnzqbqrKfZO7hJfhgy0b7OwH1Y0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "NAudio.Vorbis.wasm",
        "name": "NAudio.Vorbis.mq08s2twvc.wasm",
        "hash": "sha256-QpYbPoUbm/vUZoqvx5ktw2gV2bjtUvEgnLhvpHtd7oA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "NVorbis.wasm",
        "name": "NVorbis.182zkb2e0i.wasm",
        "hash": "sha256-/8ljo6M1M6r2Ejnne+c8SN7e45mr40w3W2ENZUwscsA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Onigwrap.wasm",
        "name": "Onigwrap.1mwdv0hsm9.wasm",
        "hash": "sha256-WXVdk0Guh2lyj4XWYgSO9b4Bk6k7XoeAoziZRMJxdng=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SixLabors.ImageSharp.wasm",
        "name": "SixLabors.ImageSharp.aybut29yyv.wasm",
        "hash": "sha256-Ai0sW3TUk5Bt2Tu5hcgXyTFkO6xaXBWjmVmHTXDML9o=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.wasm",
        "name": "System.cvgusyd4es.wasm",
        "hash": "sha256-GBsoWgsm7nTRKlb5Lr430r1XsgW1gVH+jeoGAIS2K00=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Concurrent.wasm",
        "name": "System.Collections.Concurrent.2jj4wgmxk1.wasm",
        "hash": "sha256-3jvbSjegTE8DybZ0X7tZwt2fHwiy69rKYTT8xYmk8uM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Immutable.wasm",
        "name": "System.Collections.Immutable.s3xee1yuz1.wasm",
        "hash": "sha256-GLHelwChBxNNbyIMhHVPGM1M4VcpesgDCwSB2XZmQ0M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.NonGeneric.wasm",
        "name": "System.Collections.NonGeneric.o1lkxoco6o.wasm",
        "hash": "sha256-jIhccShOvbc3U0SDCsE2uHI+9fdNCS8RFSZuL9vi8TQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Specialized.wasm",
        "name": "System.Collections.Specialized.zcgemdrpem.wasm",
        "hash": "sha256-rZvWQpgxovXQSsgICrheP3oKTa7YVqZ41kyF8ofY/pQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.wasm",
        "name": "System.Collections.1zw7ishgl8.wasm",
        "hash": "sha256-UwsqUn9QEA+rT6l54lNmemqYRhHidMlFrwRWdS/uWAI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.Annotations.wasm",
        "name": "System.ComponentModel.Annotations.ty4grszw9k.wasm",
        "hash": "sha256-JZgZXW/9jftPOCO5CTFFWFB+8+Md7l3SFm6rlQ9+3wM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.Primitives.wasm",
        "name": "System.ComponentModel.Primitives.5qn5tx2i90.wasm",
        "hash": "sha256-XQjCud5KBzdt82jPhZNoc5ci5zY7HBVryYGf61FC8x8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.TypeConverter.wasm",
        "name": "System.ComponentModel.TypeConverter.2yyw20fwn8.wasm",
        "hash": "sha256-FlXEC2JXQ5LNxbWzEthu+k8ZjpK31XM68WkZUN5+1jQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.wasm",
        "name": "System.ComponentModel.j8t4mlscoa.wasm",
        "hash": "sha256-A3cXygK2O5szILGJVXRvAq3Ru/guTxRmqMOiy1cnmVc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Console.wasm",
        "name": "System.Console.vtf7ex1wfi.wasm",
        "hash": "sha256-403pORK0NGx7lUuUq4mcNDlmF5HWaLMN9JvK1nd0B2o=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.DiagnosticSource.wasm",
        "name": "System.Diagnostics.DiagnosticSource.v7dp4hmn37.wasm",
        "hash": "sha256-1WjwEEKK69/6g081+ipKIdG+xp+lf2rn7sz+2RvHRc8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.StackTrace.wasm",
        "name": "System.Diagnostics.StackTrace.v1zmv3i126.wasm",
        "hash": "sha256-8yXSm4I3ihlzMjAY2eWC7gZHoNdFqRqziOo3jRYDf5M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.TraceSource.wasm",
        "name": "System.Diagnostics.TraceSource.rbf9ys33d7.wasm",
        "hash": "sha256-KQh32FdcvsuVwW891XNaARS+KQ9qB18zzKLldExWAQ4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.wasm",
        "name": "System.IO.Compression.yc6lmpt25d.wasm",
        "hash": "sha256-jsJoiqK63Fu204az/coYUxep8pJE/27XyTnL6NLXARg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.Watcher.wasm",
        "name": "System.IO.FileSystem.Watcher.ulggpi0m5n.wasm",
        "hash": "sha256-7obZx3eSkKyQfAxdNSiK6fEDriZh/Bnoro1RP5LrYjg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipelines.wasm",
        "name": "System.IO.Pipelines.0luqpzcwkx.wasm",
        "hash": "sha256-VAItgUEBvLcUdhWi2dVGBp/lsYOLjES6nhQF93Gd1m4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.wasm",
        "name": "System.Linq.f5ipy712it.wasm",
        "hash": "sha256-I5ZVYxini0lTDbmmgcBqklmZywNFFJ0oH3ZGEFaXKdk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Expressions.wasm",
        "name": "System.Linq.Expressions.z9t6x2un7x.wasm",
        "hash": "sha256-t4LOacotdsc4oxcrEhQYE6PdU98ffXoa8+i1/9Ql2cE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Memory.wasm",
        "name": "System.Memory.7pxcvb8e8b.wasm",
        "hash": "sha256-m8A8LWWgFdJcsvNKRzslpsyZVv87JRwAoA6uIdWzL/s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Http.wasm",
        "name": "System.Net.Http.z61v3a0kf0.wasm",
        "hash": "sha256-y2IJFYNWv9BXDTZR5EonyioXZNCt/83EIXX7XG+lPiA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Primitives.wasm",
        "name": "System.Net.Primitives.5dpvrmwpzs.wasm",
        "hash": "sha256-1xoyMs5BiZDtqJOlM8OkkLhYkjNvc0wzFE/yO76wdW8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Numerics.Vectors.wasm",
        "name": "System.Numerics.Vectors.710tk4swfl.wasm",
        "hash": "sha256-lXYvaXpKg0i+6723kSYEEE4tRnRspe5vSAQTfspPjHQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ObjectModel.wasm",
        "name": "System.ObjectModel.ec8ukh2ldt.wasm",
        "hash": "sha256-vl1FaROuCyMI2LVRgJNHkJVOX2Non74Rnz7xVc3dFtQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Uri.wasm",
        "name": "System.Private.Uri.vwh475umef.wasm",
        "hash": "sha256-UdEcacud+DCdLuWCIeAnk8g2ZfiG51qvTS6LLrnbVJ4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.Linq.wasm",
        "name": "System.Private.Xml.Linq.x8vctarzgw.wasm",
        "hash": "sha256-z8FLHZmL9r2fo0uSbmZ/bTWHjevUs6xpgnXOzeQLnzI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.wasm",
        "name": "System.Private.Xml.xet87cs1ac.wasm",
        "hash": "sha256-6V1337c58Lwj1JcXKkp6Cv3QCRe78jZ9KFEMnO/aG7w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.ILGeneration.wasm",
        "name": "System.Reflection.Emit.ILGeneration.mrej57e4s7.wasm",
        "hash": "sha256-glIcp8LXvz8miileK23pTcIFLgC5nI4z4F9H794FHtw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.Lightweight.wasm",
        "name": "System.Reflection.Emit.Lightweight.zcp2n2i7ds.wasm",
        "hash": "sha256-44sUVnJiFHxlB2U+xDlEam5rjiJveh7Qen+bX2A+IuQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Primitives.wasm",
        "name": "System.Reflection.Primitives.6n0zme69c4.wasm",
        "hash": "sha256-AlwoS7lmuxVzz+JVwEHJGXgFHhp22BPataSHTYdY2a4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.CompilerServices.Unsafe.wasm",
        "name": "System.Runtime.CompilerServices.Unsafe.9biukg2yc5.wasm",
        "hash": "sha256-NSxDWxNJia0ACKbl+EUJx2VpiZ7iRIvXiZTHmX+KNuM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.wasm",
        "name": "System.Runtime.InteropServices.9b2kyg6lrz.wasm",
        "hash": "sha256-S3XzV8EFNMaoY/7tvwgZtS+oKLY1zFtOsaWVP6QSQdU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Intrinsics.wasm",
        "name": "System.Runtime.Intrinsics.qvrvknxruy.wasm",
        "hash": "sha256-5r5zArtBNd1GRKlXjBWA8NjMPh6qQA00QfMvWeF925s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Numerics.wasm",
        "name": "System.Runtime.Numerics.urtz60mrgj.wasm",
        "hash": "sha256-emd7kGVDgrIBnCYl6CRCPWnM2tzXhuUtsUZp1aGlKr4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.wasm",
        "name": "System.Runtime.g8v0vf6j0w.wasm",
        "hash": "sha256-INljJ/+y6SQBNsTPF7cvQpgR2eWZvtQRt2ImH2VjG4k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.wasm",
        "name": "System.Security.Cryptography.3wztuh9vm7.wasm",
        "hash": "sha256-DZNxrOi+Uv/MhYt3FfAZF6Rq+lZMwtSySKWhLpYi8PM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.CodePages.wasm",
        "name": "System.Text.Encoding.CodePages.oi53t29tv2.wasm",
        "hash": "sha256-UGxFuBL+8DHo5iARmx9juGmAt6SiTrgVFRN0TTeMSWg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.Extensions.wasm",
        "name": "System.Text.Encoding.Extensions.zswydz4t9a.wasm",
        "hash": "sha256-bQwr0yZ2Vy+E57aGNk84lo2p2tH+APqeqqQ0ZmhIEi4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encodings.Web.wasm",
        "name": "System.Text.Encodings.Web.nfp6721ho9.wasm",
        "hash": "sha256-Q8XKJCMoWAhzRrKQ9hNE+5kdvh5QLkS6OzKr7xjRSkQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Json.wasm",
        "name": "System.Text.Json.l64eojfp6s.wasm",
        "hash": "sha256-U38YHvFDWr/zFfYGpn0ZE8euiuTS0IEr9oNFhXpP+Lw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.RegularExpressions.wasm",
        "name": "System.Text.RegularExpressions.0d4vcsu9d1.wasm",
        "hash": "sha256-LDyXe16BBV7zt97XjNeBlAM1ylrvvmB+KybbFl5pK48=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.wasm",
        "name": "System.Threading.5tsm7adhft.wasm",
        "hash": "sha256-p+XIF9zNejyxIJdBnwZlOzYvEiMnod10ZIPjlfpvkdk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Parallel.wasm",
        "name": "System.Threading.Tasks.Parallel.dwbl4mtns4.wasm",
        "hash": "sha256-fVsoaKbVtmZP1PM1sR/JPNZ9U4cHjET9vq9HKX58qhY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Thread.wasm",
        "name": "System.Threading.Thread.igw68ojsxg.wasm",
        "hash": "sha256-Ln6gVsFKJ92UW8TA8MYBhIZW6JzuPi0ZLZtCzMl41kU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.ThreadPool.wasm",
        "name": "System.Threading.ThreadPool.443mw0r7kl.wasm",
        "hash": "sha256-RElWNE9GuZdTYpMq8v2EPk/Mj3jHtZmKfAYg6EoyviY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.Linq.wasm",
        "name": "System.Xml.Linq.xorxxf2zgs.wasm",
        "hash": "sha256-cUe2Rpuwodxb9Zl4Aen3bgv8hlEah7r36PWyeIRYVWQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XDocument.wasm",
        "name": "System.Xml.XDocument.uamzfc7b3x.wasm",
        "hash": "sha256-WlUp7g1bH2+Kk9fB7BDPRcn+GLkU8AfLlRbV+9tYv9k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "TextMateSharp.Grammars.wasm",
        "name": "TextMateSharp.Grammars.7gyzt2zjpo.wasm",
        "hash": "sha256-2fHyEYK5ICCAS67BcVm78FJBUTATyvU25/3i3GOKMyQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "TextMateSharp.wasm",
        "name": "TextMateSharp.jyllc5kuxh.wasm",
        "hash": "sha256-a+JlRbB6rLlneze18mR25Z9ICfYRV5Z0sA7FKNjnefw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "netstandard.wasm",
        "name": "netstandard.lpl1l6zq3c.wasm",
        "hash": "sha256-1x6ABberlJCNAt9xkxqxze/itp/Tx2uGUyGfKeeeWt8=",
        "cache": "force-cache"
      }
    ],
    "satelliteResources": {
      "ps": [
        {
          "virtualPath": "Luxel.Gallery.Resources.Stories.resources.wasm",
          "name": "Luxel.Gallery.Resources.Stories.resources.1mdu7umf32.wasm",
          "hash": "sha256-l6ArXBJplSvc8YzjgV7cHNsZgECDIxDjlgf2jP53RAE=",
          "cache": "force-cache"
        }
      ]
    }
  },
  "debugLevel": 0,
  "linkerEnabled": true,
  "globalizationMode": "sharded",
  "extensions": {
    "blazor": {}
  },
  "runtimeConfig": {
    "runtimeOptions": {
      "configProperties": {
        "Microsoft.AspNetCore.Components.Routing.RegexConstraintSupport": false,
        "Microsoft.Extensions.DependencyInjection.VerifyOpenGenericServiceTrimmability": true,
        "System.ComponentModel.DefaultValueAttribute.IsSupported": false,
        "System.ComponentModel.Design.IDesignerHost.IsSupported": false,
        "System.ComponentModel.TypeConverter.EnableUnsafeBinaryFormatterInDesigntimeLicenseContextSerialization": false,
        "System.ComponentModel.TypeDescriptor.IsComObjectDescriptorSupported": false,
        "System.Data.DataSet.XmlSerializationIsSupported": false,
        "System.Diagnostics.Debugger.IsSupported": false,
        "System.Diagnostics.Metrics.Meter.IsSupported": false,
        "System.Diagnostics.Tracing.EventSource.IsSupported": false,
        "System.GC.Server": true,
        "System.Globalization.Invariant": false,
        "System.TimeZoneInfo.Invariant": false,
        "System.Linq.Enumerable.IsSizeOptimized": true,
        "System.Net.Http.EnableActivityPropagation": false,
        "System.Net.Http.WasmEnableStreamingResponse": true,
        "System.Net.SocketsHttpHandler.Http3Support": false,
        "System.Reflection.Metadata.MetadataUpdater.IsSupported": false,
        "System.Resources.ResourceManager.AllowCustomResourceTypes": false,
        "System.Resources.UseSystemResourceKeys": true,
        "System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported": true,
        "System.Runtime.InteropServices.BuiltInComInterop.IsSupported": false,
        "System.Runtime.InteropServices.EnableConsumingManagedCodeFromNativeHosting": false,
        "System.Runtime.InteropServices.EnableCppCLIHostActivation": false,
        "System.Runtime.InteropServices.Marshalling.EnableGeneratedComInterfaceComImportInterop": false,
        "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false,
        "System.StartupHookProvider.IsSupported": false,
        "System.Text.Encoding.EnableUnsafeUTF7Encoding": false,
        "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault": true,
        "System.Threading.Thread.EnableAutoreleasePool": false,
        "Microsoft.AspNetCore.Components.Endpoints.NavigationManager.DisableThrowNavigationException": false
      }
    }
  }
}/*json-end*/);export{gt as default,ft as dotnet,mt as exit};
