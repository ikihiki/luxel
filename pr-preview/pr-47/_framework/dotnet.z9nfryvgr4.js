//! Licensed to the .NET Foundation under one or more agreements.
//! The .NET Foundation licenses this file to you under the MIT license.

var e=!1;const t=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,4,1,96,0,0,3,2,1,0,10,8,1,6,0,6,64,25,11,11])),o=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,15,1,13,0,65,1,253,15,65,2,253,15,253,128,2,11])),n=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,10,1,8,0,65,0,253,15,253,98,11])),r=Symbol.for("wasm promise_control");function i(e,t){let o=null;const n=new Promise((function(n,r){o={isDone:!1,promise:null,resolve:t=>{o.isDone||(o.isDone=!0,n(t),e&&e())},reject:e=>{o.isDone||(o.isDone=!0,r(e),t&&t())}}}));o.promise=n;const i=n;return i[r]=o,{promise:i,promise_control:o}}function s(e){return e[r]}function a(e){e&&function(e){return void 0!==e[r]}(e)||Be(!1,"Promise is not controllable")}const l="__mono_message__",c=["debug","log","trace","warn","info","error"],d="MONO_WASM: ";let u,f,m,g,p,h;function w(e){g=e}function b(e){if(Pe.diagnosticTracing){const t="function"==typeof e?e():e;console.debug(d+t)}}function y(e,...t){console.info(d+e,...t)}function v(e,...t){console.info(e,...t)}function E(e,...t){console.warn(d+e,...t)}function _(e,...t){if(t&&t.length>0&&t[0]&&"object"==typeof t[0]){if(t[0].silent)return;if(t[0].toString)return void console.error(d+e,t[0].toString())}console.error(d+e,...t)}function x(e,t,o){return function(...n){try{let r=n[0];if(void 0===r)r="undefined";else if(null===r)r="null";else if("function"==typeof r)r=r.toString();else if("string"!=typeof r)try{r=JSON.stringify(r)}catch(e){r=r.toString()}t(o?JSON.stringify({method:e,payload:r,arguments:n.slice(1)}):[e+r,...n.slice(1)])}catch(e){m.error(`proxyConsole failed: ${e}`)}}}function j(e,t,o){f=t,g=e,m={...t};const n=`${o}/console`.replace("https://","wss://").replace("http://","ws://");u=new WebSocket(n),u.addEventListener("error",A),u.addEventListener("close",S),function(){for(const e of c)f[e]=x(`console.${e}`,T,!0)}()}function R(e){let t=30;const o=()=>{u?0==u.bufferedAmount||0==t?(e&&v(e),function(){for(const e of c)f[e]=x(`console.${e}`,m.log,!1)}(),u.removeEventListener("error",A),u.removeEventListener("close",S),u.close(1e3,e),u=void 0):(t--,globalThis.setTimeout(o,100)):e&&m&&m.log(e)};o()}function T(e){u&&u.readyState===WebSocket.OPEN?u.send(e):m.log(e)}function A(e){m.error(`[${g}] proxy console websocket error: ${e}`,e)}function S(e){m.debug(`[${g}] proxy console websocket closed: ${e}`,e)}function D(){Pe.preferredIcuAsset=O(Pe.config);let e="invariant"==Pe.config.globalizationMode;if(!e)if(Pe.preferredIcuAsset)Pe.diagnosticTracing&&b("ICU data archive(s) available, disabling invariant mode");else{if("custom"===Pe.config.globalizationMode||"all"===Pe.config.globalizationMode||"sharded"===Pe.config.globalizationMode){const e="invariant globalization mode is inactive and no ICU data archives are available";throw _(`ERROR: ${e}`),new Error(e)}Pe.diagnosticTracing&&b("ICU data archive(s) not available, using invariant globalization mode"),e=!0,Pe.preferredIcuAsset=null}const t="DOTNET_SYSTEM_GLOBALIZATION_INVARIANT",o=Pe.config.environmentVariables;if(void 0===o[t]&&e&&(o[t]="1"),void 0===o.TZ)try{const e=Intl.DateTimeFormat().resolvedOptions().timeZone||null;e&&(o.TZ=e)}catch(e){y("failed to detect timezone, will fallback to UTC")}}function O(e){var t;if((null===(t=e.resources)||void 0===t?void 0:t.icu)&&"invariant"!=e.globalizationMode){const t=e.applicationCulture||(ke?globalThis.navigator&&globalThis.navigator.languages&&globalThis.navigator.languages[0]:Intl.DateTimeFormat().resolvedOptions().locale),o=e.resources.icu;let n=null;if("custom"===e.globalizationMode){if(o.length>=1)return o[0].name}else t&&"all"!==e.globalizationMode?"sharded"===e.globalizationMode&&(n=function(e){const t=e.split("-")[0];return"en"===t||["fr","fr-FR","it","it-IT","de","de-DE","es","es-ES"].includes(e)?"icudt_EFIGS.dat":["zh","ko","ja"].includes(t)?"icudt_CJK.dat":"icudt_no_CJK.dat"}(t)):n="icudt.dat";if(n)for(let e=0;e<o.length;e++){const t=o[e];if(t.virtualPath===n)return t.name}}return e.globalizationMode="invariant",null}(new Date).valueOf();const C=class{constructor(e){this.url=e}toString(){return this.url}};async function k(e,t){try{const o="function"==typeof globalThis.fetch;if(Se){const n=e.startsWith("file://");if(!n&&o)return globalThis.fetch(e,t||{credentials:"same-origin"});p||(h=Ne.require("url"),p=Ne.require("fs")),n&&(e=h.fileURLToPath(e));const r=await p.promises.readFile(e);return{ok:!0,headers:{length:0,get:()=>null},url:e,arrayBuffer:()=>r,json:()=>JSON.parse(r),text:()=>{throw new Error("NotImplementedException")}}}if(o)return globalThis.fetch(e,t||{credentials:"same-origin"});if("function"==typeof read)return{ok:!0,url:e,headers:{length:0,get:()=>null},arrayBuffer:()=>new Uint8Array(read(e,"binary")),json:()=>JSON.parse(read(e,"utf8")),text:()=>read(e,"utf8")}}catch(t){return{ok:!1,url:e,status:500,headers:{length:0,get:()=>null},statusText:"ERR28: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t},text:()=>{throw t}}}throw new Error("No fetch implementation available")}function I(e){return"string"!=typeof e&&Be(!1,"url must be a string"),!M(e)&&0!==e.indexOf("./")&&0!==e.indexOf("../")&&globalThis.URL&&globalThis.document&&globalThis.document.baseURI&&(e=new URL(e,globalThis.document.baseURI).toString()),e}const U=/^[a-zA-Z][a-zA-Z\d+\-.]*?:\/\//,P=/[a-zA-Z]:[\\/]/;function M(e){return Se||Ie?e.startsWith("/")||e.startsWith("\\")||-1!==e.indexOf("///")||P.test(e):U.test(e)}let L,N=0;const $=[],z=[],W=new Map,F={"js-module-threads":!0,"js-module-runtime":!0,"js-module-dotnet":!0,"js-module-native":!0,"js-module-diagnostics":!0},B={...F,"js-module-library-initializer":!0},V={...F,dotnetwasm:!0,heap:!0,manifest:!0},q={...B,manifest:!0},H={...B,dotnetwasm:!0},J={dotnetwasm:!0,symbols:!0},Z={...B,dotnetwasm:!0,symbols:!0},Q={symbols:!0};function G(e){return!("icu"==e.behavior&&e.name!=Pe.preferredIcuAsset)}function K(e,t,o){null!=t||(t=[]),Be(1==t.length,`Expect to have one ${o} asset in resources`);const n=t[0];return n.behavior=o,X(n),e.push(n),n}function X(e){V[e.behavior]&&W.set(e.behavior,e)}function Y(e){Be(V[e],`Unknown single asset behavior ${e}`);const t=W.get(e);if(t&&!t.resolvedUrl)if(t.resolvedUrl=Pe.locateFile(t.name),F[t.behavior]){const e=ge(t);e?("string"!=typeof e&&Be(!1,"loadBootResource response for 'dotnetjs' type should be a URL string"),t.resolvedUrl=e):t.resolvedUrl=ce(t.resolvedUrl,t.behavior)}else if("dotnetwasm"!==t.behavior)throw new Error(`Unknown single asset behavior ${e}`);return t}function ee(e){const t=Y(e);return Be(t,`Single asset for ${e} not found`),t}let te=!1;async function oe(){if(!te){te=!0,Pe.diagnosticTracing&&b("mono_download_assets");try{const e=[],t=[],o=(e,t)=>{!Z[e.behavior]&&G(e)&&Pe.expected_instantiated_assets_count++,!H[e.behavior]&&G(e)&&(Pe.expected_downloaded_assets_count++,t.push(se(e)))};for(const t of $)o(t,e);for(const e of z)o(e,t);Pe.allDownloadsQueued.promise_control.resolve(),Promise.all([...e,...t]).then((()=>{Pe.allDownloadsFinished.promise_control.resolve()})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),await Pe.runtimeModuleLoaded.promise;const n=async e=>{const t=await e;if(t.buffer){if(!Z[t.behavior]){t.buffer&&"object"==typeof t.buffer||Be(!1,"asset buffer must be array-like or buffer-like or promise of these"),"string"!=typeof t.resolvedUrl&&Be(!1,"resolvedUrl must be string");const e=t.resolvedUrl,o=await t.buffer,n=new Uint8Array(o);pe(t),await Ue.beforeOnRuntimeInitialized.promise,Ue.instantiate_asset(t,e,n)}}else J[t.behavior]?("symbols"===t.behavior&&(await Ue.instantiate_symbols_asset(t),pe(t)),J[t.behavior]&&++Pe.actual_downloaded_assets_count):(t.isOptional||Be(!1,"Expected asset to have the downloaded buffer"),!H[t.behavior]&&G(t)&&Pe.expected_downloaded_assets_count--,!Z[t.behavior]&&G(t)&&Pe.expected_instantiated_assets_count--)},r=[],i=[];for(const t of e)r.push(n(t));for(const e of t)i.push(n(e));Promise.all(r).then((()=>{Ce||Ue.coreAssetsInMemory.promise_control.resolve()})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),Promise.all(i).then((async()=>{Ce||(await Ue.coreAssetsInMemory.promise,Ue.allAssetsInMemory.promise_control.resolve())})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e}))}catch(e){throw Pe.err("Error in mono_download_assets: "+e),e}}}let ne=!1;function re(){if(ne)return;ne=!0;const e=Pe.config,t=[];if(e.assets)for(const t of e.assets)"object"!=typeof t&&Be(!1,`asset must be object, it was ${typeof t} : ${t}`),"string"!=typeof t.behavior&&Be(!1,"asset behavior must be known string"),"string"!=typeof t.name&&Be(!1,"asset name must be string"),t.resolvedUrl&&"string"!=typeof t.resolvedUrl&&Be(!1,"asset resolvedUrl could be string"),t.hash&&"string"!=typeof t.hash&&Be(!1,"asset resolvedUrl could be string"),t.pendingDownload&&"object"!=typeof t.pendingDownload&&Be(!1,"asset pendingDownload could be object"),t.isCore?$.push(t):z.push(t),X(t);else if(e.resources){const o=e.resources;o.wasmNative||Be(!1,"resources.wasmNative must be defined"),o.jsModuleNative||Be(!1,"resources.jsModuleNative must be defined"),o.jsModuleRuntime||Be(!1,"resources.jsModuleRuntime must be defined"),K(z,o.wasmNative,"dotnetwasm"),K(t,o.jsModuleNative,"js-module-native"),K(t,o.jsModuleRuntime,"js-module-runtime"),o.jsModuleDiagnostics&&K(t,o.jsModuleDiagnostics,"js-module-diagnostics");const n=(e,t,o)=>{const n=e;n.behavior=t,o?(n.isCore=!0,$.push(n)):z.push(n)};if(o.coreAssembly)for(let e=0;e<o.coreAssembly.length;e++)n(o.coreAssembly[e],"assembly",!0);if(o.assembly)for(let e=0;e<o.assembly.length;e++)n(o.assembly[e],"assembly",!o.coreAssembly);if(0!=e.debugLevel&&Pe.isDebuggingSupported()){if(o.corePdb)for(let e=0;e<o.corePdb.length;e++)n(o.corePdb[e],"pdb",!0);if(o.pdb)for(let e=0;e<o.pdb.length;e++)n(o.pdb[e],"pdb",!o.corePdb)}if(e.loadAllSatelliteResources&&o.satelliteResources)for(const e in o.satelliteResources)for(let t=0;t<o.satelliteResources[e].length;t++){const r=o.satelliteResources[e][t];r.culture=e,n(r,"resource",!o.coreAssembly)}if(o.coreVfs)for(let e=0;e<o.coreVfs.length;e++)n(o.coreVfs[e],"vfs",!0);if(o.vfs)for(let e=0;e<o.vfs.length;e++)n(o.vfs[e],"vfs",!o.coreVfs);const r=O(e);if(r&&o.icu)for(let e=0;e<o.icu.length;e++){const t=o.icu[e];t.name===r&&n(t,"icu",!1)}if(o.wasmSymbols)for(let e=0;e<o.wasmSymbols.length;e++)n(o.wasmSymbols[e],"symbols",!1)}if(e.appsettings)for(let t=0;t<e.appsettings.length;t++){const o=e.appsettings[t],n=he(o);"appsettings.json"!==n&&n!==`appsettings.${e.applicationEnvironment}.json`||z.push({name:o,behavior:"vfs",cache:"no-cache",useCredentials:!0})}e.assets=[...$,...z,...t]}async function ie(e){const t=await se(e);return await t.pendingDownloadInternal.response,t.buffer}async function se(e){try{return await ae(e)}catch(t){if(!Pe.enableDownloadRetry)throw t;if(Ie||Se)throw t;if(e.pendingDownload&&e.pendingDownloadInternal==e.pendingDownload)throw t;if(e.resolvedUrl&&-1!=e.resolvedUrl.indexOf("file://"))throw t;if(t&&404==t.status)throw t;e.pendingDownloadInternal=void 0,await Pe.allDownloadsQueued.promise;try{return Pe.diagnosticTracing&&b(`Retrying download '${e.name}'`),await ae(e)}catch(t){return e.pendingDownloadInternal=void 0,await new Promise((e=>globalThis.setTimeout(e,100))),Pe.diagnosticTracing&&b(`Retrying download (2) '${e.name}' after delay`),await ae(e)}}}async function ae(e){for(;L;)await L.promise;try{++N,N==Pe.maxParallelDownloads&&(Pe.diagnosticTracing&&b("Throttling further parallel downloads"),L=i());const t=await async function(e){if(e.pendingDownload&&(e.pendingDownloadInternal=e.pendingDownload),e.pendingDownloadInternal&&e.pendingDownloadInternal.response)return e.pendingDownloadInternal.response;if(e.buffer){const t=await e.buffer;return e.resolvedUrl||(e.resolvedUrl="undefined://"+e.name),e.pendingDownloadInternal={url:e.resolvedUrl,name:e.name,response:Promise.resolve({ok:!0,arrayBuffer:()=>t,json:()=>JSON.parse(new TextDecoder("utf-8").decode(t)),text:()=>{throw new Error("NotImplementedException")},headers:{get:()=>{}}})},e.pendingDownloadInternal.response}const t=e.loadRemote&&Pe.config.remoteSources?Pe.config.remoteSources:[""];let o;for(let n of t){n=n.trim(),"./"===n&&(n="");const t=le(e,n);e.name===t?Pe.diagnosticTracing&&b(`Attempting to download '${t}'`):Pe.diagnosticTracing&&b(`Attempting to download '${t}' for ${e.name}`);try{e.resolvedUrl=t;const n=fe(e);if(e.pendingDownloadInternal=n,o=await n.response,!o||!o.ok)continue;return o}catch(e){o||(o={ok:!1,url:t,status:0,statusText:""+e});continue}}const n=e.isOptional||e.name.match(/\.pdb$/)&&Pe.config.ignorePdbLoadErrors;if(o||Be(!1,`Response undefined ${e.name}`),!n){const t=new Error(`download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`);throw t.status=o.status,t}y(`optional download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`)}(e);return t?(J[e.behavior]||(e.buffer=await t.arrayBuffer(),++Pe.actual_downloaded_assets_count),e):e}finally{if(--N,L&&N==Pe.maxParallelDownloads-1){Pe.diagnosticTracing&&b("Resuming more parallel downloads");const e=L;L=void 0,e.promise_control.resolve()}}}function le(e,t){let o;return null==t&&Be(!1,`sourcePrefix must be provided for ${e.name}`),e.resolvedUrl?o=e.resolvedUrl:(o=""===t?"assembly"===e.behavior||"pdb"===e.behavior?e.name:"resource"===e.behavior&&e.culture&&""!==e.culture?`${e.culture}/${e.name}`:e.name:t+e.name,o=ce(Pe.locateFile(o),e.behavior)),o&&"string"==typeof o||Be(!1,"attemptUrl need to be path or url string"),o}function ce(e,t){return Pe.modulesUniqueQuery&&q[t]&&(e+=Pe.modulesUniqueQuery),e}let de=0;const ue=new Set;function fe(e){try{e.resolvedUrl||Be(!1,"Request's resolvedUrl must be set");const t=function(e){let t=e.resolvedUrl;if(Pe.loadBootResource){const o=ge(e);if(o instanceof Promise)return o;"string"==typeof o&&(t=o)}const o={};return e.cache?o.cache=e.cache:Pe.config.disableNoCacheFetch||(o.cache="no-cache"),e.useCredentials?o.credentials="include":!Pe.config.disableIntegrityCheck&&e.hash&&(o.integrity=e.hash),Pe.fetch_like(t,o)}(e),o={name:e.name,url:e.resolvedUrl,response:t};return ue.add(e.name),o.response.then((()=>{"assembly"==e.behavior&&Pe.loadedAssemblies.push(e.name),de++,Pe.onDownloadResourceProgress&&Pe.onDownloadResourceProgress(de,ue.size)})),o}catch(t){const o={ok:!1,url:e.resolvedUrl,status:500,statusText:"ERR29: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t}};return{name:e.name,url:e.resolvedUrl,response:Promise.resolve(o)}}}const me={resource:"assembly",assembly:"assembly",pdb:"pdb",icu:"globalization",vfs:"configuration",manifest:"manifest",dotnetwasm:"dotnetwasm","js-module-dotnet":"dotnetjs","js-module-native":"dotnetjs","js-module-runtime":"dotnetjs","js-module-threads":"dotnetjs"};function ge(e){var t;if(Pe.loadBootResource){const o=null!==(t=e.hash)&&void 0!==t?t:"",n=e.resolvedUrl,r=me[e.behavior];if(r){const t=Pe.loadBootResource(r,e.name,n,o,e.behavior);return"string"==typeof t?I(t):t}}}function pe(e){e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null}function he(e){let t=e.lastIndexOf("/");return t>=0&&t++,e.substring(t)}async function we(e){e&&await Promise.all((null!=e?e:[]).map((e=>async function(e){try{const t=e.name;if(!e.moduleExports){const o=ce(Pe.locateFile(t),"js-module-library-initializer");Pe.diagnosticTracing&&b(`Attempting to import '${o}' for ${e}`),e.moduleExports=await import(/*! webpackIgnore: true */o)}Pe.libraryInitializers.push({scriptName:t,exports:e.moduleExports})}catch(t){E(`Failed to import library initializer '${e}': ${t}`)}}(e))))}async function be(e,t){if(!Pe.libraryInitializers)return;const o=[];for(let n=0;n<Pe.libraryInitializers.length;n++){const r=Pe.libraryInitializers[n];r.exports[e]&&o.push(ye(r.scriptName,e,(()=>r.exports[e](...t))))}await Promise.all(o)}async function ye(e,t,o){try{await o()}catch(o){throw E(`Failed to invoke '${t}' on library initializer '${e}': ${o}`),Xe(1,o),o}}function ve(e,t){if(e===t)return e;const o={...t};return void 0!==o.assets&&o.assets!==e.assets&&(o.assets=[...e.assets||[],...o.assets||[]]),void 0!==o.resources&&(o.resources=_e(e.resources||{assembly:[],jsModuleNative:[],jsModuleRuntime:[],wasmNative:[]},o.resources)),void 0!==o.environmentVariables&&(o.environmentVariables={...e.environmentVariables||{},...o.environmentVariables||{}}),void 0!==o.runtimeOptions&&o.runtimeOptions!==e.runtimeOptions&&(o.runtimeOptions=[...e.runtimeOptions||[],...o.runtimeOptions||[]]),Object.assign(e,o)}function Ee(e,t){if(e===t)return e;const o={...t};return o.config&&(e.config||(e.config={}),o.config=ve(e.config,o.config)),Object.assign(e,o)}function _e(e,t){if(e===t)return e;const o={...t};return void 0!==o.coreAssembly&&(o.coreAssembly=[...e.coreAssembly||[],...o.coreAssembly||[]]),void 0!==o.assembly&&(o.assembly=[...e.assembly||[],...o.assembly||[]]),void 0!==o.lazyAssembly&&(o.lazyAssembly=[...e.lazyAssembly||[],...o.lazyAssembly||[]]),void 0!==o.corePdb&&(o.corePdb=[...e.corePdb||[],...o.corePdb||[]]),void 0!==o.pdb&&(o.pdb=[...e.pdb||[],...o.pdb||[]]),void 0!==o.jsModuleWorker&&(o.jsModuleWorker=[...e.jsModuleWorker||[],...o.jsModuleWorker||[]]),void 0!==o.jsModuleNative&&(o.jsModuleNative=[...e.jsModuleNative||[],...o.jsModuleNative||[]]),void 0!==o.jsModuleDiagnostics&&(o.jsModuleDiagnostics=[...e.jsModuleDiagnostics||[],...o.jsModuleDiagnostics||[]]),void 0!==o.jsModuleRuntime&&(o.jsModuleRuntime=[...e.jsModuleRuntime||[],...o.jsModuleRuntime||[]]),void 0!==o.wasmSymbols&&(o.wasmSymbols=[...e.wasmSymbols||[],...o.wasmSymbols||[]]),void 0!==o.wasmNative&&(o.wasmNative=[...e.wasmNative||[],...o.wasmNative||[]]),void 0!==o.icu&&(o.icu=[...e.icu||[],...o.icu||[]]),void 0!==o.satelliteResources&&(o.satelliteResources=function(e,t){if(e===t)return e;for(const o in t)e[o]=[...e[o]||[],...t[o]||[]];return e}(e.satelliteResources||{},o.satelliteResources||{})),void 0!==o.modulesAfterConfigLoaded&&(o.modulesAfterConfigLoaded=[...e.modulesAfterConfigLoaded||[],...o.modulesAfterConfigLoaded||[]]),void 0!==o.modulesAfterRuntimeReady&&(o.modulesAfterRuntimeReady=[...e.modulesAfterRuntimeReady||[],...o.modulesAfterRuntimeReady||[]]),void 0!==o.extensions&&(o.extensions={...e.extensions||{},...o.extensions||{}}),void 0!==o.vfs&&(o.vfs=[...e.vfs||[],...o.vfs||[]]),Object.assign(e,o)}function xe(){const e=Pe.config;if(e.environmentVariables=e.environmentVariables||{},e.runtimeOptions=e.runtimeOptions||[],e.resources=e.resources||{assembly:[],jsModuleNative:[],jsModuleWorker:[],jsModuleRuntime:[],wasmNative:[],vfs:[],satelliteResources:{}},e.assets){Pe.diagnosticTracing&&b("config.assets is deprecated, use config.resources instead");for(const t of e.assets){const o={};switch(t.behavior){case"assembly":o.assembly=[t];break;case"pdb":o.pdb=[t];break;case"resource":o.satelliteResources={},o.satelliteResources[t.culture]=[t];break;case"icu":o.icu=[t];break;case"symbols":o.wasmSymbols=[t];break;case"vfs":o.vfs=[t];break;case"dotnetwasm":o.wasmNative=[t];break;case"js-module-threads":o.jsModuleWorker=[t];break;case"js-module-runtime":o.jsModuleRuntime=[t];break;case"js-module-native":o.jsModuleNative=[t];break;case"js-module-diagnostics":o.jsModuleDiagnostics=[t];break;case"js-module-dotnet":break;default:throw new Error(`Unexpected behavior ${t.behavior} of asset ${t.name}`)}_e(e.resources,o)}}e.debugLevel,e.applicationEnvironment||(e.applicationEnvironment="Production"),e.applicationCulture&&(e.environmentVariables.LANG=`${e.applicationCulture}.UTF-8`),Ue.diagnosticTracing=Pe.diagnosticTracing=!!e.diagnosticTracing,Ue.waitForDebugger=e.waitForDebugger,Pe.maxParallelDownloads=e.maxParallelDownloads||Pe.maxParallelDownloads,Pe.enableDownloadRetry=void 0!==e.enableDownloadRetry?e.enableDownloadRetry:Pe.enableDownloadRetry}let je=!1;async function Re(e){var t;if(je)return void await Pe.afterConfigLoaded.promise;let o;try{if(e.configSrc||Pe.config&&0!==Object.keys(Pe.config).length&&(Pe.config.assets||Pe.config.resources)||(e.configSrc="dotnet.boot.js"),o=e.configSrc,je=!0,o&&(Pe.diagnosticTracing&&b("mono_wasm_load_config"),await async function(e){const t=e.configSrc,o=Pe.locateFile(t);let n=null;void 0!==Pe.loadBootResource&&(n=Pe.loadBootResource("manifest",t,o,"","manifest"));let r,i=null;if(n)if("string"==typeof n)n.includes(".json")?(i=await s(I(n)),r=await Ae(i)):r=(await import(I(n))).config;else{const e=await n;"function"==typeof e.json?(i=e,r=await Ae(i)):r=e.config}else o.includes(".json")?(i=await s(ce(o,"manifest")),r=await Ae(i)):r=(await import(ce(o,"manifest"))).config;function s(e){return Pe.fetch_like(e,{method:"GET",credentials:"include",cache:"no-cache"})}Pe.config.applicationEnvironment&&(r.applicationEnvironment=Pe.config.applicationEnvironment),ve(Pe.config,r)}(e)),xe(),await we(null===(t=Pe.config.resources)||void 0===t?void 0:t.modulesAfterConfigLoaded),await be("onRuntimeConfigLoaded",[Pe.config]),e.onConfigLoaded)try{await e.onConfigLoaded(Pe.config,Le),xe()}catch(e){throw _("onConfigLoaded() failed",e),e}xe(),Pe.afterConfigLoaded.promise_control.resolve(Pe.config)}catch(t){const n=`Failed to load config file ${o} ${t} ${null==t?void 0:t.stack}`;throw Pe.config=e.config=Object.assign(Pe.config,{message:n,error:t,isError:!0}),Xe(1,new Error(n)),t}}function Te(){return!!globalThis.navigator&&(Pe.isChromium||Pe.isFirefox)}async function Ae(e){const t=Pe.config,o=await e.json();t.applicationEnvironment||o.applicationEnvironment||(o.applicationEnvironment=e.headers.get("Blazor-Environment")||e.headers.get("DotNet-Environment")||void 0),o.environmentVariables||(o.environmentVariables={});const n=e.headers.get("DOTNET-MODIFIABLE-ASSEMBLIES");n&&(o.environmentVariables.DOTNET_MODIFIABLE_ASSEMBLIES=n);const r=e.headers.get("ASPNETCORE-BROWSER-TOOLS");return r&&(o.environmentVariables.__ASPNETCORE_BROWSER_TOOLS=r),o}"function"!=typeof importScripts||globalThis.onmessage||(globalThis.dotnetSidecar=!0);const Se="object"==typeof process&&"object"==typeof process.versions&&"string"==typeof process.versions.node,De="function"==typeof importScripts,Oe=De&&"undefined"!=typeof dotnetSidecar,Ce=De&&!Oe,ke="object"==typeof window||De&&!Se,Ie=!ke&&!Se;let Ue={},Pe={},Me={},Le={},Ne={},$e=!1;const ze={},We={config:ze},Fe={mono:{},binding:{},internal:Ne,module:We,loaderHelpers:Pe,runtimeHelpers:Ue,diagnosticHelpers:Me,api:Le};function Be(e,t){if(e)return;const o="Assert failed: "+("function"==typeof t?t():t),n=new Error(o);_(o,n),Ue.nativeAbort(n)}function Ve(){return void 0!==Pe.exitCode}function qe(){return Ue.runtimeReady&&!Ve()}function He(){Ve()&&Be(!1,`.NET runtime already exited with ${Pe.exitCode} ${Pe.exitReason}. You can use runtime.runMain() which doesn't exit the runtime.`),Ue.runtimeReady||Be(!1,".NET runtime didn't start yet. Please call dotnet.create() first.")}function Je(){ke&&(globalThis.addEventListener("unhandledrejection",et),globalThis.addEventListener("error",tt))}let Ze,Qe;function Ge(e){Qe&&Qe(e),Xe(e,Pe.exitReason)}function Ke(e){Ze&&Ze(e||Pe.exitReason),Xe(1,e||Pe.exitReason)}function Xe(t,o){var n,r;const i=o&&"object"==typeof o;t=i&&"number"==typeof o.status?o.status:void 0===t?-1:t;const s=i&&"string"==typeof o.message?o.message:""+o;(o=i?o:Ue.ExitStatus?function(e,t){const o=new Ue.ExitStatus(e);return o.message=t,o.toString=()=>t,o}(t,s):new Error("Exit with code "+t+" "+s)).status=t,o.message||(o.message=s);const a=""+(o.stack||(new Error).stack);try{Object.defineProperty(o,"stack",{get:()=>a})}catch(e){}const l=!!o.silent;if(o.silent=!0,Ve())Pe.diagnosticTracing&&b("mono_exit called after exit");else{try{We.onAbort==Ke&&(We.onAbort=Ze),We.onExit==Ge&&(We.onExit=Qe),ke&&(globalThis.removeEventListener("unhandledrejection",et),globalThis.removeEventListener("error",tt)),Ue.runtimeReady?(Ue.jiterpreter_dump_stats&&Ue.jiterpreter_dump_stats(!1),0===t&&(null===(n=Pe.config)||void 0===n?void 0:n.interopCleanupOnExit)&&Ue.forceDisposeProxies(!0,!0),e&&0!==t&&(null===(r=Pe.config)||void 0===r||r.dumpThreadsOnNonZeroExit)):(Pe.diagnosticTracing&&b(`abort_startup, reason: ${o}`),function(e){Pe.allDownloadsQueued.promise_control.reject(e),Pe.allDownloadsFinished.promise_control.reject(e),Pe.afterConfigLoaded.promise_control.reject(e),Pe.wasmCompilePromise.promise_control.reject(e),Pe.runtimeModuleLoaded.promise_control.reject(e),Ue.dotnetReady&&(Ue.dotnetReady.promise_control.reject(e),Ue.afterInstantiateWasm.promise_control.reject(e),Ue.beforePreInit.promise_control.reject(e),Ue.afterPreInit.promise_control.reject(e),Ue.afterPreRun.promise_control.reject(e),Ue.beforeOnRuntimeInitialized.promise_control.reject(e),Ue.afterOnRuntimeInitialized.promise_control.reject(e),Ue.afterPostRun.promise_control.reject(e))}(o))}catch(e){E("mono_exit A failed",e)}try{l||(function(e,t){if(0!==e&&t){const e=Ue.ExitStatus&&t instanceof Ue.ExitStatus?b:_;"string"==typeof t?e(t):(void 0===t.stack&&(t.stack=(new Error).stack+""),t.message?e(Ue.stringify_as_error_with_stack?Ue.stringify_as_error_with_stack(t.message+"\n"+t.stack):t.message+"\n"+t.stack):e(JSON.stringify(t)))}!Ce&&Pe.config&&(Pe.config.logExitCode?Pe.config.forwardConsoleLogsToWS?R("WASM EXIT "+e):v("WASM EXIT "+e):Pe.config.forwardConsoleLogsToWS&&R())}(t,o),function(e){if(ke&&!Ce&&Pe.config&&Pe.config.appendElementOnExit&&document){const t=document.createElement("label");t.id="tests_done",0!==e&&(t.style.background="red"),t.innerHTML=""+e,document.body.appendChild(t)}}(t))}catch(e){E("mono_exit B failed",e)}Pe.exitCode=t,Pe.exitReason||(Pe.exitReason=o),!Ce&&Ue.runtimeReady&&We.runtimeKeepalivePop()}if(Pe.config&&Pe.config.asyncFlushOnExit&&0===t)throw(async()=>{try{await async function(){try{const e=await import(/*! webpackIgnore: true */"process"),t=e=>new Promise(((t,o)=>{e.on("error",o),e.end("","utf8",t)})),o=t(e.stderr),n=t(e.stdout);let r;const i=new Promise((e=>{r=setTimeout((()=>e("timeout")),1e3)}));await Promise.race([Promise.all([n,o]),i]),clearTimeout(r)}catch(e){_(`flushing std* streams failed: ${e}`)}}()}finally{Ye(t,o)}})(),o;Ye(t,o)}function Ye(e,t){if(Ue.runtimeReady&&Ue.nativeExit)try{Ue.nativeExit(e)}catch(e){!Ue.ExitStatus||e instanceof Ue.ExitStatus||E("set_exit_code_and_quit_now failed: "+e.toString())}if(0!==e||!ke)throw Se&&Ne.process?Ne.process.exit(e):Ue.quit&&Ue.quit(e,t),t}function et(e){ot(e,e.reason,"rejection")}function tt(e){ot(e,e.error,"error")}function ot(e,t,o){e.preventDefault();try{t||(t=new Error("Unhandled "+o)),void 0===t.stack&&(t.stack=(new Error).stack),t.stack=t.stack+"",t.silent||(_("Unhandled error:",t),Xe(1,t))}catch(e){}}!function(e){if($e)throw new Error("Loader module already loaded");$e=!0,Ue=e.runtimeHelpers,Pe=e.loaderHelpers,Me=e.diagnosticHelpers,Le=e.api,Ne=e.internal,Object.assign(Le,{INTERNAL:Ne,invokeLibraryInitializers:be}),Object.assign(e.module,{config:ve(ze,{environmentVariables:{}})});const r={mono_wasm_bindings_is_ready:!1,config:e.module.config,diagnosticTracing:!1,nativeAbort:e=>{throw e||new Error("abort")},nativeExit:e=>{throw new Error("exit:"+e)}},l={gitHash:"f7d90799ce4ef09a0bb257852a57248d2a8fb8dd",config:e.module.config,diagnosticTracing:!1,maxParallelDownloads:16,enableDownloadRetry:!0,_loaded_files:[],loadedFiles:[],loadedAssemblies:[],libraryInitializers:[],workerNextNumber:1,actual_downloaded_assets_count:0,actual_instantiated_assets_count:0,expected_downloaded_assets_count:0,expected_instantiated_assets_count:0,afterConfigLoaded:i(),allDownloadsQueued:i(),allDownloadsFinished:i(),wasmCompilePromise:i(),runtimeModuleLoaded:i(),loadingWorkers:i(),is_exited:Ve,is_runtime_running:qe,assert_runtime_running:He,mono_exit:Xe,createPromiseController:i,getPromiseController:s,assertIsControllablePromise:a,mono_download_assets:oe,resolve_single_asset_path:ee,setup_proxy_console:j,set_thread_prefix:w,installUnhandledErrorHandler:Je,retrieve_asset_download:ie,invokeLibraryInitializers:be,isDebuggingSupported:Te,exceptions:t,simd:n,relaxedSimd:o};Object.assign(Ue,r),Object.assign(Pe,l)}(Fe);let nt,rt,it,st=!1,at=!1;async function lt(e){if(!at){if(at=!0,ke&&Pe.config.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&j("main",globalThis.console,globalThis.location.origin),We||Be(!1,"Null moduleConfig"),Pe.config||Be(!1,"Null moduleConfig.config"),"function"==typeof e){const t=e(Fe.api);if(t.ready)throw new Error("Module.ready couldn't be redefined.");Object.assign(We,t),Ee(We,t)}else{if("object"!=typeof e)throw new Error("Can't use moduleFactory callback of createDotnetRuntime function.");Ee(We,e)}await async function(e){if(Se){const e=await import(/*! webpackIgnore: true */"process"),t=14;if(e.versions.node.split(".")[0]<t)throw new Error(`NodeJS at '${e.execPath}' has too low version '${e.versions.node}', please use at least ${t}. See also https://aka.ms/dotnet-wasm-features`)}const t=/*! webpackIgnore: true */import.meta.url,o=t.indexOf("?");var n;if(o>0&&(Pe.modulesUniqueQuery=t.substring(o)),Pe.scriptUrl=t.replace(/\\/g,"/").replace(/[?#].*/,""),Pe.scriptDirectory=(n=Pe.scriptUrl).slice(0,n.lastIndexOf("/"))+"/",Pe.locateFile=e=>"URL"in globalThis&&globalThis.URL!==C?new URL(e,Pe.scriptDirectory).toString():M(e)?e:Pe.scriptDirectory+e,Pe.fetch_like=k,Pe.out=console.log,Pe.err=console.error,Pe.onDownloadResourceProgress=e.onDownloadResourceProgress,ke&&globalThis.navigator){const e=globalThis.navigator,t=e.userAgentData&&e.userAgentData.brands;t&&t.length>0?Pe.isChromium=t.some((e=>"Google Chrome"===e.brand||"Microsoft Edge"===e.brand||"Chromium"===e.brand)):e.userAgent&&(Pe.isChromium=e.userAgent.includes("Chrome"),Pe.isFirefox=e.userAgent.includes("Firefox"))}Ne.require=Se?await import(/*! webpackIgnore: true */"module").then((e=>e.createRequire(/*! webpackIgnore: true */import.meta.url))):Promise.resolve((()=>{throw new Error("require not supported")})),void 0===globalThis.URL&&(globalThis.URL=C)}(We)}}async function ct(e){return await lt(e),Ze=We.onAbort,Qe=We.onExit,We.onAbort=Ke,We.onExit=Ge,We.ENVIRONMENT_IS_PTHREAD?async function(){(function(){const e=new MessageChannel,t=e.port1,o=e.port2;t.addEventListener("message",(e=>{var n,r;n=JSON.parse(e.data.config),r=JSON.parse(e.data.monoThreadInfo),st?Pe.diagnosticTracing&&b("mono config already received"):(ve(Pe.config,n),Ue.monoThreadInfo=r,xe(),Pe.diagnosticTracing&&b("mono config received"),st=!0,Pe.afterConfigLoaded.promise_control.resolve(Pe.config),ke&&n.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&Pe.setup_proxy_console("worker-idle",console,globalThis.location.origin)),t.close(),o.close()}),{once:!0}),t.start(),self.postMessage({[l]:{monoCmd:"preload",port:o}},[o])})(),await Pe.afterConfigLoaded.promise,function(){const e=Pe.config;e.assets||Be(!1,"config.assets must be defined");for(const t of e.assets)X(t),Q[t.behavior]&&z.push(t)}(),setTimeout((async()=>{try{await oe()}catch(e){Xe(1,e)}}),0);const e=dt(),t=await Promise.all(e);return await ut(t),We}():async function(){var e;await Re(We),re();const t=dt();(async function(){try{const e=ee("dotnetwasm");await se(e),e&&e.pendingDownloadInternal&&e.pendingDownloadInternal.response||Be(!1,"Can't load dotnet.native.wasm");const t=await e.pendingDownloadInternal.response,o=t.headers&&t.headers.get?t.headers.get("Content-Type"):void 0;let n;if("function"==typeof WebAssembly.compileStreaming&&"application/wasm"===o)n=await WebAssembly.compileStreaming(t);else{ke&&"application/wasm"!==o&&E('WebAssembly resource does not have the expected content type "application/wasm", so falling back to slower ArrayBuffer instantiation.');const e=await t.arrayBuffer();Pe.diagnosticTracing&&b("instantiate_wasm_module buffered"),n=Ie?await Promise.resolve(new WebAssembly.Module(e)):await WebAssembly.compile(e)}e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null,Pe.wasmCompilePromise.promise_control.resolve(n)}catch(e){Pe.wasmCompilePromise.promise_control.reject(e)}})(),setTimeout((async()=>{try{D(),await oe()}catch(e){Xe(1,e)}}),0);const o=await Promise.all(t);return await ut(o),await Ue.dotnetReady.promise,await we(null===(e=Pe.config.resources)||void 0===e?void 0:e.modulesAfterRuntimeReady),await be("onRuntimeReady",[Fe.api]),Le}()}function dt(){const e=ee("js-module-runtime"),t=ee("js-module-native");if(nt&&rt)return[nt,rt,it];"object"==typeof e.moduleExports?nt=e.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${e.resolvedUrl}' for ${e.name}`),nt=import(/*! webpackIgnore: true */e.resolvedUrl)),"object"==typeof t.moduleExports?rt=t.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${t.resolvedUrl}' for ${t.name}`),rt=import(/*! webpackIgnore: true */t.resolvedUrl));const o=Y("js-module-diagnostics");return o&&("object"==typeof o.moduleExports?it=o.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${o.resolvedUrl}' for ${o.name}`),it=import(/*! webpackIgnore: true */o.resolvedUrl))),[nt,rt,it]}async function ut(e){const{initializeExports:t,initializeReplacements:o,configureRuntimeStartup:n,configureEmscriptenStartup:r,configureWorkerStartup:i,setRuntimeGlobals:s,passEmscriptenInternals:a}=e[0],{default:l}=e[1],c=e[2];s(Fe),t(Fe),c&&c.setRuntimeGlobals(Fe),await n(We),Pe.runtimeModuleLoaded.promise_control.resolve(),l((e=>(Object.assign(We,{ready:e.ready,__dotnet_runtime:{initializeReplacements:o,configureEmscriptenStartup:r,configureWorkerStartup:i,passEmscriptenInternals:a}}),We))).catch((e=>{if(e.message&&e.message.toLowerCase().includes("out of memory"))throw new Error(".NET runtime has failed to start, because too much memory was requested. Please decrease the memory by adjusting EmccMaximumHeapSize. See also https://aka.ms/dotnet-wasm-features");throw e}))}const ft=new class{withModuleConfig(e){try{return Ee(We,e),this}catch(e){throw Xe(1,e),e}}withOnConfigLoaded(e){try{return Ee(We,{onConfigLoaded:e}),this}catch(e){throw Xe(1,e),e}}withConsoleForwarding(){try{return ve(ze,{forwardConsoleLogsToWS:!0}),this}catch(e){throw Xe(1,e),e}}withExitOnUnhandledError(){try{return ve(ze,{exitOnUnhandledError:!0}),Je(),this}catch(e){throw Xe(1,e),e}}withAsyncFlushOnExit(){try{return ve(ze,{asyncFlushOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withExitCodeLogging(){try{return ve(ze,{logExitCode:!0}),this}catch(e){throw Xe(1,e),e}}withElementOnExit(){try{return ve(ze,{appendElementOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withInteropCleanupOnExit(){try{return ve(ze,{interopCleanupOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withDumpThreadsOnNonZeroExit(){try{return ve(ze,{dumpThreadsOnNonZeroExit:!0}),this}catch(e){throw Xe(1,e),e}}withWaitingForDebugger(e){try{return ve(ze,{waitForDebugger:e}),this}catch(e){throw Xe(1,e),e}}withInterpreterPgo(e,t){try{return ve(ze,{interpreterPgo:e,interpreterPgoSaveDelay:t}),ze.runtimeOptions?ze.runtimeOptions.push("--interp-pgo-recording"):ze.runtimeOptions=["--interp-pgo-recording"],this}catch(e){throw Xe(1,e),e}}withConfig(e){try{return ve(ze,e),this}catch(e){throw Xe(1,e),e}}withConfigSrc(e){try{return e&&"string"==typeof e||Be(!1,"must be file path or URL"),Ee(We,{configSrc:e}),this}catch(e){throw Xe(1,e),e}}withVirtualWorkingDirectory(e){try{return e&&"string"==typeof e||Be(!1,"must be directory path"),ve(ze,{virtualWorkingDirectory:e}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariable(e,t){try{const o={};return o[e]=t,ve(ze,{environmentVariables:o}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariables(e){try{return e&&"object"==typeof e||Be(!1,"must be dictionary object"),ve(ze,{environmentVariables:e}),this}catch(e){throw Xe(1,e),e}}withDiagnosticTracing(e){try{return"boolean"!=typeof e&&Be(!1,"must be boolean"),ve(ze,{diagnosticTracing:e}),this}catch(e){throw Xe(1,e),e}}withDebugging(e){try{return null!=e&&"number"==typeof e||Be(!1,"must be number"),ve(ze,{debugLevel:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArguments(...e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ve(ze,{applicationArguments:e}),this}catch(e){throw Xe(1,e),e}}withRuntimeOptions(e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ze.runtimeOptions?ze.runtimeOptions.push(...e):ze.runtimeOptions=e,this}catch(e){throw Xe(1,e),e}}withMainAssembly(e){try{return ve(ze,{mainAssemblyName:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArgumentsFromQuery(){try{if(!globalThis.window)throw new Error("Missing window to the query parameters from");if(void 0===globalThis.URLSearchParams)throw new Error("URLSearchParams is supported");const e=new URLSearchParams(globalThis.window.location.search).getAll("arg");return this.withApplicationArguments(...e)}catch(e){throw Xe(1,e),e}}withApplicationEnvironment(e){try{return ve(ze,{applicationEnvironment:e}),this}catch(e){throw Xe(1,e),e}}withApplicationCulture(e){try{return ve(ze,{applicationCulture:e}),this}catch(e){throw Xe(1,e),e}}withResourceLoader(e){try{return Pe.loadBootResource=e,this}catch(e){throw Xe(1,e),e}}async download(){try{await async function(){lt(We),await Re(We),re(),D(),oe(),await Pe.allDownloadsFinished.promise}()}catch(e){throw Xe(1,e),e}}async create(){try{return this.instance||(this.instance=await async function(){return await ct(We),Fe.api}()),this.instance}catch(e){throw Xe(1,e),e}}async run(){try{return We.config||Be(!1,"Null moduleConfig.config"),this.instance||await this.create(),this.instance.runMainAndExit()}catch(e){throw Xe(1,e),e}}},mt=Xe,gt=ct;Ie||"function"==typeof globalThis.URL||Be(!1,"This browser/engine doesn't support URL API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),"function"!=typeof globalThis.BigInt64Array&&Be(!1,"This browser/engine doesn't support BigInt64Array API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),ft.withConfig(/*json-start*/{
  "mainAssemblyName": "GalleryBrowser",
  "resources": {
    "hash": "sha256-UUr/UVVeLrCvknZHYrQDTLvOu9K1ghJD+8YPlluKVYI=",
    "jsModuleNative": [
      {
        "name": "dotnet.native.615wqt18fb.js"
      }
    ],
    "jsModuleRuntime": [
      {
        "name": "dotnet.runtime.web2r9gqbh.js"
      }
    ],
    "wasmNative": [
      {
        "name": "dotnet.native.sjmzfu2yl0.wasm",
        "hash": "sha256-GFyPwOJab+HuGUQ8M+9l7P4xnbzttK78a5U8uCAIBpk=",
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
        "name": "System.Private.CoreLib.vqxmumosg3.wasm",
        "hash": "sha256-V8qP8aFHIKY7Q2w0UGyKilaaipXQIq3j47YKq+pFhuU=",
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
        "name": "Friflo.Engine.ECS.hq3qowaj7h.wasm",
        "hash": "sha256-aY0+qGxoBOJNrVE0eX5rod43Pdzv8YITvdEo7etDFbA=",
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
        "name": "GalleryBrowser.qzx3t6poci.wasm",
        "hash": "sha256-1nP8Y6Sz9T13MDYS8nsKbRWSOnP99qr2sykSQ6aMC1g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "HarfBuzzSharp.wasm",
        "name": "HarfBuzzSharp.ez5l8urr9q.wasm",
        "hash": "sha256-dj7cVX2w0eXm88Nf53goNDmdtpiWS0JBQlalgs3V2Fo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Humanizer.wasm",
        "name": "Humanizer.oqup3v7t3k.wasm",
        "hash": "sha256-4NbSboZzzP9nikRtXapUZNzOyITt7ht9TNqCIQHr5OE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Animation.Gallery.wasm",
        "name": "Luxel.Animation.Gallery.1pt5ma3qep.wasm",
        "hash": "sha256-a+xb3P3HKIzKcH4PzpkHydYEFm33/t9X+bD5Mrvk5WY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Animation.ThreeD.wasm",
        "name": "Luxel.Animation.ThreeD.nxp66hb7tk.wasm",
        "hash": "sha256-NR1WAyRxrgj0EPRS/Yjw7F91INVJcuVLXPedhsNd3Bo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Animation.TwoD.wasm",
        "name": "Luxel.Animation.TwoD.utairk970p.wasm",
        "hash": "sha256-UiJVmai2KnoXEe3HzhDsjVwn9Od3A+8d64K1bOmPfHc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Animation.UI.wasm",
        "name": "Luxel.Animation.UI.ruuv273590.wasm",
        "hash": "sha256-89tRPkj3z2U3ANAu1VQNJobEj4w6YVknawBbxP1CnH8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Animation.wasm",
        "name": "Luxel.Animation.lrnuj57u51.wasm",
        "hash": "sha256-0epvCgM99xMtgFHTFQOmU4oLKKM9jrat19XzDBfkxHA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.AssetRuntime.wasm",
        "name": "Luxel.AssetRuntime.yt2qh9gkat.wasm",
        "hash": "sha256-1gUyDIQkHRUeA6NoU0skN442O4BMWjrmlOu2MkbLeCU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Assets.Gltf.wasm",
        "name": "Luxel.Assets.Gltf.6xq3m9ufb3.wasm",
        "hash": "sha256-CuVWWL/FyolgB+9GTsnVLbEenD+WQX7ZiiEI/vtzYNw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Assets.wasm",
        "name": "Luxel.Assets.g3hnhhe72r.wasm",
        "hash": "sha256-TOstIK63skgpUZzS90EjqkXbIOt9WWtAnqCh34HsmRg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.AssetsGpu.wasm",
        "name": "Luxel.AssetsGpu.si7iu6lc06.wasm",
        "hash": "sha256-lPx8zGuAdXgxgBrpAP5KzenI3Wq12hVgaajejW4n7J0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Audio.Browser.wasm",
        "name": "Luxel.Audio.Browser.3pa80zmsyi.wasm",
        "hash": "sha256-J7TeNKb12DoHGqB96NSwyt/U7qBp8LJIfGbghnCSHOw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Audio.Gallery.wasm",
        "name": "Luxel.Audio.Gallery.boppw96h2h.wasm",
        "hash": "sha256-xS1Xvx9L9OOr0lMdCbrQFscbgkEEMUps81fnqTfGC+A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Audio.wasm",
        "name": "Luxel.Audio.tcks7ds6z9.wasm",
        "hash": "sha256-864LkXpVpuH3BGnDBMyGHVwas6H9S0P01feh1kxIlkg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Controls.wasm",
        "name": "Luxel.Controls.uqpd57fncz.wasm",
        "hash": "sha256-aut5e0Bgrm3dwL8dLXP0vwIwjP74JnNsqf5vvfz1098=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.DevTools.Gallery.wasm",
        "name": "Luxel.DevTools.Gallery.f254crqjx5.wasm",
        "hash": "sha256-pyoBBu3TM/IwSERPnBxSts+Uk1s1eXMzaeEyboMfbLs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.DevTools.wasm",
        "name": "Luxel.DevTools.f9am7ohaym.wasm",
        "hash": "sha256-jcsExNtaGsmUoioCHM5AWYVwQ1BAeQipiiiyYOkrVJg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Diagnostics.wasm",
        "name": "Luxel.Diagnostics.5w23mrk8nz.wasm",
        "hash": "sha256-m2xoCM0r70eDUXTm0HhCe08NJ8x0+HAuzvu7gKFIwZQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Diagram.wasm",
        "name": "Luxel.Diagram.wr7a5b0tcn.wasm",
        "hash": "sha256-JEkO/X6Ci77uknJ/E/CMuOzRyDVVstXvqgAcu4HYpkk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Document.wasm",
        "name": "Luxel.Document.t3fnenw4i9.wasm",
        "hash": "sha256-OTYd6pHoW/8JiQYwM9zh6i+CX2b/gVVYJ0+nLC+VyNc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Ecs.wasm",
        "name": "Luxel.Ecs.750duq5ol3.wasm",
        "hash": "sha256-Bej+dNbBjATDzwMZAVUyMOWziWqo216CA95+Bat8zI8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Ecs.Signal.wasm",
        "name": "Luxel.Ecs.Signal.0758lb62px.wasm",
        "hash": "sha256-tBK3ybotexvB7DZ5rIKIgIsM+FQURmr2Ftzyrq0rzUU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Editor.Gallery.wasm",
        "name": "Luxel.Editor.Gallery.iqjnbfzz5k.wasm",
        "hash": "sha256-qjoBl4fV4Y9cXQpJ8SBGws2DTeu3X/1z66be9YZLFFc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Framework.Gallery.wasm",
        "name": "Luxel.Framework.Gallery.k86yhpal3p.wasm",
        "hash": "sha256-FOk+BU1ENalclAQlsWgTYk5G6Dz9ivyGDXS2Y8f4LbI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Framework.Game.wasm",
        "name": "Luxel.Framework.Game.s0dqjq5r5e.wasm",
        "hash": "sha256-bG+nrGPoXfYhzhkSjoAxxI23dTY7yqGTNzU5clvVi9U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.Browser.wasm",
        "name": "Luxel.Gallery.Browser.twlfoehxzc.wasm",
        "hash": "sha256-rXzg80IJlEgFk1L1ONohmiYnqhwUNpscwvDuHn5HHX4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.DocKit.wasm",
        "name": "Luxel.Gallery.DocKit.2xz3uppele.wasm",
        "hash": "sha256-ajW1uFx3wa2AkVTBHiZfh5MMNEzqRiIdBTDCGjVwJPI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.Docs.wasm",
        "name": "Luxel.Gallery.Docs.dd2betgvjl.wasm",
        "hash": "sha256-wQIFaonkPtpYaIUPFNsXPbkjDRfZhmUlo/r2kcMnrWw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.Playground.wasm",
        "name": "Luxel.Gallery.Playground.s7khto8udl.wasm",
        "hash": "sha256-mB2oAcCRi8IB0n5K5sRfHjBmqXqIGDoOKFG2ur9ZO/w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.UI.wasm",
        "name": "Luxel.Gallery.UI.z2grz6wyby.wasm",
        "hash": "sha256-K+VNkAwIsGNvVW1jdGA1sEfJyTncuY/fatBAEz5QyFM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.wasm",
        "name": "Luxel.Gallery.bbdv58szhq.wasm",
        "hash": "sha256-2eXiQIdzrTCCK9LaZDSQF5HAOQDB4gvxOyXNo56PyEI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.GamesSamples.Gallery.wasm",
        "name": "Luxel.GamesSamples.Gallery.f7j55os2s6.wasm",
        "hash": "sha256-e7WJa+CKBeds9tnvW7wsOeVyaosHWjD5yu00teVmY80=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.Gallery.wasm",
        "name": "Luxel.Graphics.Gallery.ze5v7xh6hn.wasm",
        "hash": "sha256-1be0d8wuTiKIpmScmgfOUquyw1h4hjJQvYGBdL6q7xc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.MessagePipe.wasm",
        "name": "Luxel.Graphics.MessagePipe.35yia66baj.wasm",
        "hash": "sha256-V7/aoS7xy8Z6gYOrEVq0CGlUsBKt/bkh35taE6iQkB0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.RenderGraph.wasm",
        "name": "Luxel.Graphics.RenderGraph.tl0oh0h7yk.wasm",
        "hash": "sha256-72XV/5kopsa5L8Njyo3mHVW+/CbbK8r2VTUYkSmIE2k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.TwoD.wasm",
        "name": "Luxel.Graphics.TwoD.hmx3s120v5.wasm",
        "hash": "sha256-+SycnKf2WnMMz+wdmL75vkyLwBFnzno6RmkYln4fX44=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.WebGPU.Browser.wasm",
        "name": "Luxel.Graphics.WebGPU.Browser.0t5e6g882x.wasm",
        "hash": "sha256-5JoOL0Yh8UleXLlsnGihkId3HLn4aW7QbFqY4PSxRt4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.wasm",
        "name": "Luxel.Graphics.icjp2g5b8h.wasm",
        "hash": "sha256-MsCz6jWiAcwmiXqS6DRHCeHuJYGg9H2h4fuQdh4PobY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Highlight.TextMate.wasm",
        "name": "Luxel.Highlight.TextMate.lfybd3qbh4.wasm",
        "hash": "sha256-7wMlvb307MuM0vIDJvaeHsymG6Pb6yALO4nnsZwRBGg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Imaging.wasm",
        "name": "Luxel.Imaging.pna529fya3.wasm",
        "hash": "sha256-NTvzFxJQ81M/xJHb38ALYLs8TfAqIEntxU7EQ3i+8/U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Input.wasm",
        "name": "Luxel.Input.39rpdu8zos.wasm",
        "hash": "sha256-yQS+BVXkdkTmgpSkqxCzcvsnHL78KeCt74fFdEuXCzg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Input.Gallery.wasm",
        "name": "Luxel.Input.Gallery.w1i6122yl8.wasm",
        "hash": "sha256-HNaky4FG1NbHutavnH66N/HxFX/R1S0KMsnFrGKB/sY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.MathText.wasm",
        "name": "Luxel.MathText.n5yob3r3nr.wasm",
        "hash": "sha256-FfZP0HnSNdm5FHKk+WTKpIh1VCYkLICxy72t8t4pI60=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Mathematics.wasm",
        "name": "Luxel.Mathematics.j3ytnwy42s.wasm",
        "hash": "sha256-32iSZTq5VSZvI3HBcwYnnFcpX9u1cIiIM8TTSuk8zmI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.NodeGraph.wasm",
        "name": "Luxel.NodeGraph.4xrt3gya76.wasm",
        "hash": "sha256-aApWB1HFWfzdRfeijRhtgw2lXuu58+1JoVoL0WUJ+ng=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Particles.Gallery.wasm",
        "name": "Luxel.Particles.Gallery.qb2zvvjwbg.wasm",
        "hash": "sha256-en6BdQRnXXS2DRtal4OHhtfcgDcoCet8SntlxFet5do=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Particles.ThreeD.wasm",
        "name": "Luxel.Particles.ThreeD.dlc30xo33m.wasm",
        "hash": "sha256-fznPSJqN/P2E4yWwZX8K/7NW+ajBNjcGwrrP2MlPtc4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Particles.TwoD.wasm",
        "name": "Luxel.Particles.TwoD.fix62eovjb.wasm",
        "hash": "sha256-2ZbQu+q7X8CsYwFxQwlVg/gFGzwojGKAgYH1nrluCJ4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Particles.UI.wasm",
        "name": "Luxel.Particles.UI.y4bv6bzpup.wasm",
        "hash": "sha256-3nOFSonHoOme9dQg9Qz5Ksr1J9Nr8NSfSyDfyPEOHpE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Particles.wasm",
        "name": "Luxel.Particles.wsu5h0jytl.wasm",
        "hash": "sha256-0HPk06SrWKjgExvuPsWDmmuToP5TyLnX+Z+7XQ6SmrM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Physics.wasm",
        "name": "Luxel.Physics.2z5evr21fd.wasm",
        "hash": "sha256-Qr/MlgqrDpaxxh2QztXmOy5nUZgRppOCYt0gNtc4sbU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Physics.Gizmos.wasm",
        "name": "Luxel.Physics.Gizmos.eqnzqin45v.wasm",
        "hash": "sha256-5rQXMh6zHT9Al93tqxq3mLztMreMFfQzkfc3NTYMZIc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Platform.Web.wasm",
        "name": "Luxel.Platform.Web.ugp2tqsph5.wasm",
        "hash": "sha256-ki1TGfdwKnWE59hiy4lW09QbK2OLB3WFdRyolCpmdfY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Platform.wasm",
        "name": "Luxel.Platform.lzq244avv3.wasm",
        "hash": "sha256-IWneQRy5qm8vJRmSVgA7hzoj8XqTpq0w44gpBRbDSpI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Player.wasm",
        "name": "Luxel.Player.v1judxd0lz.wasm",
        "hash": "sha256-DSMpBYYGtczgAPf9OTHLDogmVg75waEB0sys1GiPkiM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Resources.Browser.wasm",
        "name": "Luxel.Resources.Browser.b91wsph96v.wasm",
        "hash": "sha256-oUHvfqEL+cK8+HT8Rc4xwVxho+PdR115vkjE81KGH5U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Resources.Gallery.wasm",
        "name": "Luxel.Resources.Gallery.f5r3e7yyfl.wasm",
        "hash": "sha256-zWmEEkLCaGCncqYbpek4tAwyui+tNQ+qv4/CvO7jZqc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Resources.wasm",
        "name": "Luxel.Resources.k069gvb6x3.wasm",
        "hash": "sha256-tKJijpYtuaAyuWq0kZAAIsFu6ciQUjrEVoMSAHkSI+0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Scene.UI.wasm",
        "name": "Luxel.Scene.UI.te5vop37f2.wasm",
        "hash": "sha256-JTmPTi/mcAR6ex7VsGMA2zoZ6AfUbUyQLluR4stKTnE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.SceneEdit.wasm",
        "name": "Luxel.SceneEdit.tvpqd617wr.wasm",
        "hash": "sha256-idpuEcYosq49zU2r+5frgsjUd5QbIuzUDq/zRnamoYo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Scripting.Framework.wasm",
        "name": "Luxel.Scripting.Framework.qnhjw6gi3b.wasm",
        "hash": "sha256-2qf9p6zffoDDr0jtVQokaO96RMMmBrqJ2IIbnTTSKwc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Scripting.Gallery.wasm",
        "name": "Luxel.Scripting.Gallery.nkoh71resi.wasm",
        "hash": "sha256-p9EMrEsaypB7Ow7vHGGENio27o/NilnzQQkIK8uXLjY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Scripting.Roslyn.Web.wasm",
        "name": "Luxel.Scripting.Roslyn.Web.ehisjd9tx7.wasm",
        "hash": "sha256-kmOX9h6JjuwklQsHatP6H/wymkUDY9O97p8DuMjbsXs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Scripting.wasm",
        "name": "Luxel.Scripting.anffu4thev.wasm",
        "hash": "sha256-CoUoBdUuLCi0txgwn2mweqmkG780Z2pKvevi1lWys8w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Settings.wasm",
        "name": "Luxel.Settings.azs3i0nu9b.wasm",
        "hash": "sha256-igjl/qhoIr7YX95mSHn6xF8H/8aqquhYFYb9rv6MeWQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Shaders.Slang.Browser.wasm",
        "name": "Luxel.Shaders.Slang.Browser.8mbv6ct51c.wasm",
        "hash": "sha256-kNLsyrVBqXt56YCC0fAmni82kfgQmeRQlxl+rwuGyj4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Shaders.Slang.Native.wasm",
        "name": "Luxel.Shaders.Slang.Native.uefhbtvorr.wasm",
        "hash": "sha256-RtdamcL8IK8QapSe1ih72vI3kAL/DWmYD0CI5j+f/J4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Shaders.wasm",
        "name": "Luxel.Shaders.n3yb99lqrz.wasm",
        "hash": "sha256-nUFroQwKTwnyvgAtbMB0pdjlF1wWkajbqrF9TbdUXcI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Strudel.wasm",
        "name": "Luxel.Strudel.o4msfy480z.wasm",
        "hash": "sha256-CNbI+r1MY4MJpSEGRwvl5ZdingkIICYcNwKoso81CgE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Terminal.UI.wasm",
        "name": "Luxel.Terminal.UI.4oreivjm82.wasm",
        "hash": "sha256-bJ5WYsCEauXl+T+s5RpCMjtq7DNK0+67FvDDlaLajUg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Terminal.wasm",
        "name": "Luxel.Terminal.muaka96q27.wasm",
        "hash": "sha256-Ep7mEtcvqNSbTbZ+EBF1+/89LdRb0XrqOW0JF8vMUE8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Typography.TwoD.wasm",
        "name": "Luxel.Typography.TwoD.4xn2polxlx.wasm",
        "hash": "sha256-mI4tpO9wonVobA0ncT5koWA1uUCEVSwzNE5bNmJWInE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Typography.wasm",
        "name": "Luxel.Typography.bnzyh7s87w.wasm",
        "hash": "sha256-CSra9FHWO3vLohjubatITE72wNE8bTAHCeLXRqOMuxw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.UI.wasm",
        "name": "Luxel.UI.6ypul8buys.wasm",
        "hash": "sha256-3mP4VD9CiEI/0xtfU3wO9TJ2ADtWXM+tClXpmkTiLVU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.UI.Gallery.wasm",
        "name": "Luxel.UI.Gallery.pykp4dhd2x.wasm",
        "hash": "sha256-n6G9091UpEPKHbTmQaoVnZuYKo9GtVmqaiQgQB68zqI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.UI.Tailwind.wasm",
        "name": "Luxel.UI.Tailwind.t47bhh0abp.wasm",
        "hash": "sha256-I/JxBjT7NN2JoxFoJ9M2ggrDzG37hFCOhCtuuBV+Tbk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Workbench.wasm",
        "name": "Luxel.Workbench.kpgqpq4whq.wasm",
        "hash": "sha256-6IeKjFts8qf5yyjAePD8GVY4G8cPGb8CXlerMh/hM4s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "LuxelCavern.Core.wasm",
        "name": "LuxelCavern.Core.z4cxqpxgqh.wasm",
        "hash": "sha256-syn/F3jFSRgmndP5Y+QuGdo+BMjxFLRnmkYSumRXdDo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "LuxelRange.Core.wasm",
        "name": "LuxelRange.Core.cw2e173gtj.wasm",
        "hash": "sha256-LFNI54TWuRwTQ4ePHI7bYb3YFaXJ0U3+puQ5ZDRtdv0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Markdig.wasm",
        "name": "Markdig.ato3fn3juu.wasm",
        "hash": "sha256-XrsZGktyAakARIo+IPOpkBODiKrhtyzTXNj2m8vj390=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "MessagePipe.wasm",
        "name": "MessagePipe.cx96aumgn8.wasm",
        "hash": "sha256-wFkKjFgBYUgrT0K92qSWSf/dri8KAGZ8wsM9OE5xrEs=",
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
        "name": "Microsoft.AspNetCore.Components.WebAssembly.nzcb7tm6tr.wasm",
        "hash": "sha256-fQ52CtXrW8rXtwATLOMuLYoE+YCHUz7KZ8eEnMH2AOI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.AnalyzerUtilities.wasm",
        "name": "Microsoft.CodeAnalysis.AnalyzerUtilities.f8evldgxkg.wasm",
        "hash": "sha256-zyIWbIZkbJV6ViB6EkQZa+4z2LfSHfeLgRcZE1ofai0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.wasm",
        "name": "Microsoft.CodeAnalysis.CSharp.Features.l6zvbd5v4h.wasm",
        "hash": "sha256-I+1ngfyr9+yI+ysdW1HJ2Kz6s+fnmlpo0qQkhw2irao=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.CSharp.Scripting.wasm",
        "name": "Microsoft.CodeAnalysis.CSharp.Scripting.3mf86xnqvm.wasm",
        "hash": "sha256-9SQt2liy5/zcZX0u+t+KNOlw2J9KixSPUqds+idhMRc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.wasm",
        "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.864jgfqi25.wasm",
        "hash": "sha256-4GdV2IDuJc8/o/W1SF93f+8QKZ7fylc+PQFQ4M9Ti8M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.CSharp.wasm",
        "name": "Microsoft.CodeAnalysis.CSharp.d8576riaoy.wasm",
        "hash": "sha256-wze0y1CKV4jzD2JgnlEZAlHQ6GNIa/R4pBc/86drX1U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.Elfie.wasm",
        "name": "Microsoft.CodeAnalysis.Elfie.6dt0yknqsb.wasm",
        "hash": "sha256-VkmO4woyJqa7TDcQ9e+kuZEdNoHkFA3Nd/d9RXWVxa0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.Features.wasm",
        "name": "Microsoft.CodeAnalysis.Features.94y7m4evwx.wasm",
        "hash": "sha256-ea7UKnWmaQsS8sML30qfhP2i5UkjiAb+klq/XFhhq9c=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.Scripting.wasm",
        "name": "Microsoft.CodeAnalysis.Scripting.7vfat4fgfm.wasm",
        "hash": "sha256-LdQJEnfe9gmzhPSg95V7zUAPhSA8CeH80RGRlLEUkfI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.Workspaces.wasm",
        "name": "Microsoft.CodeAnalysis.Workspaces.f4kui5x3sw.wasm",
        "hash": "sha256-XxfwJxWjIqlEfSq/YI58lgZmtnBsynWlyVRGhquaS5M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CodeAnalysis.wasm",
        "name": "Microsoft.CodeAnalysis.ctwbnipil7.wasm",
        "hash": "sha256-4IkvZJ/QXHdm8q+RlffyWANCj2GtX3sqavsdfWEijb0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.DiaSymReader.wasm",
        "name": "Microsoft.DiaSymReader.n7aozp74g0.wasm",
        "hash": "sha256-s8A+LIZPdelFgqksm2voS6oVrorJuL2xM+/mgQHThnM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.Abstractions.wasm",
        "name": "Microsoft.Extensions.Configuration.Abstractions.36nlq3jm98.wasm",
        "hash": "sha256-daHFxgw8mSL4JUEsEdpp05S8SSUkuYBz9rwVKt5fCz4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.Binder.wasm",
        "name": "Microsoft.Extensions.Configuration.Binder.rzge1s69fj.wasm",
        "hash": "sha256-h34jnwaI9JtFwCVlQlLPeLbwkmT1RgvAXF8lZKMA8HQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.CommandLine.wasm",
        "name": "Microsoft.Extensions.Configuration.CommandLine.tt85oyhiz2.wasm",
        "hash": "sha256-D9S/E4WcEmKubQrKkWmoTOXK3gZhzgEWLXkKnv28Xyg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.EnvironmentVariables.wasm",
        "name": "Microsoft.Extensions.Configuration.EnvironmentVariables.3bk74btpzh.wasm",
        "hash": "sha256-cf1uu8piNtDzG+k1FBdf9DmFlxxsgpkF1FGe/Wa2Tzc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.FileExtensions.wasm",
        "name": "Microsoft.Extensions.Configuration.FileExtensions.76f519m7od.wasm",
        "hash": "sha256-8Z7n0g/23DOYpIZkPj0f8Hi3UxAVn5qll0v1xo1b7gU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.Json.wasm",
        "name": "Microsoft.Extensions.Configuration.Json.8a1hfxnrxt.wasm",
        "hash": "sha256-QBw01UchzqaEMjf+RnzUugyqH8B/D0t7n/NllnjkPnE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.UserSecrets.wasm",
        "name": "Microsoft.Extensions.Configuration.UserSecrets.86donye3pq.wasm",
        "hash": "sha256-UMiIP5NOb0HWn8lDSYr46aJcnxt+Tz1OJwisQtrBO8g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.wasm",
        "name": "Microsoft.Extensions.Configuration.28g3wmgx90.wasm",
        "hash": "sha256-4uwwhJzoHmobeI57EDEsFzDTBLC1WNuJhOOZ34DTGAI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.DependencyInjection.wasm",
        "name": "Microsoft.Extensions.DependencyInjection.vl0p8261bl.wasm",
        "hash": "sha256-U79rgQK10itX91QzfRbpP4FHnxFQayJ8V/JG6JgVSbA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.DependencyInjection.Abstractions.wasm",
        "name": "Microsoft.Extensions.DependencyInjection.Abstractions.4ud3on2i6h.wasm",
        "hash": "sha256-zNwGyrRvvbI9Q1Qh9biLAWEWopYfprS7pOBZHARSJms=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Diagnostics.Abstractions.wasm",
        "name": "Microsoft.Extensions.Diagnostics.Abstractions.rv6z37bvpn.wasm",
        "hash": "sha256-l9yEa7NeZsYyMy1qLy6xKO6OQVl9PzLO0AdlE3Do9fA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Diagnostics.wasm",
        "name": "Microsoft.Extensions.Diagnostics.jkukudx6e2.wasm",
        "hash": "sha256-w5m2XVDWXvZbhs/Q5/Rs35pQ33wYtLvqvcFGDpcu6C8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.FileProviders.Abstractions.wasm",
        "name": "Microsoft.Extensions.FileProviders.Abstractions.x6176kihxr.wasm",
        "hash": "sha256-Nwje5LAJD++6E4QQ2M6bZjDJj1Uv/JJLeCudoPSQYm8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.FileProviders.Physical.wasm",
        "name": "Microsoft.Extensions.FileProviders.Physical.i567b15h4s.wasm",
        "hash": "sha256-Gsb0mxWwHquWGAW+5Lqa9pJo9OhO0lQfl5oKVkWPgx4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.FileSystemGlobbing.wasm",
        "name": "Microsoft.Extensions.FileSystemGlobbing.7eil2p354w.wasm",
        "hash": "sha256-XwbVkRtMletFNqiC36VMGJwvPV0qYuRHTkN34bhgrbA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Hosting.Abstractions.wasm",
        "name": "Microsoft.Extensions.Hosting.Abstractions.l5wqwy9746.wasm",
        "hash": "sha256-SV34D56uZwI9WHNYZIS9LNLMI+lpKw1PLundpAamZiQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Hosting.wasm",
        "name": "Microsoft.Extensions.Hosting.tca6lws3ah.wasm",
        "hash": "sha256-E1EOOM0l/qGrRa93NhfkACKsT++CvvjtsaWg0Qjy4f4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Logging.Abstractions.wasm",
        "name": "Microsoft.Extensions.Logging.Abstractions.90yj58g27j.wasm",
        "hash": "sha256-Fwi8P1QhePaU+ZXcbq1LgGx26aSSSWKj/AqxLvusnbw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Logging.Configuration.wasm",
        "name": "Microsoft.Extensions.Logging.Configuration.qqyxldfx7x.wasm",
        "hash": "sha256-3hgwG7KQ7p6jTJ7nGnbHN0CFYu6E8+XvxAYANKwmLzw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Logging.Debug.wasm",
        "name": "Microsoft.Extensions.Logging.Debug.epnayjtw50.wasm",
        "hash": "sha256-gvRKZSDsl/goynLNnbcDxKXMNBgG5kh5ONtAo6CaSmk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Logging.EventLog.wasm",
        "name": "Microsoft.Extensions.Logging.EventLog.dl1kfs80za.wasm",
        "hash": "sha256-ESQ89IwD434/vYADStkDJliHbhjT6o3jXttXUIX5kjQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Logging.EventSource.wasm",
        "name": "Microsoft.Extensions.Logging.EventSource.7z8ebn69sa.wasm",
        "hash": "sha256-82u3Y+bBSutxMEj2tjfgDJXaZDLPM8UdTON1tLUjWQU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Logging.wasm",
        "name": "Microsoft.Extensions.Logging.pwzrqqif2n.wasm",
        "hash": "sha256-dy9KJDzsCuDZvttgAaQeGpUVXSy7SAGR+o3gp/egbSM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Options.ConfigurationExtensions.wasm",
        "name": "Microsoft.Extensions.Options.ConfigurationExtensions.praaq7wwnb.wasm",
        "hash": "sha256-BStZB2TauRdCRPaJ+wHc3NihsxsBehjOG/Y3o0EuE80=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Options.wasm",
        "name": "Microsoft.Extensions.Options.6l8rdcioqx.wasm",
        "hash": "sha256-OmHDAfhy3p95Jll3cNcGjYtR90mkLD5EUHLe0dUI8kc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Primitives.wasm",
        "name": "Microsoft.Extensions.Primitives.xh1xt99df6.wasm",
        "hash": "sha256-1VYmHWdIVWJQanA7xE6SLiNW4UoRR3DMavdwg5RdvR8=",
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
        "virtualPath": "Microsoft.Win32.Primitives.wasm",
        "name": "Microsoft.Win32.Primitives.q7qe8y8zau.wasm",
        "hash": "sha256-ynMBS+OYCxCmQSbz2XM23eZ+GNSoDJoeanA/Zrlw+P0=",
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
        "name": "SixLabors.ImageSharp.4gwmeb6dl9.wasm",
        "hash": "sha256-PDi39TQi8h3y7COwZ60VESiPp3r5OCVymnO8bbT8nQY=",
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
        "name": "System.Collections.Concurrent.26heom5vzj.wasm",
        "hash": "sha256-lnEAI/Q4h9KIo6SFSNCMyV7jKN/LxFfNHWlRONEISjE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Immutable.wasm",
        "name": "System.Collections.Immutable.1uvlewmfrv.wasm",
        "hash": "sha256-l/0cAzkPya+bxnh32qv/nVpL3g9PH4ws4Jf/uMQ2axE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.NonGeneric.wasm",
        "name": "System.Collections.NonGeneric.kcjd6108nt.wasm",
        "hash": "sha256-zPIEZ/6laEKQc/9rOuIusoFEBQIqogcBk4lT+ZNAkcI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Specialized.wasm",
        "name": "System.Collections.Specialized.5aninj2f8o.wasm",
        "hash": "sha256-XckwPmoRBFM/89LsRsXQ8yNjn7xA0aOEheUYbTKbPnU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.wasm",
        "name": "System.Collections.akgsjart91.wasm",
        "hash": "sha256-JnIppA2kyy6WtC608w/NpiIRfN9DpdNxYE+pa9XPwrk=",
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
        "name": "System.ComponentModel.Primitives.d147vj75x8.wasm",
        "hash": "sha256-z5QEJK3NBinIU/c7DsytUnkvdj+EsA4yp7gwalJGk8M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.TypeConverter.wasm",
        "name": "System.ComponentModel.TypeConverter.l5c0deqwzc.wasm",
        "hash": "sha256-iQJ1N1+sB7ZzF+zSZHrklaDJLtsNyBQEXAj/V1LXrJI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.wasm",
        "name": "System.ComponentModel.ltos09siez.wasm",
        "hash": "sha256-V497EYCC4FaMnZjNAVu0jRzvMaUJsQtnKyZ6NyKO23w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Composition.AttributedModel.wasm",
        "name": "System.Composition.AttributedModel.7wlzo97gc2.wasm",
        "hash": "sha256-l6XhhWoUeMZoTJp9Pds5+OySvD5Pe7uqOOjbfoEQLBg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Composition.Convention.wasm",
        "name": "System.Composition.Convention.rz18ai2loe.wasm",
        "hash": "sha256-F+4ut/DaW2Av6QU6SdGriyNzMj2p4JzzR2reZCMdptQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Composition.Hosting.wasm",
        "name": "System.Composition.Hosting.ryf802n5m2.wasm",
        "hash": "sha256-0/qasPu6Qo58V4uI3GtAfSeXqIEYm7qkOxsn7RqPx0k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Composition.Runtime.wasm",
        "name": "System.Composition.Runtime.95w5ugnd1o.wasm",
        "hash": "sha256-AwDMfJHvw82zEJSArg/LEaeS/NwYibPFbBNJov+W0SE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Composition.TypedParts.wasm",
        "name": "System.Composition.TypedParts.az7zj6fkp3.wasm",
        "hash": "sha256-Js6jMny7OmkVoorhI/erhg5kvJQNMXkRvsbp8kzVBmg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Configuration.ConfigurationManager.wasm",
        "name": "System.Configuration.ConfigurationManager.50p01uimie.wasm",
        "hash": "sha256-awi0DdRMBQr2w3YJtU9l+nQSH2MH5AsDPHbXPa4qWO8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Console.wasm",
        "name": "System.Console.5hup3cgr82.wasm",
        "hash": "sha256-ibnAZ7i2LkxMFXspKvE0yy2KT2aGyeAgceWkFOAaSxA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.Common.wasm",
        "name": "System.Data.Common.dk7rgp9hj4.wasm",
        "hash": "sha256-DUOUWwaiv6IoijzOOqRJkFcs2St08qXrm2xEQAnqxt4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.DataSetExtensions.wasm",
        "name": "System.Data.DataSetExtensions.x8ynu9z8mq.wasm",
        "hash": "sha256-3yCfpfIEHZufWHT4FDtpJe/0TqvFYwSoHyiqMRFGY0w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.DiagnosticSource.wasm",
        "name": "System.Diagnostics.DiagnosticSource.opsb9s8sbe.wasm",
        "hash": "sha256-5ICcQ6alqVTXZ4ydkfW75BmcD2Q/Mi/cMgWJ+rj6nPM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.EventLog.wasm",
        "name": "System.Diagnostics.EventLog.kv6g9vb3ds.wasm",
        "hash": "sha256-MIRGy+VMN9PBr8hBQffrc4SuodYHNwdgoEcnnvu/qZU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.FileVersionInfo.wasm",
        "name": "System.Diagnostics.FileVersionInfo.dkvpzsewqz.wasm",
        "hash": "sha256-88DxKSX+jQiL+Qy9J7796RS9sCza8Ze75RrEuZfquHg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Process.wasm",
        "name": "System.Diagnostics.Process.539c9vte7n.wasm",
        "hash": "sha256-AY74bX1ay3gXbcl24rGEW1Ur1mD5ecrwu4cEbGDl9l4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.StackTrace.wasm",
        "name": "System.Diagnostics.StackTrace.shtyp47e8d.wasm",
        "hash": "sha256-MP3+hobVeHvI0MkfKOlRBqAth72My33LaKVLsOLB5dI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.TextWriterTraceListener.wasm",
        "name": "System.Diagnostics.TextWriterTraceListener.yixvmxezel.wasm",
        "hash": "sha256-hqpOebcoZmTRvJ9QlftGUBhKfmQY27TiPOtZn8x7mdE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.TraceSource.wasm",
        "name": "System.Diagnostics.TraceSource.gp1drfa3m4.wasm",
        "hash": "sha256-5EDUpJs9VZU7/Ko0YWCX8Mgy/kiQSXfM99qvzApzmvE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Tracing.wasm",
        "name": "System.Diagnostics.Tracing.tfg02zyuud.wasm",
        "hash": "sha256-x14q84BI8w6PvuLQpdnbMePNdaQz/krvb36K5Byb0Is=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Globalization.wasm",
        "name": "System.Globalization.oj4vbem43v.wasm",
        "hash": "sha256-PJQn0hl8IKuY4jAjiYPco18Qnsh0+wfhrpQKcDpqLKQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.wasm",
        "name": "System.IO.Compression.ntipb8ycx9.wasm",
        "hash": "sha256-daluQdE1nYjnXtSfQlZcUo0/GhzZ0t8IRgaLKpFaj3E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.Watcher.wasm",
        "name": "System.IO.FileSystem.Watcher.mjzu7fhzgp.wasm",
        "hash": "sha256-jh3A/dLTU9zKeTLwZ1zospH0rnrjelXddYfZbW/st0g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.MemoryMappedFiles.wasm",
        "name": "System.IO.MemoryMappedFiles.qaagthczfc.wasm",
        "hash": "sha256-3sYBZM9bbZfV4j8LxkVw5+vGN4wohjwKFoycVeadFwU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipelines.wasm",
        "name": "System.IO.Pipelines.r45vhyjqzq.wasm",
        "hash": "sha256-RauAGKjS0juxovLr+aKhbRKVyc3D4siW2emV6TJXJ6Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.wasm",
        "name": "System.Linq.3w8dfb8898.wasm",
        "hash": "sha256-Q2lCBYqhDipTBMdg0gi53F6NjpgwECIRn69ZLj9ujKQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Expressions.wasm",
        "name": "System.Linq.Expressions.u2mp1k1daf.wasm",
        "hash": "sha256-Bv3OqfGV225jdqA6yfhmRYz343wiAY6LGtK0/wok2bk=",
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
        "name": "System.Net.Http.lzwnj1yetp.wasm",
        "hash": "sha256-P8LkItIrqbujTI1c3fR4eRXc8Pz7qCh58LDQjqkRHaM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.HttpListener.wasm",
        "name": "System.Net.HttpListener.dgkdha4xm7.wasm",
        "hash": "sha256-zUHc0QMq9agdh3duMvqeHpvDCKEBz5se+Jb9h9bXyqs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Primitives.wasm",
        "name": "System.Net.Primitives.g84k80qz5f.wasm",
        "hash": "sha256-kEWZ4Xzd1IT65lTp9lazlEeoWEOBORHwNREr50Hlqcc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Requests.wasm",
        "name": "System.Net.Requests.u7cormlhxh.wasm",
        "hash": "sha256-2vYSAporInpy0pXQCxU1yhyr6opYReSMD1hIQYLRisQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Sockets.wasm",
        "name": "System.Net.Sockets.nxg75tgjaj.wasm",
        "hash": "sha256-e8q/k7aMXPBl7qkFsKOTpaWDgjEqxHpvmNo5Uf6Udlo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebClient.wasm",
        "name": "System.Net.WebClient.880pjkvlf2.wasm",
        "hash": "sha256-aMDS+E2/EZYlUYV3gnziFbl1c0zOeBeTcMXaVQO06a0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebHeaderCollection.wasm",
        "name": "System.Net.WebHeaderCollection.ujgaai79sr.wasm",
        "hash": "sha256-SvQYh3ixmZo4LmnzZYRIl9lKc8e2ZCSlcvna5udEQ/s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebSockets.wasm",
        "name": "System.Net.WebSockets.07l0ybozcp.wasm",
        "hash": "sha256-tOYGbZ3/LnzrHQW5i+lkGC+BgGrFD9PhaJbVICO+8Z4=",
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
        "name": "System.ObjectModel.x0a07v63o0.wasm",
        "hash": "sha256-P2rVKFaDlxCh/kjGpNvgA+4ZSAffmi985LoAjrQZRo8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.DataContractSerialization.wasm",
        "name": "System.Private.DataContractSerialization.ph09jivlk2.wasm",
        "hash": "sha256-vRXnXh8OjGJc3H7u0nXDR+RHP0QjBYqYTF6nI0vOtCY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Uri.wasm",
        "name": "System.Private.Uri.0eb5hczn6a.wasm",
        "hash": "sha256-GObzi1ig/MrfIGjheAwS5OHqZaBTQ9V47H+Dr7UMQJI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.Linq.wasm",
        "name": "System.Private.Xml.Linq.4ld4y9qgig.wasm",
        "hash": "sha256-yONXIKweZdMD9lXjLMfpU6ONDHTyV2N93RV+F3/uCcE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.wasm",
        "name": "System.Private.Xml.x4mxkxh9da.wasm",
        "hash": "sha256-Ufac0PJ/ecEVsAQVRtsTXk4/BnbUvfTcxwzOUauoR5U=",
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
        "virtualPath": "System.Reflection.Metadata.wasm",
        "name": "System.Reflection.Metadata.14tp9oprhf.wasm",
        "hash": "sha256-X0dxmH5zGX65ww307mIRdqJ2aqa6vTHHnKrvBr8qXXo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Primitives.wasm",
        "name": "System.Reflection.Primitives.0b2ltrd72v.wasm",
        "hash": "sha256-7Bc7wDoNP19rMA66Q8eCZcyCs7XhENTeNELUtyHs7N8=",
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
        "name": "System.Runtime.InteropServices.sj7p5zpsy9.wasm",
        "hash": "sha256-iOyruSqrwDfh8bnbO6VLkBNXm+uKIl4BamP7Qfk2AtU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Intrinsics.wasm",
        "name": "System.Runtime.Intrinsics.vbkzxpikdf.wasm",
        "hash": "sha256-Vclp7cY180C8/t0xCj5LR2dihxie2Aqpn5dPgUMJIlk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Loader.wasm",
        "name": "System.Runtime.Loader.82zaadu9v2.wasm",
        "hash": "sha256-AI4tMarrFgAvE30kecTWtVSG8nFTmUJCN3gI/uGXt+A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Numerics.wasm",
        "name": "System.Runtime.Numerics.egvkhzg6ky.wasm",
        "hash": "sha256-woaWcp8hoxGvKBSVOuFnRd4XO6c2EFDUzkLORDm4ivA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Formatters.wasm",
        "name": "System.Runtime.Serialization.Formatters.z332nopivf.wasm",
        "hash": "sha256-P2OSWET4Cps9Oz3KTHHdTvuCqNu6EdjmIXoTW2IBgJ8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Json.wasm",
        "name": "System.Runtime.Serialization.Json.ggpjqmq90b.wasm",
        "hash": "sha256-YKY5Sk19siXMu2wyHKHeOWCiUypEP1myPOeRyYscHBc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Primitives.wasm",
        "name": "System.Runtime.Serialization.Primitives.8rmy3uqcri.wasm",
        "hash": "sha256-tGLaYOyESdhzxUz30ixn1EpatLRTChKXN7Q5PObnuaY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Xml.wasm",
        "name": "System.Runtime.Serialization.Xml.7bf4x7gtg9.wasm",
        "hash": "sha256-g6PdmjG+UlpV7t8KMC8r9WtGCGHwmu2pZATIaJ18Xws=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.wasm",
        "name": "System.Runtime.t304h6tnjy.wasm",
        "hash": "sha256-ifJN/YdVNGUqLmPYO6/vxrQNr0SalkSul56hj0N1dE8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.ProtectedData.wasm",
        "name": "System.Security.Cryptography.ProtectedData.2yj0iaqwy3.wasm",
        "hash": "sha256-BkHqtpEGgmL8T3LHQ3xBXuG30aVtho8kecNryUecI4k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.wasm",
        "name": "System.Security.Cryptography.6j36a89c3x.wasm",
        "hash": "sha256-nq7MiSaDnXvZ31gURaEBH9tlXnh5PXnb4hnGAbikNxE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Principal.Windows.wasm",
        "name": "System.Security.Principal.Windows.fqe229id7r.wasm",
        "hash": "sha256-6eIv7R0AMoic0GdxaJKW4Nz8EYQvLwSuEopSO2M+s7s=",
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
        "name": "System.Text.Encoding.Extensions.6oiu1hbauk.wasm",
        "hash": "sha256-+pC50tt9ahT7rG4A6mj4WbrP57GlWasJyADbPkNrcl0=",
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
        "name": "System.Text.Json.d5ktmplhnw.wasm",
        "hash": "sha256-QXW4FvAlQOQlEwIuKf2HhTUnQyr3jOx3ZQn09RaAbnQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.RegularExpressions.wasm",
        "name": "System.Text.RegularExpressions.xt05exltnm.wasm",
        "hash": "sha256-i+q9pLXWtPevqGTu3AlSewepit4SY+9D0qY8sSP9iRE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.wasm",
        "name": "System.Threading.a1utv3dvx8.wasm",
        "hash": "sha256-TjhuNco5UQELzQYhLWSf+A68vvVkECJEmTll6d7SlrY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Channels.wasm",
        "name": "System.Threading.Channels.nq840d3mbl.wasm",
        "hash": "sha256-PwkTmny/eJx44oPXfEsOdtCO/h5euaodXHbB3bXqdXI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Parallel.wasm",
        "name": "System.Threading.Tasks.Parallel.9xtbeba38y.wasm",
        "hash": "sha256-w1ma2x2JAFD0NwY+LdtI+x1/OdmcT6RGUs2Em+WU/Hc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Thread.wasm",
        "name": "System.Threading.Thread.k3q0zuopst.wasm",
        "hash": "sha256-IH/erVwv2Bp4A7I5v+pQZXQ6M+uv158Y8jrRIL+y0oE=",
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
        "virtualPath": "System.Xml.ReaderWriter.wasm",
        "name": "System.Xml.ReaderWriter.leiqoxia1m.wasm",
        "hash": "sha256-rbvv1O1ZFkMl66D+ZL/511JWBJz/9FrPYNHkQrbRDvc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XDocument.wasm",
        "name": "System.Xml.XDocument.zjptzkl6lx.wasm",
        "hash": "sha256-n8mFix5WLSVL8WWZKi+cUv28cBFTkVLBQy8dc4OwmCw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XPath.XDocument.wasm",
        "name": "System.Xml.XPath.XDocument.ylf7ird27m.wasm",
        "hash": "sha256-xjjR+stJ8ENwr4JOA4C3yEIWg8bZbVwVajMBQ/SM56I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XPath.wasm",
        "name": "System.Xml.XPath.i6h2xsume3.wasm",
        "hash": "sha256-hiuPXbRvHqnrIeMd5hJC4ZJLJhmUBc0Ifl6NQbXadlE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XmlSerializer.wasm",
        "name": "System.Xml.XmlSerializer.dvt0rs1uvp.wasm",
        "hash": "sha256-x+0TTBm92suyyhaR4/ovt+J9QTRKyTeIPjIGlAJQm1U=",
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
        "name": "netstandard.fhi075q63g.wasm",
        "hash": "sha256-beeG8RgHxtCmWfvxjnQMyM4HDlDCHAIPRfbg+a38xeE=",
        "cache": "force-cache"
      }
    ],
    "satelliteResources": {
      "cs": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.49ajrw9ce5.wasm",
          "hash": "sha256-WDpldckvwZfu/pqv8S849+fTQeKK95Xw8pNnrhFgW8o=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.1yuya6hz78.wasm",
          "hash": "sha256-KcMiEvprZeNjSNJx0k4nkR5Xeb0Hmx/c5FLi3d4ymFo=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.kbwa3xseae.wasm",
          "hash": "sha256-4PZMrT1dvhvrRSyYMBMwwfGylM5FMqo1yK2dDXg5ijw=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.d4x8iudjlw.wasm",
          "hash": "sha256-U4xVXjVIVC+VpdQxm7+xGT1zWB7IUbTMoQJkrSCGuZw=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Features.resources.ctqpmzklu3.wasm",
          "hash": "sha256-AOoy1O/5MfXBDWRIKa74H4xlpcxlEAFuv7g83+Wizvo=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.uoaxq93jzl.wasm",
          "hash": "sha256-oj1r9Y2yvVSsz7mCj8Hkr0PM/5LJfgfNLqHH2d237qo=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.03yz04hbv3.wasm",
          "hash": "sha256-TPcrYhnUeDxYTxN0hsXXvK5mTI9yVKZD1mGIyVJP5DQ=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.wasm",
          "name": "Microsoft.CodeAnalysis.resources.19bkj09r6q.wasm",
          "hash": "sha256-C0eEIIfRt8yOyE3Wi4V+4zQ9SSmbtqqrSUX6tPh+zBg=",
          "cache": "force-cache"
        }
      ],
      "de": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.9ioauyjqq9.wasm",
          "hash": "sha256-bw/Pwx9CCdyS/vQAmpHyjvpC/iw1Ye0BfbvvbU6ytyA=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.zioxovvs9b.wasm",
          "hash": "sha256-lSEwJ9OSsd/FFiKGUkLymXfwt59rn2nl90oid4orhNs=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.5yesv4arta.wasm",
          "hash": "sha256-U1aqkZ5oDvWzuSwA0EfuqT5wMaQcUqEobOg7G6f78bs=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.1b9hp9fj1v.wasm",
          "hash": "sha256-XXbKgIjjD3RJUq0h+ynE8B64Aqozrv7AzY7T6VnxBeM=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Features.resources.xpko7nvt71.wasm",
          "hash": "sha256-Gy77If8SkyXqjyfptgCIib5ekngBlQsAXk+gttBRDPo=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.se4ryidtyw.wasm",
          "hash": "sha256-7LZi+6PHxGPHNpODWPOHA4+RxUcziuW7V/w+asbuwv8=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.hxgx2y2lpy.wasm",
          "hash": "sha256-C8KwTCWvbS+DhFGrr4ASiokVnwuHbj6DY3Q8TO09AJU=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.wasm",
          "name": "Microsoft.CodeAnalysis.resources.6ahl1jjxbp.wasm",
          "hash": "sha256-QniJUG2oR8iF3MSN7B8SlmGC98e2k369jGic2SbHD3o=",
          "cache": "force-cache"
        }
      ],
      "es": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.w8eyzi5vfp.wasm",
          "hash": "sha256-oBch75+Eekk/b1NPhq/7r1bW8WZOkrV7G510gdIaDy4=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.b6owlcrdrc.wasm",
          "hash": "sha256-negTVQE+qQetIpSA/qFN5+q/kUZJ3MiOnFLXgBHPbcU=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.n924u6knr9.wasm",
          "hash": "sha256-e5q3Tar8jmluXDY+CnBJVUl30XziO0nze37RVNdqcfs=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.fmr9zzb8y1.wasm",
          "hash": "sha256-FxOKgBRrJPAnPJENlWwhLv6R88Bo8PLYVYSQyIiacdA=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Features.resources.7ljjw7ehx9.wasm",
          "hash": "sha256-FVq5bWxOdfWpsGtol4D4O/IZePdn+dS/yIZvGv26+Bo=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.ww1m2zm4mf.wasm",
          "hash": "sha256-MMHdj1a8v2VDNatVYVU0JAbVxGY5yKClMb2bbQUevZo=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.ymdo4orhk6.wasm",
          "hash": "sha256-9tCzRH4/qwvCEIOkq28aWsCXieHYUOFVaO6wsI8E59o=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.wasm",
          "name": "Microsoft.CodeAnalysis.resources.f832v39dvm.wasm",
          "hash": "sha256-IUgsilkQIdLXaLNgfvEsTupiuPMnlD7CxMo9gyAfvEA=",
          "cache": "force-cache"
        }
      ],
      "fr": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.p4m87j1px7.wasm",
          "hash": "sha256-9/q7Ssr/BPHWR/24qqGC1H1t053Om3qrfKESx8viz5c=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.bdbprvg3vi.wasm",
          "hash": "sha256-z5wi/G233n5+k67J4+auVDwXIjkEQjSZOs7DIN/2JtY=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.pj0w3ca5iy.wasm",
          "hash": "sha256-xT077pUTYkRL5nv914pHi3TmS/cyfKpxjnxAlESQ+7Y=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.9yn34brhwj.wasm",
          "hash": "sha256-/+PcDBnkW23f5yQSoazFE1FyAaM6RwFTu+UuiSRIQ2A=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Features.resources.2fhqowvc2k.wasm",
          "hash": "sha256-rmKOxIFGOB5kh3EAyGfExGfR2yrpK8OR5iQMTXmMlb4=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.1guw6pkqai.wasm",
          "hash": "sha256-IewMLAUklTkjrnivPqY8fa1hwrEP+glgloe2JYfd4KE=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.uxa4dbhs91.wasm",
          "hash": "sha256-YlmRIAxWqM04bNB9++rglYKJd6nVj9MOWKbg6hIeWSY=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.wasm",
          "name": "Microsoft.CodeAnalysis.resources.41x6hinrd5.wasm",
          "hash": "sha256-SKqb8viXNn6ibir/gvTcuIn/qi56t1mSy4MQecAongw=",
          "cache": "force-cache"
        }
      ],
      "it": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.y9eta31xwg.wasm",
          "hash": "sha256-eowlbZaAUPKZshuPBw0PDeIjPpAxRou2xgNbl7FlnK8=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.n0jcfngmhg.wasm",
          "hash": "sha256-R5Z5njXRwmtwZG7l5PgY9uBNnKnh2cU0fTWPGbE9ay8=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.iqk0bqbb1p.wasm",
          "hash": "sha256-+mtmcueOtQId6W1VsS7xnG6i5J6Pow2DjAwQraxexPU=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.43ca6rn0wu.wasm",
          "hash": "sha256-UCJ9Xyd+g2S2ZVvoDJbCklk5h2RbX2UHH8gtUt+ECWA=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Features.resources.o3tixidyys.wasm",
          "hash": "sha256-1PyUu06ClA0+BhvKetsvtmrkip4z11MpdCe9n9RUD70=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.xunytho65y.wasm",
          "hash": "sha256-tzHPWelOtUHpqirTCmYHlqCp3hY1C075xh2opVtnmLk=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.o9v4xb7q8n.wasm",
          "hash": "sha256-TDSWfZ04mdF4MAY1RvMJEosYzokhcnvColBIv71N/UQ=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.wasm",
          "name": "Microsoft.CodeAnalysis.resources.xnj5mdcldu.wasm",
          "hash": "sha256-zRw37ixqvfpVIZQz0MzcLdCkr3dvnT5KzPx5XpCT1mQ=",
          "cache": "force-cache"
        }
      ],
      "ja": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.xwem2ikkkw.wasm",
          "hash": "sha256-/wA/r6eSBK22dFfZowHWYluyXYmubW1ZvisN2K3WDQE=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.6p6w54lle3.wasm",
          "hash": "sha256-6i7z/xZ+KUd1nAVvjVs2C2UiPIE4iaMINc1lokfdstY=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.eek7m0udnc.wasm",
          "hash": "sha256-Blm9fEkinY97sxwo5ilySyBf7YK8u+vBeYtvewCXjFc=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.99bl2qxczg.wasm",
          "hash": "sha256-PSZm8u5prL4WecNzgOT0Kv4tgYIDkhLeM2wt0+NnDio=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Features.resources.6d1lift9gv.wasm",
          "hash": "sha256-Kvhs6muarFIyI/LRcUk6PurPJ3NyTYwvYWxH99r8hIc=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.5imm0zwpxc.wasm",
          "hash": "sha256-bcer6oUZzGFpSOy7wTNL91u5dN77PNNT3Hn+uMrPEFM=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.j34ushg4ea.wasm",
          "hash": "sha256-P/D3aM3htrolcHFjCbr3tqwJQyeslmvitR43r8z0HlM=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.wasm",
          "name": "Microsoft.CodeAnalysis.resources.jv2u6mk4av.wasm",
          "hash": "sha256-8Xeaj4bZsKWykGF3fGgJKnzpj2azxmC86EoXmPhAtpk=",
          "cache": "force-cache"
        }
      ],
      "ko": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.yj3yy1yhr8.wasm",
          "hash": "sha256-fqxnMQltEsQFroteoESvqqpUFN1dFVfmI78hbC6ium4=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.fq4h84qu8p.wasm",
          "hash": "sha256-yRVNwrI3gXXBt+nMii4NlzmbmeIxskcoH3/tpf2qGOw=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.nujuwbsi1f.wasm",
          "hash": "sha256-/ytizr/Q52A/cAYj2rrBS1qNOiTVBp6w1uCymXEHACw=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.nvbp3t5mbi.wasm",
          "hash": "sha256-nbCEdAJFuKbRwT8Y1Bjs+5PPsuiJxEZXyYwMvC3If8A=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Features.resources.m95evlemps.wasm",
          "hash": "sha256-1pqmBdX5nD/luPoKHUtTex4yoUDpOe/HuKsE19GXm/0=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.emr8ok6lvr.wasm",
          "hash": "sha256-KjpdjIyEBNelIcCIOM+upC8SUNNeNOXKmNMN/md1uXo=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.66vhsq3mp2.wasm",
          "hash": "sha256-8lO57H1J6HKs8j1nGsbMQmFMJ0BOs51uA+Ssf4ti1l8=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.wasm",
          "name": "Microsoft.CodeAnalysis.resources.4c7af2g7ow.wasm",
          "hash": "sha256-pLopEDdKBaAajj7CYt/dyEgwy4oypsm5gNCciX74/EQ=",
          "cache": "force-cache"
        }
      ],
      "pl": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.fjsi7ho7nu.wasm",
          "hash": "sha256-++vx316tuCcA8Jq4UBFkcoEV6kxC5bFhZRaWhlRchW4=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.vlf3t5lbes.wasm",
          "hash": "sha256-w/JlAOowX9xjOGCdWoot3tn8gFmR3KnvviiAm5S4pbQ=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.cmoh65zpyh.wasm",
          "hash": "sha256-HP86HBQsp5TwBDuXFDkBnjngt20rZ4YQu2hbGilo2PM=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.uraylo3wjb.wasm",
          "hash": "sha256-9s9pVd1kVe3IyWpiOKcRsdBZJcGuPGIM65ksWD2POYk=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Features.resources.5ie29fmzu6.wasm",
          "hash": "sha256-E+VzDh9XO23F+uULWJuQsS93aWDfiXtoNgAm3GgNs44=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.zg1xf0vt89.wasm",
          "hash": "sha256-syT0BYYlkClDrTfqw9WaWqZtK5TjL5xMWe4tXToYCic=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.uyrh6mg6ek.wasm",
          "hash": "sha256-KvqKl0iUPeq9QCbXSeYsXuqu7PoL68rub4sed0KRdcw=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.wasm",
          "name": "Microsoft.CodeAnalysis.resources.g141hvnysy.wasm",
          "hash": "sha256-tEchz+zj22dI+ryCf7h94SFXb2o0vDnKbomMNRdhwTs=",
          "cache": "force-cache"
        }
      ],
      "ps": [
        {
          "virtualPath": "Luxel.Resources.Gallery.resources.wasm",
          "name": "Luxel.Resources.Gallery.resources.1relh181qx.wasm",
          "hash": "sha256-E60dW1Mx5dbHGDM7PoFX6fdLwHQDqsRxmKNrkmoPSe0=",
          "cache": "force-cache"
        }
      ],
      "pt-BR": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.jpnc22zbxn.wasm",
          "hash": "sha256-B3064bi/8qtz03NAKFNzfKwtTgDMGLKFtbtCXiKyq3U=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.jl4ut7xthc.wasm",
          "hash": "sha256-x3uW8zoeAN1mBfCW/SAYZAoLGUsaa7/g/6Y7Huf9bgs=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.d0zm4obiz5.wasm",
          "hash": "sha256-Q5EsoepaDRL7WK8nt5Qb4zlqbz14TtQE595QUsundQU=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.jy0ik169qz.wasm",
          "hash": "sha256-W60Y9Ck4MyR6tc3jvj/0Pu5hwMSgFwkIrG4617pRDzA=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Features.resources.yh3u36xv3m.wasm",
          "hash": "sha256-ygdIm9FVPHKPkuimX9wrl+XavaKnY89+CHEtYqUp5DU=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.zjthba3lcr.wasm",
          "hash": "sha256-33LhJVwud1QN3JcKav+KbgXnucyZiAzHwdWZZcTgr+4=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.a055xugjbz.wasm",
          "hash": "sha256-Zp7Ix7iapr7JT1Zej19QHHFryLrcxJQmbihzCp2cjho=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.wasm",
          "name": "Microsoft.CodeAnalysis.resources.0fubno3h54.wasm",
          "hash": "sha256-REqGUd0T7fXpfWAAcDXIQsCVil7whA94FhYq8dteunE=",
          "cache": "force-cache"
        }
      ],
      "ru": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.0nal6o10he.wasm",
          "hash": "sha256-5KA//cP5XUWYqfKtojMFRAe8ypiREpBWfy0wlLMma00=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.ojsivhoxrx.wasm",
          "hash": "sha256-BISL2WV2nxw43wvWS70h8s1yv9m1zVzmT3dkpC/gaY0=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.30moo1f9wt.wasm",
          "hash": "sha256-nY4mhwRk70Os1Or1q6QsFNdqboxz0J4FhjQnmIbgrSU=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.bk4g3kgdev.wasm",
          "hash": "sha256-G26Baz5OEdsq+REekZuTR6iD8WoWnjaUA4RsXnXS9IY=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Features.resources.fuvmqk6chy.wasm",
          "hash": "sha256-SStSowhBgvLyVkZjsCYW6wrlGnOo34mA3ZqILswtl6U=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.i8zlm1kp97.wasm",
          "hash": "sha256-op+UDOFnhCoiPoSC60g+8+8BWjbRC057M6cgQEw7AHI=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.chuvj3vgq3.wasm",
          "hash": "sha256-EHvquBRxL/RR9Xt8J93YgEWDtuzKtKbsfUxl1IkhLi0=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.wasm",
          "name": "Microsoft.CodeAnalysis.resources.cgmxhiplvo.wasm",
          "hash": "sha256-7Ph9j4PszT1MW4JH/9W8Hl3cv146Uxk9gaclnu7xq1s=",
          "cache": "force-cache"
        }
      ],
      "tr": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.5n5qsxvble.wasm",
          "hash": "sha256-7/BTAFuXPJWQTHBV+aWVkZTYJYx0ah/pFilyzHfxO+E=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.1dkdj9t2nb.wasm",
          "hash": "sha256-q1Bri5+IQ/SgUv9GBaTxEfTfYx1hgeqhmPadaHYDLzs=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.mij4mtuqmt.wasm",
          "hash": "sha256-zgWcXMUmJ4okIqqGqJzd9ktrRhCi2PMBlELmWqC/Hqc=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.emh0h8cvvk.wasm",
          "hash": "sha256-ylXJ6ol1VYrArVX9hSqYREmlystMQ4sHUn1Tz17yGko=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Features.resources.h23t072kxh.wasm",
          "hash": "sha256-N6/AelLVGgubSgwzrcX0CxCOFeVDbFINiNlEUTTM3gc=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.64j9v8zy4r.wasm",
          "hash": "sha256-Bhia0LfdMP0Hg73Q0OzR1WF56z66yugDCOyjxgZmvIE=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.x12tmjnk15.wasm",
          "hash": "sha256-W95gct4AFc6+Vy6BBM6hkH+2NsYVszLHlTBUwcFH4qA=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.wasm",
          "name": "Microsoft.CodeAnalysis.resources.imti7xsm65.wasm",
          "hash": "sha256-hs72nw5T2lOIyikOAkY3tmkG/2u3LCFTyAPKsxAlFz4=",
          "cache": "force-cache"
        }
      ],
      "zh-Hans": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.gf1o3jpvqr.wasm",
          "hash": "sha256-PHyAuAZCat0AS61ZnIlwexoHTP2U17/N+cL9JwoxAJY=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.8i9jc1edb2.wasm",
          "hash": "sha256-4AarP9lXbI5DFw7o9sViIna55OYCCeCG0D9dhSLL8Ro=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.ba2zb2uo5b.wasm",
          "hash": "sha256-rfYsaY5GVyauPjlH68P/6uJipf26PD2LYU6LGGL0hXk=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.art3swb9qd.wasm",
          "hash": "sha256-ZsXoxHkngWIAbVzjsHNKtniuHxiVrQEU5BYshYvFc9E=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Features.resources.pu7pqg2zrk.wasm",
          "hash": "sha256-AW4y3VnZ3/IzqeZXnchYFHlIUlzJMwkBAVAqC5Z9Omk=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.e28fug4c75.wasm",
          "hash": "sha256-astnLJbVbwazeEFuDomRKWwlH460PiYevnyytKXrteU=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.v3yfcqd5eu.wasm",
          "hash": "sha256-a56EAEa8o0RIScR74ig12/OvvBy4OzP91TPKrMcOMYs=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.wasm",
          "name": "Microsoft.CodeAnalysis.resources.7kqclsvbg0.wasm",
          "hash": "sha256-d+ZMlNcEFEgONnCLpflwG8Fg/lIbkK8kESxYJz4s5XY=",
          "cache": "force-cache"
        }
      ],
      "zh-Hant": [
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Features.resources.zbs69j7qb6.wasm",
          "hash": "sha256-EWZb8sOGt8rJVyR6gWEEHuCDR7gQBdEsaOZUqdv0Klc=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Scripting.resources.umkvojyaqx.wasm",
          "hash": "sha256-Nl50gwuzSgdLdrhiqfq5KrjqVw7gHiH2+dBum0Vb+ck=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.Workspaces.resources.fg7kutfdif.wasm",
          "hash": "sha256-vwStw5c3d2oso2Y9fHnii8viZvUagyOAYydj1jlKfIY=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.CSharp.resources.wasm",
          "name": "Microsoft.CodeAnalysis.CSharp.resources.a6ai2ctyg1.wasm",
          "hash": "sha256-AkY9mS+xJs5+q4qKF/vFU6iG7Y/j8kZax5SdiS2Ss+s=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Features.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Features.resources.neuk5wcgoi.wasm",
          "hash": "sha256-EcQjTjfJrlOxlA1BHn7yG7DEQZxQ10LG/YlthGcBuD0=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Scripting.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Scripting.resources.mm86e55bwv.wasm",
          "hash": "sha256-0nx7oWmXh9iJbuW28LUfHaVbea9rNsdDYGF5SCFeS9Q=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.Workspaces.resources.wasm",
          "name": "Microsoft.CodeAnalysis.Workspaces.resources.v6nuw6z9ra.wasm",
          "hash": "sha256-GcdTNunLA2+jVOwy6C/BHCOdEdfxuAaAmif7ESgGAgQ=",
          "cache": "force-cache"
        },
        {
          "virtualPath": "Microsoft.CodeAnalysis.resources.wasm",
          "name": "Microsoft.CodeAnalysis.resources.mz36p92aqq.wasm",
          "hash": "sha256-snZDwxrhIwQs5vUA1zV1M6tNX0CtdM59sF9HB0LrUtw=",
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
