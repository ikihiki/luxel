//! Licensed to the .NET Foundation under one or more agreements.
//! The .NET Foundation licenses this file to you under the MIT license.

var e=!1;const t=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,4,1,96,0,0,3,2,1,0,10,8,1,6,0,6,64,25,11,11])),o=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,15,1,13,0,65,1,253,15,65,2,253,15,253,128,2,11])),n=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,10,1,8,0,65,0,253,15,253,98,11])),r=Symbol.for("wasm promise_control");function i(e,t){let o=null;const n=new Promise((function(n,r){o={isDone:!1,promise:null,resolve:t=>{o.isDone||(o.isDone=!0,n(t),e&&e())},reject:e=>{o.isDone||(o.isDone=!0,r(e),t&&t())}}}));o.promise=n;const i=n;return i[r]=o,{promise:i,promise_control:o}}function s(e){return e[r]}function a(e){e&&function(e){return void 0!==e[r]}(e)||Be(!1,"Promise is not controllable")}const l="__mono_message__",c=["debug","log","trace","warn","info","error"],d="MONO_WASM: ";let u,f,m,g,p,h;function w(e){g=e}function b(e){if(Pe.diagnosticTracing){const t="function"==typeof e?e():e;console.debug(d+t)}}function y(e,...t){console.info(d+e,...t)}function v(e,...t){console.info(e,...t)}function E(e,...t){console.warn(d+e,...t)}function _(e,...t){if(t&&t.length>0&&t[0]&&"object"==typeof t[0]){if(t[0].silent)return;if(t[0].toString)return void console.error(d+e,t[0].toString())}console.error(d+e,...t)}function x(e,t,o){return function(...n){try{let r=n[0];if(void 0===r)r="undefined";else if(null===r)r="null";else if("function"==typeof r)r=r.toString();else if("string"!=typeof r)try{r=JSON.stringify(r)}catch(e){r=r.toString()}t(o?JSON.stringify({method:e,payload:r,arguments:n.slice(1)}):[e+r,...n.slice(1)])}catch(e){m.error(`proxyConsole failed: ${e}`)}}}function j(e,t,o){f=t,g=e,m={...t};const n=`${o}/console`.replace("https://","wss://").replace("http://","ws://");u=new WebSocket(n),u.addEventListener("error",A),u.addEventListener("close",S),function(){for(const e of c)f[e]=x(`console.${e}`,T,!0)}()}function R(e){let t=30;const o=()=>{u?0==u.bufferedAmount||0==t?(e&&v(e),function(){for(const e of c)f[e]=x(`console.${e}`,m.log,!1)}(),u.removeEventListener("error",A),u.removeEventListener("close",S),u.close(1e3,e),u=void 0):(t--,globalThis.setTimeout(o,100)):e&&m&&m.log(e)};o()}function T(e){u&&u.readyState===WebSocket.OPEN?u.send(e):m.log(e)}function A(e){m.error(`[${g}] proxy console websocket error: ${e}`,e)}function S(e){m.debug(`[${g}] proxy console websocket closed: ${e}`,e)}function D(){Pe.preferredIcuAsset=O(Pe.config);let e="invariant"==Pe.config.globalizationMode;if(!e)if(Pe.preferredIcuAsset)Pe.diagnosticTracing&&b("ICU data archive(s) available, disabling invariant mode");else{if("custom"===Pe.config.globalizationMode||"all"===Pe.config.globalizationMode||"sharded"===Pe.config.globalizationMode){const e="invariant globalization mode is inactive and no ICU data archives are available";throw _(`ERROR: ${e}`),new Error(e)}Pe.diagnosticTracing&&b("ICU data archive(s) not available, using invariant globalization mode"),e=!0,Pe.preferredIcuAsset=null}const t="DOTNET_SYSTEM_GLOBALIZATION_INVARIANT",o=Pe.config.environmentVariables;if(void 0===o[t]&&e&&(o[t]="1"),void 0===o.TZ)try{const e=Intl.DateTimeFormat().resolvedOptions().timeZone||null;e&&(o.TZ=e)}catch(e){y("failed to detect timezone, will fallback to UTC")}}function O(e){var t;if((null===(t=e.resources)||void 0===t?void 0:t.icu)&&"invariant"!=e.globalizationMode){const t=e.applicationCulture||(ke?globalThis.navigator&&globalThis.navigator.languages&&globalThis.navigator.languages[0]:Intl.DateTimeFormat().resolvedOptions().locale),o=e.resources.icu;let n=null;if("custom"===e.globalizationMode){if(o.length>=1)return o[0].name}else t&&"all"!==e.globalizationMode?"sharded"===e.globalizationMode&&(n=function(e){const t=e.split("-")[0];return"en"===t||["fr","fr-FR","it","it-IT","de","de-DE","es","es-ES"].includes(e)?"icudt_EFIGS.dat":["zh","ko","ja"].includes(t)?"icudt_CJK.dat":"icudt_no_CJK.dat"}(t)):n="icudt.dat";if(n)for(let e=0;e<o.length;e++){const t=o[e];if(t.virtualPath===n)return t.name}}return e.globalizationMode="invariant",null}(new Date).valueOf();const C=class{constructor(e){this.url=e}toString(){return this.url}};async function k(e,t){try{const o="function"==typeof globalThis.fetch;if(Se){const n=e.startsWith("file://");if(!n&&o)return globalThis.fetch(e,t||{credentials:"same-origin"});p||(h=Ne.require("url"),p=Ne.require("fs")),n&&(e=h.fileURLToPath(e));const r=await p.promises.readFile(e);return{ok:!0,headers:{length:0,get:()=>null},url:e,arrayBuffer:()=>r,json:()=>JSON.parse(r),text:()=>{throw new Error("NotImplementedException")}}}if(o)return globalThis.fetch(e,t||{credentials:"same-origin"});if("function"==typeof read)return{ok:!0,url:e,headers:{length:0,get:()=>null},arrayBuffer:()=>new Uint8Array(read(e,"binary")),json:()=>JSON.parse(read(e,"utf8")),text:()=>read(e,"utf8")}}catch(t){return{ok:!1,url:e,status:500,headers:{length:0,get:()=>null},statusText:"ERR28: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t},text:()=>{throw t}}}throw new Error("No fetch implementation available")}function I(e){return"string"!=typeof e&&Be(!1,"url must be a string"),!M(e)&&0!==e.indexOf("./")&&0!==e.indexOf("../")&&globalThis.URL&&globalThis.document&&globalThis.document.baseURI&&(e=new URL(e,globalThis.document.baseURI).toString()),e}const U=/^[a-zA-Z][a-zA-Z\d+\-.]*?:\/\//,P=/[a-zA-Z]:[\\/]/;function M(e){return Se||Ie?e.startsWith("/")||e.startsWith("\\")||-1!==e.indexOf("///")||P.test(e):U.test(e)}let L,N=0;const $=[],z=[],W=new Map,F={"js-module-threads":!0,"js-module-runtime":!0,"js-module-dotnet":!0,"js-module-native":!0,"js-module-diagnostics":!0},B={...F,"js-module-library-initializer":!0},V={...F,dotnetwasm:!0,heap:!0,manifest:!0},q={...B,manifest:!0},H={...B,dotnetwasm:!0},J={dotnetwasm:!0,symbols:!0},Z={...B,dotnetwasm:!0,symbols:!0},Q={symbols:!0};function G(e){return!("icu"==e.behavior&&e.name!=Pe.preferredIcuAsset)}function K(e,t,o){null!=t||(t=[]),Be(1==t.length,`Expect to have one ${o} asset in resources`);const n=t[0];return n.behavior=o,X(n),e.push(n),n}function X(e){V[e.behavior]&&W.set(e.behavior,e)}function Y(e){Be(V[e],`Unknown single asset behavior ${e}`);const t=W.get(e);if(t&&!t.resolvedUrl)if(t.resolvedUrl=Pe.locateFile(t.name),F[t.behavior]){const e=ge(t);e?("string"!=typeof e&&Be(!1,"loadBootResource response for 'dotnetjs' type should be a URL string"),t.resolvedUrl=e):t.resolvedUrl=ce(t.resolvedUrl,t.behavior)}else if("dotnetwasm"!==t.behavior)throw new Error(`Unknown single asset behavior ${e}`);return t}function ee(e){const t=Y(e);return Be(t,`Single asset for ${e} not found`),t}let te=!1;async function oe(){if(!te){te=!0,Pe.diagnosticTracing&&b("mono_download_assets");try{const e=[],t=[],o=(e,t)=>{!Z[e.behavior]&&G(e)&&Pe.expected_instantiated_assets_count++,!H[e.behavior]&&G(e)&&(Pe.expected_downloaded_assets_count++,t.push(se(e)))};for(const t of $)o(t,e);for(const e of z)o(e,t);Pe.allDownloadsQueued.promise_control.resolve(),Promise.all([...e,...t]).then((()=>{Pe.allDownloadsFinished.promise_control.resolve()})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),await Pe.runtimeModuleLoaded.promise;const n=async e=>{const t=await e;if(t.buffer){if(!Z[t.behavior]){t.buffer&&"object"==typeof t.buffer||Be(!1,"asset buffer must be array-like or buffer-like or promise of these"),"string"!=typeof t.resolvedUrl&&Be(!1,"resolvedUrl must be string");const e=t.resolvedUrl,o=await t.buffer,n=new Uint8Array(o);pe(t),await Ue.beforeOnRuntimeInitialized.promise,Ue.instantiate_asset(t,e,n)}}else J[t.behavior]?("symbols"===t.behavior&&(await Ue.instantiate_symbols_asset(t),pe(t)),J[t.behavior]&&++Pe.actual_downloaded_assets_count):(t.isOptional||Be(!1,"Expected asset to have the downloaded buffer"),!H[t.behavior]&&G(t)&&Pe.expected_downloaded_assets_count--,!Z[t.behavior]&&G(t)&&Pe.expected_instantiated_assets_count--)},r=[],i=[];for(const t of e)r.push(n(t));for(const e of t)i.push(n(e));Promise.all(r).then((()=>{Ce||Ue.coreAssetsInMemory.promise_control.resolve()})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),Promise.all(i).then((async()=>{Ce||(await Ue.coreAssetsInMemory.promise,Ue.allAssetsInMemory.promise_control.resolve())})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e}))}catch(e){throw Pe.err("Error in mono_download_assets: "+e),e}}}let ne=!1;function re(){if(ne)return;ne=!0;const e=Pe.config,t=[];if(e.assets)for(const t of e.assets)"object"!=typeof t&&Be(!1,`asset must be object, it was ${typeof t} : ${t}`),"string"!=typeof t.behavior&&Be(!1,"asset behavior must be known string"),"string"!=typeof t.name&&Be(!1,"asset name must be string"),t.resolvedUrl&&"string"!=typeof t.resolvedUrl&&Be(!1,"asset resolvedUrl could be string"),t.hash&&"string"!=typeof t.hash&&Be(!1,"asset resolvedUrl could be string"),t.pendingDownload&&"object"!=typeof t.pendingDownload&&Be(!1,"asset pendingDownload could be object"),t.isCore?$.push(t):z.push(t),X(t);else if(e.resources){const o=e.resources;o.wasmNative||Be(!1,"resources.wasmNative must be defined"),o.jsModuleNative||Be(!1,"resources.jsModuleNative must be defined"),o.jsModuleRuntime||Be(!1,"resources.jsModuleRuntime must be defined"),K(z,o.wasmNative,"dotnetwasm"),K(t,o.jsModuleNative,"js-module-native"),K(t,o.jsModuleRuntime,"js-module-runtime"),o.jsModuleDiagnostics&&K(t,o.jsModuleDiagnostics,"js-module-diagnostics");const n=(e,t,o)=>{const n=e;n.behavior=t,o?(n.isCore=!0,$.push(n)):z.push(n)};if(o.coreAssembly)for(let e=0;e<o.coreAssembly.length;e++)n(o.coreAssembly[e],"assembly",!0);if(o.assembly)for(let e=0;e<o.assembly.length;e++)n(o.assembly[e],"assembly",!o.coreAssembly);if(0!=e.debugLevel&&Pe.isDebuggingSupported()){if(o.corePdb)for(let e=0;e<o.corePdb.length;e++)n(o.corePdb[e],"pdb",!0);if(o.pdb)for(let e=0;e<o.pdb.length;e++)n(o.pdb[e],"pdb",!o.corePdb)}if(e.loadAllSatelliteResources&&o.satelliteResources)for(const e in o.satelliteResources)for(let t=0;t<o.satelliteResources[e].length;t++){const r=o.satelliteResources[e][t];r.culture=e,n(r,"resource",!o.coreAssembly)}if(o.coreVfs)for(let e=0;e<o.coreVfs.length;e++)n(o.coreVfs[e],"vfs",!0);if(o.vfs)for(let e=0;e<o.vfs.length;e++)n(o.vfs[e],"vfs",!o.coreVfs);const r=O(e);if(r&&o.icu)for(let e=0;e<o.icu.length;e++){const t=o.icu[e];t.name===r&&n(t,"icu",!1)}if(o.wasmSymbols)for(let e=0;e<o.wasmSymbols.length;e++)n(o.wasmSymbols[e],"symbols",!1)}if(e.appsettings)for(let t=0;t<e.appsettings.length;t++){const o=e.appsettings[t],n=he(o);"appsettings.json"!==n&&n!==`appsettings.${e.applicationEnvironment}.json`||z.push({name:o,behavior:"vfs",cache:"no-cache",useCredentials:!0})}e.assets=[...$,...z,...t]}async function ie(e){const t=await se(e);return await t.pendingDownloadInternal.response,t.buffer}async function se(e){try{return await ae(e)}catch(t){if(!Pe.enableDownloadRetry)throw t;if(Ie||Se)throw t;if(e.pendingDownload&&e.pendingDownloadInternal==e.pendingDownload)throw t;if(e.resolvedUrl&&-1!=e.resolvedUrl.indexOf("file://"))throw t;if(t&&404==t.status)throw t;e.pendingDownloadInternal=void 0,await Pe.allDownloadsQueued.promise;try{return Pe.diagnosticTracing&&b(`Retrying download '${e.name}'`),await ae(e)}catch(t){return e.pendingDownloadInternal=void 0,await new Promise((e=>globalThis.setTimeout(e,100))),Pe.diagnosticTracing&&b(`Retrying download (2) '${e.name}' after delay`),await ae(e)}}}async function ae(e){for(;L;)await L.promise;try{++N,N==Pe.maxParallelDownloads&&(Pe.diagnosticTracing&&b("Throttling further parallel downloads"),L=i());const t=await async function(e){if(e.pendingDownload&&(e.pendingDownloadInternal=e.pendingDownload),e.pendingDownloadInternal&&e.pendingDownloadInternal.response)return e.pendingDownloadInternal.response;if(e.buffer){const t=await e.buffer;return e.resolvedUrl||(e.resolvedUrl="undefined://"+e.name),e.pendingDownloadInternal={url:e.resolvedUrl,name:e.name,response:Promise.resolve({ok:!0,arrayBuffer:()=>t,json:()=>JSON.parse(new TextDecoder("utf-8").decode(t)),text:()=>{throw new Error("NotImplementedException")},headers:{get:()=>{}}})},e.pendingDownloadInternal.response}const t=e.loadRemote&&Pe.config.remoteSources?Pe.config.remoteSources:[""];let o;for(let n of t){n=n.trim(),"./"===n&&(n="");const t=le(e,n);e.name===t?Pe.diagnosticTracing&&b(`Attempting to download '${t}'`):Pe.diagnosticTracing&&b(`Attempting to download '${t}' for ${e.name}`);try{e.resolvedUrl=t;const n=fe(e);if(e.pendingDownloadInternal=n,o=await n.response,!o||!o.ok)continue;return o}catch(e){o||(o={ok:!1,url:t,status:0,statusText:""+e});continue}}const n=e.isOptional||e.name.match(/\.pdb$/)&&Pe.config.ignorePdbLoadErrors;if(o||Be(!1,`Response undefined ${e.name}`),!n){const t=new Error(`download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`);throw t.status=o.status,t}y(`optional download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`)}(e);return t?(J[e.behavior]||(e.buffer=await t.arrayBuffer(),++Pe.actual_downloaded_assets_count),e):e}finally{if(--N,L&&N==Pe.maxParallelDownloads-1){Pe.diagnosticTracing&&b("Resuming more parallel downloads");const e=L;L=void 0,e.promise_control.resolve()}}}function le(e,t){let o;return null==t&&Be(!1,`sourcePrefix must be provided for ${e.name}`),e.resolvedUrl?o=e.resolvedUrl:(o=""===t?"assembly"===e.behavior||"pdb"===e.behavior?e.name:"resource"===e.behavior&&e.culture&&""!==e.culture?`${e.culture}/${e.name}`:e.name:t+e.name,o=ce(Pe.locateFile(o),e.behavior)),o&&"string"==typeof o||Be(!1,"attemptUrl need to be path or url string"),o}function ce(e,t){return Pe.modulesUniqueQuery&&q[t]&&(e+=Pe.modulesUniqueQuery),e}let de=0;const ue=new Set;function fe(e){try{e.resolvedUrl||Be(!1,"Request's resolvedUrl must be set");const t=function(e){let t=e.resolvedUrl;if(Pe.loadBootResource){const o=ge(e);if(o instanceof Promise)return o;"string"==typeof o&&(t=o)}const o={};return e.cache?o.cache=e.cache:Pe.config.disableNoCacheFetch||(o.cache="no-cache"),e.useCredentials?o.credentials="include":!Pe.config.disableIntegrityCheck&&e.hash&&(o.integrity=e.hash),Pe.fetch_like(t,o)}(e),o={name:e.name,url:e.resolvedUrl,response:t};return ue.add(e.name),o.response.then((()=>{"assembly"==e.behavior&&Pe.loadedAssemblies.push(e.name),de++,Pe.onDownloadResourceProgress&&Pe.onDownloadResourceProgress(de,ue.size)})),o}catch(t){const o={ok:!1,url:e.resolvedUrl,status:500,statusText:"ERR29: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t}};return{name:e.name,url:e.resolvedUrl,response:Promise.resolve(o)}}}const me={resource:"assembly",assembly:"assembly",pdb:"pdb",icu:"globalization",vfs:"configuration",manifest:"manifest",dotnetwasm:"dotnetwasm","js-module-dotnet":"dotnetjs","js-module-native":"dotnetjs","js-module-runtime":"dotnetjs","js-module-threads":"dotnetjs"};function ge(e){var t;if(Pe.loadBootResource){const o=null!==(t=e.hash)&&void 0!==t?t:"",n=e.resolvedUrl,r=me[e.behavior];if(r){const t=Pe.loadBootResource(r,e.name,n,o,e.behavior);return"string"==typeof t?I(t):t}}}function pe(e){e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null}function he(e){let t=e.lastIndexOf("/");return t>=0&&t++,e.substring(t)}async function we(e){e&&await Promise.all((null!=e?e:[]).map((e=>async function(e){try{const t=e.name;if(!e.moduleExports){const o=ce(Pe.locateFile(t),"js-module-library-initializer");Pe.diagnosticTracing&&b(`Attempting to import '${o}' for ${e}`),e.moduleExports=await import(/*! webpackIgnore: true */o)}Pe.libraryInitializers.push({scriptName:t,exports:e.moduleExports})}catch(t){E(`Failed to import library initializer '${e}': ${t}`)}}(e))))}async function be(e,t){if(!Pe.libraryInitializers)return;const o=[];for(let n=0;n<Pe.libraryInitializers.length;n++){const r=Pe.libraryInitializers[n];r.exports[e]&&o.push(ye(r.scriptName,e,(()=>r.exports[e](...t))))}await Promise.all(o)}async function ye(e,t,o){try{await o()}catch(o){throw E(`Failed to invoke '${t}' on library initializer '${e}': ${o}`),Xe(1,o),o}}function ve(e,t){if(e===t)return e;const o={...t};return void 0!==o.assets&&o.assets!==e.assets&&(o.assets=[...e.assets||[],...o.assets||[]]),void 0!==o.resources&&(o.resources=_e(e.resources||{assembly:[],jsModuleNative:[],jsModuleRuntime:[],wasmNative:[]},o.resources)),void 0!==o.environmentVariables&&(o.environmentVariables={...e.environmentVariables||{},...o.environmentVariables||{}}),void 0!==o.runtimeOptions&&o.runtimeOptions!==e.runtimeOptions&&(o.runtimeOptions=[...e.runtimeOptions||[],...o.runtimeOptions||[]]),Object.assign(e,o)}function Ee(e,t){if(e===t)return e;const o={...t};return o.config&&(e.config||(e.config={}),o.config=ve(e.config,o.config)),Object.assign(e,o)}function _e(e,t){if(e===t)return e;const o={...t};return void 0!==o.coreAssembly&&(o.coreAssembly=[...e.coreAssembly||[],...o.coreAssembly||[]]),void 0!==o.assembly&&(o.assembly=[...e.assembly||[],...o.assembly||[]]),void 0!==o.lazyAssembly&&(o.lazyAssembly=[...e.lazyAssembly||[],...o.lazyAssembly||[]]),void 0!==o.corePdb&&(o.corePdb=[...e.corePdb||[],...o.corePdb||[]]),void 0!==o.pdb&&(o.pdb=[...e.pdb||[],...o.pdb||[]]),void 0!==o.jsModuleWorker&&(o.jsModuleWorker=[...e.jsModuleWorker||[],...o.jsModuleWorker||[]]),void 0!==o.jsModuleNative&&(o.jsModuleNative=[...e.jsModuleNative||[],...o.jsModuleNative||[]]),void 0!==o.jsModuleDiagnostics&&(o.jsModuleDiagnostics=[...e.jsModuleDiagnostics||[],...o.jsModuleDiagnostics||[]]),void 0!==o.jsModuleRuntime&&(o.jsModuleRuntime=[...e.jsModuleRuntime||[],...o.jsModuleRuntime||[]]),void 0!==o.wasmSymbols&&(o.wasmSymbols=[...e.wasmSymbols||[],...o.wasmSymbols||[]]),void 0!==o.wasmNative&&(o.wasmNative=[...e.wasmNative||[],...o.wasmNative||[]]),void 0!==o.icu&&(o.icu=[...e.icu||[],...o.icu||[]]),void 0!==o.satelliteResources&&(o.satelliteResources=function(e,t){if(e===t)return e;for(const o in t)e[o]=[...e[o]||[],...t[o]||[]];return e}(e.satelliteResources||{},o.satelliteResources||{})),void 0!==o.modulesAfterConfigLoaded&&(o.modulesAfterConfigLoaded=[...e.modulesAfterConfigLoaded||[],...o.modulesAfterConfigLoaded||[]]),void 0!==o.modulesAfterRuntimeReady&&(o.modulesAfterRuntimeReady=[...e.modulesAfterRuntimeReady||[],...o.modulesAfterRuntimeReady||[]]),void 0!==o.extensions&&(o.extensions={...e.extensions||{},...o.extensions||{}}),void 0!==o.vfs&&(o.vfs=[...e.vfs||[],...o.vfs||[]]),Object.assign(e,o)}function xe(){const e=Pe.config;if(e.environmentVariables=e.environmentVariables||{},e.runtimeOptions=e.runtimeOptions||[],e.resources=e.resources||{assembly:[],jsModuleNative:[],jsModuleWorker:[],jsModuleRuntime:[],wasmNative:[],vfs:[],satelliteResources:{}},e.assets){Pe.diagnosticTracing&&b("config.assets is deprecated, use config.resources instead");for(const t of e.assets){const o={};switch(t.behavior){case"assembly":o.assembly=[t];break;case"pdb":o.pdb=[t];break;case"resource":o.satelliteResources={},o.satelliteResources[t.culture]=[t];break;case"icu":o.icu=[t];break;case"symbols":o.wasmSymbols=[t];break;case"vfs":o.vfs=[t];break;case"dotnetwasm":o.wasmNative=[t];break;case"js-module-threads":o.jsModuleWorker=[t];break;case"js-module-runtime":o.jsModuleRuntime=[t];break;case"js-module-native":o.jsModuleNative=[t];break;case"js-module-diagnostics":o.jsModuleDiagnostics=[t];break;case"js-module-dotnet":break;default:throw new Error(`Unexpected behavior ${t.behavior} of asset ${t.name}`)}_e(e.resources,o)}}e.debugLevel,e.applicationEnvironment||(e.applicationEnvironment="Production"),e.applicationCulture&&(e.environmentVariables.LANG=`${e.applicationCulture}.UTF-8`),Ue.diagnosticTracing=Pe.diagnosticTracing=!!e.diagnosticTracing,Ue.waitForDebugger=e.waitForDebugger,Pe.maxParallelDownloads=e.maxParallelDownloads||Pe.maxParallelDownloads,Pe.enableDownloadRetry=void 0!==e.enableDownloadRetry?e.enableDownloadRetry:Pe.enableDownloadRetry}let je=!1;async function Re(e){var t;if(je)return void await Pe.afterConfigLoaded.promise;let o;try{if(e.configSrc||Pe.config&&0!==Object.keys(Pe.config).length&&(Pe.config.assets||Pe.config.resources)||(e.configSrc="dotnet.boot.js"),o=e.configSrc,je=!0,o&&(Pe.diagnosticTracing&&b("mono_wasm_load_config"),await async function(e){const t=e.configSrc,o=Pe.locateFile(t);let n=null;void 0!==Pe.loadBootResource&&(n=Pe.loadBootResource("manifest",t,o,"","manifest"));let r,i=null;if(n)if("string"==typeof n)n.includes(".json")?(i=await s(I(n)),r=await Ae(i)):r=(await import(I(n))).config;else{const e=await n;"function"==typeof e.json?(i=e,r=await Ae(i)):r=e.config}else o.includes(".json")?(i=await s(ce(o,"manifest")),r=await Ae(i)):r=(await import(ce(o,"manifest"))).config;function s(e){return Pe.fetch_like(e,{method:"GET",credentials:"include",cache:"no-cache"})}Pe.config.applicationEnvironment&&(r.applicationEnvironment=Pe.config.applicationEnvironment),ve(Pe.config,r)}(e)),xe(),await we(null===(t=Pe.config.resources)||void 0===t?void 0:t.modulesAfterConfigLoaded),await be("onRuntimeConfigLoaded",[Pe.config]),e.onConfigLoaded)try{await e.onConfigLoaded(Pe.config,Le),xe()}catch(e){throw _("onConfigLoaded() failed",e),e}xe(),Pe.afterConfigLoaded.promise_control.resolve(Pe.config)}catch(t){const n=`Failed to load config file ${o} ${t} ${null==t?void 0:t.stack}`;throw Pe.config=e.config=Object.assign(Pe.config,{message:n,error:t,isError:!0}),Xe(1,new Error(n)),t}}function Te(){return!!globalThis.navigator&&(Pe.isChromium||Pe.isFirefox)}async function Ae(e){const t=Pe.config,o=await e.json();t.applicationEnvironment||o.applicationEnvironment||(o.applicationEnvironment=e.headers.get("Blazor-Environment")||e.headers.get("DotNet-Environment")||void 0),o.environmentVariables||(o.environmentVariables={});const n=e.headers.get("DOTNET-MODIFIABLE-ASSEMBLIES");n&&(o.environmentVariables.DOTNET_MODIFIABLE_ASSEMBLIES=n);const r=e.headers.get("ASPNETCORE-BROWSER-TOOLS");return r&&(o.environmentVariables.__ASPNETCORE_BROWSER_TOOLS=r),o}"function"!=typeof importScripts||globalThis.onmessage||(globalThis.dotnetSidecar=!0);const Se="object"==typeof process&&"object"==typeof process.versions&&"string"==typeof process.versions.node,De="function"==typeof importScripts,Oe=De&&"undefined"!=typeof dotnetSidecar,Ce=De&&!Oe,ke="object"==typeof window||De&&!Se,Ie=!ke&&!Se;let Ue={},Pe={},Me={},Le={},Ne={},$e=!1;const ze={},We={config:ze},Fe={mono:{},binding:{},internal:Ne,module:We,loaderHelpers:Pe,runtimeHelpers:Ue,diagnosticHelpers:Me,api:Le};function Be(e,t){if(e)return;const o="Assert failed: "+("function"==typeof t?t():t),n=new Error(o);_(o,n),Ue.nativeAbort(n)}function Ve(){return void 0!==Pe.exitCode}function qe(){return Ue.runtimeReady&&!Ve()}function He(){Ve()&&Be(!1,`.NET runtime already exited with ${Pe.exitCode} ${Pe.exitReason}. You can use runtime.runMain() which doesn't exit the runtime.`),Ue.runtimeReady||Be(!1,".NET runtime didn't start yet. Please call dotnet.create() first.")}function Je(){ke&&(globalThis.addEventListener("unhandledrejection",et),globalThis.addEventListener("error",tt))}let Ze,Qe;function Ge(e){Qe&&Qe(e),Xe(e,Pe.exitReason)}function Ke(e){Ze&&Ze(e||Pe.exitReason),Xe(1,e||Pe.exitReason)}function Xe(t,o){var n,r;const i=o&&"object"==typeof o;t=i&&"number"==typeof o.status?o.status:void 0===t?-1:t;const s=i&&"string"==typeof o.message?o.message:""+o;(o=i?o:Ue.ExitStatus?function(e,t){const o=new Ue.ExitStatus(e);return o.message=t,o.toString=()=>t,o}(t,s):new Error("Exit with code "+t+" "+s)).status=t,o.message||(o.message=s);const a=""+(o.stack||(new Error).stack);try{Object.defineProperty(o,"stack",{get:()=>a})}catch(e){}const l=!!o.silent;if(o.silent=!0,Ve())Pe.diagnosticTracing&&b("mono_exit called after exit");else{try{We.onAbort==Ke&&(We.onAbort=Ze),We.onExit==Ge&&(We.onExit=Qe),ke&&(globalThis.removeEventListener("unhandledrejection",et),globalThis.removeEventListener("error",tt)),Ue.runtimeReady?(Ue.jiterpreter_dump_stats&&Ue.jiterpreter_dump_stats(!1),0===t&&(null===(n=Pe.config)||void 0===n?void 0:n.interopCleanupOnExit)&&Ue.forceDisposeProxies(!0,!0),e&&0!==t&&(null===(r=Pe.config)||void 0===r||r.dumpThreadsOnNonZeroExit)):(Pe.diagnosticTracing&&b(`abort_startup, reason: ${o}`),function(e){Pe.allDownloadsQueued.promise_control.reject(e),Pe.allDownloadsFinished.promise_control.reject(e),Pe.afterConfigLoaded.promise_control.reject(e),Pe.wasmCompilePromise.promise_control.reject(e),Pe.runtimeModuleLoaded.promise_control.reject(e),Ue.dotnetReady&&(Ue.dotnetReady.promise_control.reject(e),Ue.afterInstantiateWasm.promise_control.reject(e),Ue.beforePreInit.promise_control.reject(e),Ue.afterPreInit.promise_control.reject(e),Ue.afterPreRun.promise_control.reject(e),Ue.beforeOnRuntimeInitialized.promise_control.reject(e),Ue.afterOnRuntimeInitialized.promise_control.reject(e),Ue.afterPostRun.promise_control.reject(e))}(o))}catch(e){E("mono_exit A failed",e)}try{l||(function(e,t){if(0!==e&&t){const e=Ue.ExitStatus&&t instanceof Ue.ExitStatus?b:_;"string"==typeof t?e(t):(void 0===t.stack&&(t.stack=(new Error).stack+""),t.message?e(Ue.stringify_as_error_with_stack?Ue.stringify_as_error_with_stack(t.message+"\n"+t.stack):t.message+"\n"+t.stack):e(JSON.stringify(t)))}!Ce&&Pe.config&&(Pe.config.logExitCode?Pe.config.forwardConsoleLogsToWS?R("WASM EXIT "+e):v("WASM EXIT "+e):Pe.config.forwardConsoleLogsToWS&&R())}(t,o),function(e){if(ke&&!Ce&&Pe.config&&Pe.config.appendElementOnExit&&document){const t=document.createElement("label");t.id="tests_done",0!==e&&(t.style.background="red"),t.innerHTML=""+e,document.body.appendChild(t)}}(t))}catch(e){E("mono_exit B failed",e)}Pe.exitCode=t,Pe.exitReason||(Pe.exitReason=o),!Ce&&Ue.runtimeReady&&We.runtimeKeepalivePop()}if(Pe.config&&Pe.config.asyncFlushOnExit&&0===t)throw(async()=>{try{await async function(){try{const e=await import(/*! webpackIgnore: true */"process"),t=e=>new Promise(((t,o)=>{e.on("error",o),e.end("","utf8",t)})),o=t(e.stderr),n=t(e.stdout);let r;const i=new Promise((e=>{r=setTimeout((()=>e("timeout")),1e3)}));await Promise.race([Promise.all([n,o]),i]),clearTimeout(r)}catch(e){_(`flushing std* streams failed: ${e}`)}}()}finally{Ye(t,o)}})(),o;Ye(t,o)}function Ye(e,t){if(Ue.runtimeReady&&Ue.nativeExit)try{Ue.nativeExit(e)}catch(e){!Ue.ExitStatus||e instanceof Ue.ExitStatus||E("set_exit_code_and_quit_now failed: "+e.toString())}if(0!==e||!ke)throw Se&&Ne.process?Ne.process.exit(e):Ue.quit&&Ue.quit(e,t),t}function et(e){ot(e,e.reason,"rejection")}function tt(e){ot(e,e.error,"error")}function ot(e,t,o){e.preventDefault();try{t||(t=new Error("Unhandled "+o)),void 0===t.stack&&(t.stack=(new Error).stack),t.stack=t.stack+"",t.silent||(_("Unhandled error:",t),Xe(1,t))}catch(e){}}!function(e){if($e)throw new Error("Loader module already loaded");$e=!0,Ue=e.runtimeHelpers,Pe=e.loaderHelpers,Me=e.diagnosticHelpers,Le=e.api,Ne=e.internal,Object.assign(Le,{INTERNAL:Ne,invokeLibraryInitializers:be}),Object.assign(e.module,{config:ve(ze,{environmentVariables:{}})});const r={mono_wasm_bindings_is_ready:!1,config:e.module.config,diagnosticTracing:!1,nativeAbort:e=>{throw e||new Error("abort")},nativeExit:e=>{throw new Error("exit:"+e)}},l={gitHash:"e2f47b0110ed922f21a1522da67279133ce28f32",config:e.module.config,diagnosticTracing:!1,maxParallelDownloads:16,enableDownloadRetry:!0,_loaded_files:[],loadedFiles:[],loadedAssemblies:[],libraryInitializers:[],workerNextNumber:1,actual_downloaded_assets_count:0,actual_instantiated_assets_count:0,expected_downloaded_assets_count:0,expected_instantiated_assets_count:0,afterConfigLoaded:i(),allDownloadsQueued:i(),allDownloadsFinished:i(),wasmCompilePromise:i(),runtimeModuleLoaded:i(),loadingWorkers:i(),is_exited:Ve,is_runtime_running:qe,assert_runtime_running:He,mono_exit:Xe,createPromiseController:i,getPromiseController:s,assertIsControllablePromise:a,mono_download_assets:oe,resolve_single_asset_path:ee,setup_proxy_console:j,set_thread_prefix:w,installUnhandledErrorHandler:Je,retrieve_asset_download:ie,invokeLibraryInitializers:be,isDebuggingSupported:Te,exceptions:t,simd:n,relaxedSimd:o};Object.assign(Ue,r),Object.assign(Pe,l)}(Fe);let nt,rt,it,st=!1,at=!1;async function lt(e){if(!at){if(at=!0,ke&&Pe.config.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&j("main",globalThis.console,globalThis.location.origin),We||Be(!1,"Null moduleConfig"),Pe.config||Be(!1,"Null moduleConfig.config"),"function"==typeof e){const t=e(Fe.api);if(t.ready)throw new Error("Module.ready couldn't be redefined.");Object.assign(We,t),Ee(We,t)}else{if("object"!=typeof e)throw new Error("Can't use moduleFactory callback of createDotnetRuntime function.");Ee(We,e)}await async function(e){if(Se){const e=await import(/*! webpackIgnore: true */"process"),t=14;if(e.versions.node.split(".")[0]<t)throw new Error(`NodeJS at '${e.execPath}' has too low version '${e.versions.node}', please use at least ${t}. See also https://aka.ms/dotnet-wasm-features`)}const t=/*! webpackIgnore: true */import.meta.url,o=t.indexOf("?");var n;if(o>0&&(Pe.modulesUniqueQuery=t.substring(o)),Pe.scriptUrl=t.replace(/\\/g,"/").replace(/[?#].*/,""),Pe.scriptDirectory=(n=Pe.scriptUrl).slice(0,n.lastIndexOf("/"))+"/",Pe.locateFile=e=>"URL"in globalThis&&globalThis.URL!==C?new URL(e,Pe.scriptDirectory).toString():M(e)?e:Pe.scriptDirectory+e,Pe.fetch_like=k,Pe.out=console.log,Pe.err=console.error,Pe.onDownloadResourceProgress=e.onDownloadResourceProgress,ke&&globalThis.navigator){const e=globalThis.navigator,t=e.userAgentData&&e.userAgentData.brands;t&&t.length>0?Pe.isChromium=t.some((e=>"Google Chrome"===e.brand||"Microsoft Edge"===e.brand||"Chromium"===e.brand)):e.userAgent&&(Pe.isChromium=e.userAgent.includes("Chrome"),Pe.isFirefox=e.userAgent.includes("Firefox"))}Ne.require=Se?await import(/*! webpackIgnore: true */"module").then((e=>e.createRequire(/*! webpackIgnore: true */import.meta.url))):Promise.resolve((()=>{throw new Error("require not supported")})),void 0===globalThis.URL&&(globalThis.URL=C)}(We)}}async function ct(e){return await lt(e),Ze=We.onAbort,Qe=We.onExit,We.onAbort=Ke,We.onExit=Ge,We.ENVIRONMENT_IS_PTHREAD?async function(){(function(){const e=new MessageChannel,t=e.port1,o=e.port2;t.addEventListener("message",(e=>{var n,r;n=JSON.parse(e.data.config),r=JSON.parse(e.data.monoThreadInfo),st?Pe.diagnosticTracing&&b("mono config already received"):(ve(Pe.config,n),Ue.monoThreadInfo=r,xe(),Pe.diagnosticTracing&&b("mono config received"),st=!0,Pe.afterConfigLoaded.promise_control.resolve(Pe.config),ke&&n.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&Pe.setup_proxy_console("worker-idle",console,globalThis.location.origin)),t.close(),o.close()}),{once:!0}),t.start(),self.postMessage({[l]:{monoCmd:"preload",port:o}},[o])})(),await Pe.afterConfigLoaded.promise,function(){const e=Pe.config;e.assets||Be(!1,"config.assets must be defined");for(const t of e.assets)X(t),Q[t.behavior]&&z.push(t)}(),setTimeout((async()=>{try{await oe()}catch(e){Xe(1,e)}}),0);const e=dt(),t=await Promise.all(e);return await ut(t),We}():async function(){var e;await Re(We),re();const t=dt();(async function(){try{const e=ee("dotnetwasm");await se(e),e&&e.pendingDownloadInternal&&e.pendingDownloadInternal.response||Be(!1,"Can't load dotnet.native.wasm");const t=await e.pendingDownloadInternal.response,o=t.headers&&t.headers.get?t.headers.get("Content-Type"):void 0;let n;if("function"==typeof WebAssembly.compileStreaming&&"application/wasm"===o)n=await WebAssembly.compileStreaming(t);else{ke&&"application/wasm"!==o&&E('WebAssembly resource does not have the expected content type "application/wasm", so falling back to slower ArrayBuffer instantiation.');const e=await t.arrayBuffer();Pe.diagnosticTracing&&b("instantiate_wasm_module buffered"),n=Ie?await Promise.resolve(new WebAssembly.Module(e)):await WebAssembly.compile(e)}e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null,Pe.wasmCompilePromise.promise_control.resolve(n)}catch(e){Pe.wasmCompilePromise.promise_control.reject(e)}})(),setTimeout((async()=>{try{D(),await oe()}catch(e){Xe(1,e)}}),0);const o=await Promise.all(t);return await ut(o),await Ue.dotnetReady.promise,await we(null===(e=Pe.config.resources)||void 0===e?void 0:e.modulesAfterRuntimeReady),await be("onRuntimeReady",[Fe.api]),Le}()}function dt(){const e=ee("js-module-runtime"),t=ee("js-module-native");if(nt&&rt)return[nt,rt,it];"object"==typeof e.moduleExports?nt=e.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${e.resolvedUrl}' for ${e.name}`),nt=import(/*! webpackIgnore: true */e.resolvedUrl)),"object"==typeof t.moduleExports?rt=t.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${t.resolvedUrl}' for ${t.name}`),rt=import(/*! webpackIgnore: true */t.resolvedUrl));const o=Y("js-module-diagnostics");return o&&("object"==typeof o.moduleExports?it=o.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${o.resolvedUrl}' for ${o.name}`),it=import(/*! webpackIgnore: true */o.resolvedUrl))),[nt,rt,it]}async function ut(e){const{initializeExports:t,initializeReplacements:o,configureRuntimeStartup:n,configureEmscriptenStartup:r,configureWorkerStartup:i,setRuntimeGlobals:s,passEmscriptenInternals:a}=e[0],{default:l}=e[1],c=e[2];s(Fe),t(Fe),c&&c.setRuntimeGlobals(Fe),await n(We),Pe.runtimeModuleLoaded.promise_control.resolve(),l((e=>(Object.assign(We,{ready:e.ready,__dotnet_runtime:{initializeReplacements:o,configureEmscriptenStartup:r,configureWorkerStartup:i,passEmscriptenInternals:a}}),We))).catch((e=>{if(e.message&&e.message.toLowerCase().includes("out of memory"))throw new Error(".NET runtime has failed to start, because too much memory was requested. Please decrease the memory by adjusting EmccMaximumHeapSize. See also https://aka.ms/dotnet-wasm-features");throw e}))}const ft=new class{withModuleConfig(e){try{return Ee(We,e),this}catch(e){throw Xe(1,e),e}}withOnConfigLoaded(e){try{return Ee(We,{onConfigLoaded:e}),this}catch(e){throw Xe(1,e),e}}withConsoleForwarding(){try{return ve(ze,{forwardConsoleLogsToWS:!0}),this}catch(e){throw Xe(1,e),e}}withExitOnUnhandledError(){try{return ve(ze,{exitOnUnhandledError:!0}),Je(),this}catch(e){throw Xe(1,e),e}}withAsyncFlushOnExit(){try{return ve(ze,{asyncFlushOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withExitCodeLogging(){try{return ve(ze,{logExitCode:!0}),this}catch(e){throw Xe(1,e),e}}withElementOnExit(){try{return ve(ze,{appendElementOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withInteropCleanupOnExit(){try{return ve(ze,{interopCleanupOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withDumpThreadsOnNonZeroExit(){try{return ve(ze,{dumpThreadsOnNonZeroExit:!0}),this}catch(e){throw Xe(1,e),e}}withWaitingForDebugger(e){try{return ve(ze,{waitForDebugger:e}),this}catch(e){throw Xe(1,e),e}}withInterpreterPgo(e,t){try{return ve(ze,{interpreterPgo:e,interpreterPgoSaveDelay:t}),ze.runtimeOptions?ze.runtimeOptions.push("--interp-pgo-recording"):ze.runtimeOptions=["--interp-pgo-recording"],this}catch(e){throw Xe(1,e),e}}withConfig(e){try{return ve(ze,e),this}catch(e){throw Xe(1,e),e}}withConfigSrc(e){try{return e&&"string"==typeof e||Be(!1,"must be file path or URL"),Ee(We,{configSrc:e}),this}catch(e){throw Xe(1,e),e}}withVirtualWorkingDirectory(e){try{return e&&"string"==typeof e||Be(!1,"must be directory path"),ve(ze,{virtualWorkingDirectory:e}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariable(e,t){try{const o={};return o[e]=t,ve(ze,{environmentVariables:o}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariables(e){try{return e&&"object"==typeof e||Be(!1,"must be dictionary object"),ve(ze,{environmentVariables:e}),this}catch(e){throw Xe(1,e),e}}withDiagnosticTracing(e){try{return"boolean"!=typeof e&&Be(!1,"must be boolean"),ve(ze,{diagnosticTracing:e}),this}catch(e){throw Xe(1,e),e}}withDebugging(e){try{return null!=e&&"number"==typeof e||Be(!1,"must be number"),ve(ze,{debugLevel:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArguments(...e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ve(ze,{applicationArguments:e}),this}catch(e){throw Xe(1,e),e}}withRuntimeOptions(e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ze.runtimeOptions?ze.runtimeOptions.push(...e):ze.runtimeOptions=e,this}catch(e){throw Xe(1,e),e}}withMainAssembly(e){try{return ve(ze,{mainAssemblyName:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArgumentsFromQuery(){try{if(!globalThis.window)throw new Error("Missing window to the query parameters from");if(void 0===globalThis.URLSearchParams)throw new Error("URLSearchParams is supported");const e=new URLSearchParams(globalThis.window.location.search).getAll("arg");return this.withApplicationArguments(...e)}catch(e){throw Xe(1,e),e}}withApplicationEnvironment(e){try{return ve(ze,{applicationEnvironment:e}),this}catch(e){throw Xe(1,e),e}}withApplicationCulture(e){try{return ve(ze,{applicationCulture:e}),this}catch(e){throw Xe(1,e),e}}withResourceLoader(e){try{return Pe.loadBootResource=e,this}catch(e){throw Xe(1,e),e}}async download(){try{await async function(){lt(We),await Re(We),re(),D(),oe(),await Pe.allDownloadsFinished.promise}()}catch(e){throw Xe(1,e),e}}async create(){try{return this.instance||(this.instance=await async function(){return await ct(We),Fe.api}()),this.instance}catch(e){throw Xe(1,e),e}}async run(){try{return We.config||Be(!1,"Null moduleConfig.config"),this.instance||await this.create(),this.instance.runMainAndExit()}catch(e){throw Xe(1,e),e}}},mt=Xe,gt=ct;Ie||"function"==typeof globalThis.URL||Be(!1,"This browser/engine doesn't support URL API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),"function"!=typeof globalThis.BigInt64Array&&Be(!1,"This browser/engine doesn't support BigInt64Array API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),ft.withConfig(/*json-start*/{
  "mainAssemblyName": "GalleryBrowser",
  "resources": {
    "hash": "sha256-LsvVIHNUGk1jovctYdQGebUTP69sS5AXEZw21Sx3mqc=",
    "jsModuleNative": [
      {
        "name": "dotnet.native.m6x6l359wr.js"
      }
    ],
    "jsModuleRuntime": [
      {
        "name": "dotnet.runtime.zbexyp8zrs.js"
      }
    ],
    "wasmNative": [
      {
        "name": "dotnet.native.4tdu2f1bry.wasm",
        "hash": "sha256-aE93jDV5fO0Orwn1vKl2KzOVxkzPVpBXqxKfhT0vL0Q=",
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
        "name": "System.Private.CoreLib.6jsp929v6s.wasm",
        "hash": "sha256-TmJKFp+5z/6a/PQiKNf1Y+3uR0McBzA22C+2bZyLdok=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.JavaScript.wasm",
        "name": "System.Runtime.InteropServices.JavaScript.lcvzq78wga.wasm",
        "hash": "sha256-iyBp1rhqxp+1xie/Mtt70VjTr9ELelXgW/UE3yb4hWA=",
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
        "name": "Friflo.Engine.ECS.1klg6ah2ip.wasm",
        "hash": "sha256-33DNY1SCx+z10SKbpm0BxEwd+n0SsvFZgqlYc+YqCVQ=",
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
        "name": "GalleryBrowser.rbff13wcw2.wasm",
        "hash": "sha256-V4hpWuefJX8ngT3JqKWwK+cZdmcoq1bRsvJBIJH8lFQ=",
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
        "name": "Luxel.Animation.Gallery.1bj59m9wg5.wasm",
        "hash": "sha256-/UHoYFvYnL0+3d/3gEpM1tUFHoj6GK5BOPMio0g2vLo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Animation.ThreeD.wasm",
        "name": "Luxel.Animation.ThreeD.vqh1daqcxo.wasm",
        "hash": "sha256-6bpzE9KJrFbkoNF/BOYWjjOepMxcCvJjF/16RE0aqDM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Animation.TwoD.wasm",
        "name": "Luxel.Animation.TwoD.34i4s1exoe.wasm",
        "hash": "sha256-s5SEaMqw4UEpGGoEBOJuJsIzjvRzaW+WUgL/M5sJh34=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Animation.UI.wasm",
        "name": "Luxel.Animation.UI.4hmt2bvc6y.wasm",
        "hash": "sha256-+KpGnf++BVydVGuusw9akGwnoHKBvVQgNqMSdDnRpg0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Animation.wasm",
        "name": "Luxel.Animation.td4lnxx9eb.wasm",
        "hash": "sha256-cfT2t0c1Et5MFQnnY7874yD1373WMQSybpWDkyzRLok=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.AssetRuntime.wasm",
        "name": "Luxel.AssetRuntime.ubhuyws9th.wasm",
        "hash": "sha256-xmpHX7e4FVPX8tW67KUWlXoW12A9efoz/iix2/xUsrc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Assets.Gltf.wasm",
        "name": "Luxel.Assets.Gltf.dk9ryt6mwj.wasm",
        "hash": "sha256-LT/PrRLZRqpwK2OKNAP3fU080QG+a2qW5OYwHmVuEVg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Assets.wasm",
        "name": "Luxel.Assets.by962rwdpj.wasm",
        "hash": "sha256-Hbooa2c5XvrAJr2O6gEhpEVrbKdhTPO2d4EPG6JJbDo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.AssetsGpu.wasm",
        "name": "Luxel.AssetsGpu.cqbg4h0khl.wasm",
        "hash": "sha256-nPhWaoET3NjY9xEflbzktkLXzDMnYVC7hWmGKfnvbGg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Audio.Browser.wasm",
        "name": "Luxel.Audio.Browser.lafielz5j7.wasm",
        "hash": "sha256-7c+o4C2n3tUsVeVUIQwlq7zBeRxfcUuyGYO1Y7xTSiQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Audio.Gallery.wasm",
        "name": "Luxel.Audio.Gallery.a2702os5as.wasm",
        "hash": "sha256-Pjrmp47vkavr2/ngIpUVL1GuGWpUXVEaY6kcDDGuX2E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Audio.wasm",
        "name": "Luxel.Audio.lyukjdnfek.wasm",
        "hash": "sha256-vVA+TvxR8FA8ASkbVMK82AOY6oAXWBaJSIh37AhBShk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Controls.wasm",
        "name": "Luxel.Controls.b0rfd7km64.wasm",
        "hash": "sha256-hWDAU/jcJ8KQ6+8TfSob9siLkW1Ttfmi/U8x+DjZ1Vc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.DevTools.Gallery.wasm",
        "name": "Luxel.DevTools.Gallery.pe4t2vmyew.wasm",
        "hash": "sha256-b0YQ9t2NCi6W9T7lDUNQTrBJ5HMo2AzC0YaX1Cf8Mjk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.DevTools.wasm",
        "name": "Luxel.DevTools.rulgu2kq11.wasm",
        "hash": "sha256-XUc5tnhn+bLosIAOUyByG7dVEeM20X2A7aqLT9eqtto=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Diagnostics.wasm",
        "name": "Luxel.Diagnostics.uipgeyc4cp.wasm",
        "hash": "sha256-ysS1wJPEJOK5nm5Zzx6TIrDc2ba/Z99dNbiUYVSrUmI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Diagram.wasm",
        "name": "Luxel.Diagram.9ohge5y4iw.wasm",
        "hash": "sha256-hyD9M6EADCj9nKoqvxiozj63cygqxAE6DR8UwkqNwps=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Document.wasm",
        "name": "Luxel.Document.7y1ls2fncn.wasm",
        "hash": "sha256-4W5s95NPxdOt+Ue8RfslNMPmHs9cfFLUGqJZtp25NAQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Ecs.wasm",
        "name": "Luxel.Ecs.1dtrsc77t8.wasm",
        "hash": "sha256-m7ZUX+dEH26LVGyXVCp9mggnNnh8gSH50Z/xb8qm6RA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Ecs.Signal.wasm",
        "name": "Luxel.Ecs.Signal.wu13ojmfku.wasm",
        "hash": "sha256-2G8fjhBwkEKsjzFcZS1iGtSpiRzO3HoGzxDLdd0GHX0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Editor.Gallery.wasm",
        "name": "Luxel.Editor.Gallery.vyoa9z9a6g.wasm",
        "hash": "sha256-58FevKof680xw0VhKLJybspJCEz1K9qGI5PuD1y2018=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Framework.Gallery.wasm",
        "name": "Luxel.Framework.Gallery.a4ydepwj3i.wasm",
        "hash": "sha256-+n9G1Vox7UhLUtIG4KycE0WAE9zwwalHfkGeZ5Y7Y3k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Framework.Game.wasm",
        "name": "Luxel.Framework.Game.jo5c6n50gi.wasm",
        "hash": "sha256-PYaVqfhEpWjSbL+RR8WgpOPmZ1/l1JYmsjUI2CByjUk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.Browser.wasm",
        "name": "Luxel.Gallery.Browser.5311a838h1.wasm",
        "hash": "sha256-wVVaaJYxhmUsNS3NmjLJJSIv+aA8nQqaE42W9Nh+DRQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.DocKit.wasm",
        "name": "Luxel.Gallery.DocKit.fzt14cvj3w.wasm",
        "hash": "sha256-9c0j2Y3b27XzXIbzZB09oJBk7/T003w2uAqOQJFceWo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.Docs.wasm",
        "name": "Luxel.Gallery.Docs.hxulsf406b.wasm",
        "hash": "sha256-1QerwxN5VLUEPcFGzGRrdtVj1Bzot+1/O8tTJurueXc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.Playground.wasm",
        "name": "Luxel.Gallery.Playground.fcif284mro.wasm",
        "hash": "sha256-nyx0h6dMTm5CdyCbGtzc5e4MZYem4N6rC+ne37hL2hM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.UI.wasm",
        "name": "Luxel.Gallery.UI.0ly3ptdwxa.wasm",
        "hash": "sha256-1O58VwKfNf5049ZtLEYZ1hYKZLbjt5W6L08/sV388aI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Gallery.wasm",
        "name": "Luxel.Gallery.o88fblrdrd.wasm",
        "hash": "sha256-iNnsIJpFrYnsnRwDFGuK3NQ5N/C5wfM3r7fME+ihl9Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.GamesSamples.Gallery.wasm",
        "name": "Luxel.GamesSamples.Gallery.c0wzx29c8d.wasm",
        "hash": "sha256-NNqUOSBuWS2ZvNqbkmpoE9JlKhMJKVIt3scUL4uSxMw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.Gallery.wasm",
        "name": "Luxel.Graphics.Gallery.k42x4etr7x.wasm",
        "hash": "sha256-BJUz3gMnJVclHS7vuqufN1GIjZe+pUN3IsN/m6AgR4M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.MessagePipe.wasm",
        "name": "Luxel.Graphics.MessagePipe.xvcyay6e2x.wasm",
        "hash": "sha256-vWe6NOpG7qMfvns1KHUuPybv3tdLjegYi5ZdiyE7mvY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.RenderGraph.wasm",
        "name": "Luxel.Graphics.RenderGraph.1ehq0ujvfe.wasm",
        "hash": "sha256-iQJxEP2rdlZB9SMW4spVkhDbOxU7y4DcZxLTC9wb0JE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.RenderSystem.wasm",
        "name": "Luxel.Graphics.RenderSystem.mhlcv5kstj.wasm",
        "hash": "sha256-NhWSSZYOKdGf6AEiN++sEvfsvpRArKOqDWZJaBuo/fE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.TwoD.wasm",
        "name": "Luxel.Graphics.TwoD.1clsw46c1y.wasm",
        "hash": "sha256-AWtxZovrxFg34H8+eLouKU1bI/juluY/ETHGvdxJows=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.WebGPU.Browser.wasm",
        "name": "Luxel.Graphics.WebGPU.Browser.pi9tzk8p1x.wasm",
        "hash": "sha256-cWj8YTCaXcYlGNy6wrAzmkoJCbcDjfWDUQMt/U5YjtU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Graphics.wasm",
        "name": "Luxel.Graphics.xwludi0ywm.wasm",
        "hash": "sha256-jwBBhD0LcmHU75QcP87p5JSma/kKbby56GvN2ouw+N4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Highlight.TextMate.wasm",
        "name": "Luxel.Highlight.TextMate.it3c7hoa47.wasm",
        "hash": "sha256-qhbnYy/VvfCSKcnBTEK7V5UjRBa0EahUjCxj/PY0yVo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Imaging.wasm",
        "name": "Luxel.Imaging.9brg7a6p7v.wasm",
        "hash": "sha256-RV2rs51w7K9HnJX1oM2GnItTsMWbxbmzdt2AliIfZXM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Input.wasm",
        "name": "Luxel.Input.8typtgvoho.wasm",
        "hash": "sha256-fOkNhX0OLbZv9FBRVBBfj2r8YfbLt8HtfrwL8AHeq50=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Input.Gallery.wasm",
        "name": "Luxel.Input.Gallery.dz0tj1hpmv.wasm",
        "hash": "sha256-x26Tf2QqO3bKSNOO08HFt5uljosQU7D15hVq14X26dY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.MathText.wasm",
        "name": "Luxel.MathText.025imywxlu.wasm",
        "hash": "sha256-GMiPTth7Y14oNyHjfZ9zsF4vH1wh2WV3QfTv86DKpWk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Mathematics.wasm",
        "name": "Luxel.Mathematics.tq1tmw2ig4.wasm",
        "hash": "sha256-FaSbX0iG1q0kCA1C7oDBkN6iDqFGa4uVaf9r2egsp3w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.NodeGraph.wasm",
        "name": "Luxel.NodeGraph.v3m8o4ns4r.wasm",
        "hash": "sha256-FcYO+kaDraGeJcFe9CaKkjhYW4DYTIFjmA9m/UFLtu4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Particles.Gallery.wasm",
        "name": "Luxel.Particles.Gallery.bg0wb5nfnn.wasm",
        "hash": "sha256-SxlpYQXnVgVyPdq76ISHVTfmRlo7ixrjCYQZGDjRcFo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Particles.ThreeD.wasm",
        "name": "Luxel.Particles.ThreeD.q7yycxvf3a.wasm",
        "hash": "sha256-Sr6wyqIZcP2wN8CegtZkaPfeD/iYcd/Qz0Zxg8Ti8IM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Particles.TwoD.wasm",
        "name": "Luxel.Particles.TwoD.p6fslladff.wasm",
        "hash": "sha256-4cRIQ3P7EgI/k9I+vv2UuqdD/BX2cPlcS0rqlZiYctM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Particles.UI.wasm",
        "name": "Luxel.Particles.UI.uofz5g8run.wasm",
        "hash": "sha256-Lt/WGxAaLZVMx/aLFyeNsrQ9C7OK0RwpZ3945A+IliY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Particles.wasm",
        "name": "Luxel.Particles.ss12r67icl.wasm",
        "hash": "sha256-nDjPEFSIiwBc8zyUpbu0ghFU0NoXviX9d5ue2mWeWAg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Physics.Gizmos.wasm",
        "name": "Luxel.Physics.Gizmos.hi9ojsvruo.wasm",
        "hash": "sha256-KdkjBnxAUaFlY7yyaaiztZ/VHosx6pstUo7J6Je8c9k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Physics.wasm",
        "name": "Luxel.Physics.z3zn38axx5.wasm",
        "hash": "sha256-f/TElR11+gFQ+47Nu+peELiBIlNiRM66a+UocTrS824=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Platform.Web.wasm",
        "name": "Luxel.Platform.Web.uwgntizhhh.wasm",
        "hash": "sha256-XuoNHVlIH8gqGK+P7qW9/jafR8SZS1/lzLMlOqb3og8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Platform.wasm",
        "name": "Luxel.Platform.d4x1q62dek.wasm",
        "hash": "sha256-ExCfc0zYmUXj8PF6Tv/ujrOpcBdsTdPSYDG+5WwR5l8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Player.wasm",
        "name": "Luxel.Player.1ab3o0ex1b.wasm",
        "hash": "sha256-J2zwoMijXLnl7kBqCuwzVNPN3OJqjJmi+ePINN/TPB8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Resources.wasm",
        "name": "Luxel.Resources.5gmv1but6w.wasm",
        "hash": "sha256-m6+zUm9ZGTTiWkcoCXkTlrd3MjpR7FFjvane5EdJvho=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Resources.Browser.wasm",
        "name": "Luxel.Resources.Browser.n8s24f5xce.wasm",
        "hash": "sha256-iVTynY1P7pbcJ/WgVKMv70fTeWNHyd/0dRPqInD7CAs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Resources.Gallery.wasm",
        "name": "Luxel.Resources.Gallery.vq4osrrays.wasm",
        "hash": "sha256-+blKIOJdEUzF54iLhEdBeNgpILCOzL8FiNU4Npb61sU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Scene.UI.wasm",
        "name": "Luxel.Scene.UI.rononuz6gl.wasm",
        "hash": "sha256-6+w2UXe8k+Zv3ujfktM8p9BS5ZI8nCQLrujMkX4OAAc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.SceneEdit.wasm",
        "name": "Luxel.SceneEdit.lkv6lunou1.wasm",
        "hash": "sha256-VcJw/KokLW1T4QSNQ7ZIX07CkyWANiM+5hRtoiHHgmo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Scripting.Framework.wasm",
        "name": "Luxel.Scripting.Framework.q8kcsel6f5.wasm",
        "hash": "sha256-elVAlAoUbPhDBRQyET9+4jBVihe+dfMUXKAroZhB7cY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Scripting.Gallery.wasm",
        "name": "Luxel.Scripting.Gallery.8l1hb7qc66.wasm",
        "hash": "sha256-tNaORmd7Wcv2UYvgUO4VTIXuv5gIH2nNQubNE1Y3Fx0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Scripting.Roslyn.Web.wasm",
        "name": "Luxel.Scripting.Roslyn.Web.ddueltigt9.wasm",
        "hash": "sha256-QXrN/eBT5x9EHazJJ5hjC9ln/6ukiaFH78SJafuv49U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Scripting.wasm",
        "name": "Luxel.Scripting.ra0pp7pcic.wasm",
        "hash": "sha256-PW1dIuKnztOISKANtbIlnrJZ43U8bpgiap7+0K7EHkA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Settings.wasm",
        "name": "Luxel.Settings.dq2n2jm3xc.wasm",
        "hash": "sha256-lft0IKE2GhVbyXtPHidLH5ip0BbpQx1rvxKLxDBfH0k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Shaders.Slang.Browser.wasm",
        "name": "Luxel.Shaders.Slang.Browser.ooq8vzdd8w.wasm",
        "hash": "sha256-GDSqgkweehpagKpnNLTuQjks9pdU3C/YQGb90D5DvFQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Shaders.Slang.Native.wasm",
        "name": "Luxel.Shaders.Slang.Native.275mfwcypv.wasm",
        "hash": "sha256-Mg0NomiN0k6bThPVOWiRo42e1xkg2j0kHd0gtq1TjYw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Shaders.wasm",
        "name": "Luxel.Shaders.he4r2ihimv.wasm",
        "hash": "sha256-CanIADyIT6t1DukJeXA+JssOb6xeU/39Zvp5PVNf+Nc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Strudel.wasm",
        "name": "Luxel.Strudel.nkgq894idl.wasm",
        "hash": "sha256-mZ2SC7SXRWL/+ZvzEV1YGi2epEpvYQrx9cqQzi6z+pM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Terminal.wasm",
        "name": "Luxel.Terminal.14cllhmfsj.wasm",
        "hash": "sha256-b5ZIatHqOmabXxWJ2YZD7FF3w52+QzmXQjVFfyFY2yE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Terminal.UI.wasm",
        "name": "Luxel.Terminal.UI.dru037tv7k.wasm",
        "hash": "sha256-uYotOzi4VGVt1+WcOIwVWdFxY0mxdZBzmL3U6bNmc5k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Typography.TwoD.wasm",
        "name": "Luxel.Typography.TwoD.kwlmemrnom.wasm",
        "hash": "sha256-ZG5/aOltjMt2mgP96FGyO+bbqQto/SNBp1yWCQzwo2I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Typography.wasm",
        "name": "Luxel.Typography.rzaofodlsy.wasm",
        "hash": "sha256-WdM77l5DZOOY29+SlHJgeq3n5D1D3NkY0AXkhhSfSf4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.UI.Gallery.wasm",
        "name": "Luxel.UI.Gallery.hfc94rrg49.wasm",
        "hash": "sha256-09iAAJhojCrIDmPcmqapwHkBluurjPQrFuBncDP2mbw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.UI.Tailwind.wasm",
        "name": "Luxel.UI.Tailwind.fxqefh04ko.wasm",
        "hash": "sha256-LS1jmZl9KS7gDV91KuuCDGVWnXTIkjuRxiM5EDm+xVA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.UI.wasm",
        "name": "Luxel.UI.yd4a0bjw4z.wasm",
        "hash": "sha256-tnBSRxYVWdo1+/cJnUWRMcmyMqMeeA5ARsxqOZAe2/M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Luxel.Workbench.wasm",
        "name": "Luxel.Workbench.m4rrisinxe.wasm",
        "hash": "sha256-FuQ6zRcnQzNDu20mH0LQmGt7OHnIjREXDYgXWKZ8u1U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "LuxelCavern.Core.wasm",
        "name": "LuxelCavern.Core.uh0w8c2rh0.wasm",
        "hash": "sha256-fp08Y0MG3Xbd1+iK/opz4bvmaG6jro1OenpQy8RNXPs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "LuxelRange.Core.wasm",
        "name": "LuxelRange.Core.ouw042itxd.wasm",
        "hash": "sha256-UNJt3EMrzJUxK6G3AxR//2DQat7OT6AukdvH+gW/mBs=",
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
        "name": "Microsoft.Extensions.DependencyInjection.857rzh5ynj.wasm",
        "hash": "sha256-FOyDUv5DRQHYlCHGAmFrB9n1CHi/59fx0qoucp62zGA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.DependencyInjection.Abstractions.wasm",
        "name": "Microsoft.Extensions.DependencyInjection.Abstractions.6quvtr059g.wasm",
        "hash": "sha256-sonyMgKujxzof7LfmERbaZfeCp4dH7mvvdJ45SEer0U=",
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
        "name": "Microsoft.Win32.Primitives.vnyyaw4u4h.wasm",
        "hash": "sha256-+23NSdEiFOh6hycd1m3TuwjAggEs4OPZu6Jf0QeNsOE=",
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
        "virtualPath": "System.Collections.Concurrent.wasm",
        "name": "System.Collections.Concurrent.y775zb9dzx.wasm",
        "hash": "sha256-Mm3Ox0yqf1LNaza9g4b6FZxMqPXlrHiV3bvn8CM9riI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Immutable.wasm",
        "name": "System.Collections.Immutable.iuq9y4sfi9.wasm",
        "hash": "sha256-1p2V4IFjbl7W1bPrleO38hh8JKksv6E2W6mLC9VFxtc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.NonGeneric.wasm",
        "name": "System.Collections.NonGeneric.9dw1jk651k.wasm",
        "hash": "sha256-40KgV7orBhXkyyqdpXMW/cwvWpIZgKivVl52xq10bRQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Specialized.wasm",
        "name": "System.Collections.Specialized.qfdou0405m.wasm",
        "hash": "sha256-+oe0xrUpgT/Zd/rEhVhR89iC4a8xEUw/dLr//a/VVe8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.wasm",
        "name": "System.Collections.akizlyj1k3.wasm",
        "hash": "sha256-RkrWkX811nvnOx02QiKuU9bnu5QyqfB4/cQHEtsIBdU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.Annotations.wasm",
        "name": "System.ComponentModel.Annotations.lxuub07h3g.wasm",
        "hash": "sha256-52yhOp6e2OrSFo2mRf6FbNxxYP2CwxAUCRX3hSjjD/Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.Primitives.wasm",
        "name": "System.ComponentModel.Primitives.ddp8x26991.wasm",
        "hash": "sha256-cduQodpoddgFcwBWchJixQIEmtRLkb9sHv9+shxW4XE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.TypeConverter.wasm",
        "name": "System.ComponentModel.TypeConverter.3bk92o30qt.wasm",
        "hash": "sha256-D6HkNk1KkvQMPJK+0fqWFiEAN0uj6mC3nxfpW35vxEM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.wasm",
        "name": "System.ComponentModel.371m1lo8v7.wasm",
        "hash": "sha256-cbtAN/vCdE/rCjcjrWs80jsrDNjOwncvgOCu0PoFt98=",
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
        "name": "System.Console.gmkalihz1i.wasm",
        "hash": "sha256-GOyit+DT0KWBSPkB14F4beKBDLbvJF+uOgFp+AJyIjA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.Common.wasm",
        "name": "System.Data.Common.bbmxgw490z.wasm",
        "hash": "sha256-yQCRmaOWZmrXhqnj9KJDhbQniGj52pZ55ko+cOOHab0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.DataSetExtensions.wasm",
        "name": "System.Data.DataSetExtensions.muwrqrhfbz.wasm",
        "hash": "sha256-8qp1+9175gOkivUD2uD5rLKP2YWnqJvwS2PSfHMz0j0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.DiagnosticSource.wasm",
        "name": "System.Diagnostics.DiagnosticSource.4bzbla18sd.wasm",
        "hash": "sha256-gDQOr2CbjXBVBSrc/dbBSEEMaJfxJyPo9JBKgh9RMsk=",
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
        "name": "System.Diagnostics.FileVersionInfo.0essyh86mx.wasm",
        "hash": "sha256-SDuX895E7aym5/KZfk3DnUhAX0CJK4xBJFzP6LphNVs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Process.wasm",
        "name": "System.Diagnostics.Process.9pgw304ubl.wasm",
        "hash": "sha256-c0AFlJRS+a3WE6M8+nPgDzMFl1SLqsXQCdDhtryl5vQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.StackTrace.wasm",
        "name": "System.Diagnostics.StackTrace.bj9yel8vbs.wasm",
        "hash": "sha256-ORkEeVx9W77yrQvYgXrlDot9DvTQBC7gJ1y5wuK1Y5I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.TextWriterTraceListener.wasm",
        "name": "System.Diagnostics.TextWriterTraceListener.ozl5xy2j8t.wasm",
        "hash": "sha256-lGdULzM1kBgwU5O0fp3i9X5iZoyA8DJDRi9Rax5datI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.TraceSource.wasm",
        "name": "System.Diagnostics.TraceSource.9wbtxyvanb.wasm",
        "hash": "sha256-edrQ7fkGnshJMpHHvSQ5SOPP99gnEkHBhMMfs3AhZRI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Tracing.wasm",
        "name": "System.Diagnostics.Tracing.jvl9hdnc3v.wasm",
        "hash": "sha256-AbSmQEUMxcydZ7uAh6K/VS+SdRr0rMIpqnFKf+zteLU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Globalization.wasm",
        "name": "System.Globalization.h12el1ujm5.wasm",
        "hash": "sha256-K4XUvDW+3wP3YbIJHMSVpwWAuwrtNKQt2Rq1o1VK7vI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.wasm",
        "name": "System.IO.Compression.e833adl38p.wasm",
        "hash": "sha256-4h7woTi6JrbdtUOxMkroMHU3wTDxFXdZ8pj295UaESg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.Watcher.wasm",
        "name": "System.IO.FileSystem.Watcher.6fvnlclqxu.wasm",
        "hash": "sha256-LkxtliiKpj2p9HkA4QP6MEQhVpsnS1UJwAa/Zd0wQ4M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.MemoryMappedFiles.wasm",
        "name": "System.IO.MemoryMappedFiles.6rdsrpv216.wasm",
        "hash": "sha256-XlylHCfMw53OgL8YEs0D6pIoTdLs6S+Jzh1qrTkg+/w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipelines.wasm",
        "name": "System.IO.Pipelines.arhr4f9dxs.wasm",
        "hash": "sha256-Wn2UzQQQqADnT4uEJjPzED4FMoQkIQTZ7igE6B25zLk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Expressions.wasm",
        "name": "System.Linq.Expressions.knmtyw3c33.wasm",
        "hash": "sha256-ENZXpb6xgabiRj8J1+vKfCzLr7fFH4pdQMyI/Fon7bM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.wasm",
        "name": "System.Linq.7drpmtnee8.wasm",
        "hash": "sha256-NU91QebOX7u9Z6veMAPeF793mgfysJK88Qs7y1zEEJ4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Memory.wasm",
        "name": "System.Memory.vpz01na5li.wasm",
        "hash": "sha256-LQ5GJTBjxfqkXtjiWQA6d23RI9f9iZPpiwpcWdD87/U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Http.wasm",
        "name": "System.Net.Http.nedyrotgae.wasm",
        "hash": "sha256-X6NmsVzgI1pupcklwspVONNZXWdvhJ3lrTOxahhBaCE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.HttpListener.wasm",
        "name": "System.Net.HttpListener.vf5qw9cfwq.wasm",
        "hash": "sha256-CwL5scl8NUwCxqiU7FMi9w3FPGhoVcoxB/ackHZEGWA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Primitives.wasm",
        "name": "System.Net.Primitives.rhxvbwwhsk.wasm",
        "hash": "sha256-sZlKyyfFzl6Ckp9x+brrVJqD4t9FpDCNSUe7ydTjMaE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Requests.wasm",
        "name": "System.Net.Requests.l25ant102g.wasm",
        "hash": "sha256-06z11jeSmWaGt8BCdOjygftQzRaHh0nh5bbfXLP7h7E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Sockets.wasm",
        "name": "System.Net.Sockets.n2bl14wzpj.wasm",
        "hash": "sha256-Txy9BGYFTWwxKps6Q1fUhOBdigyi4tHTxrdSyCKOGOU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebClient.wasm",
        "name": "System.Net.WebClient.std4buk9gj.wasm",
        "hash": "sha256-AD6a1BCA6Vr10D1F/XhwRLY6sf2qSQrb/5hY6sXpMfg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebHeaderCollection.wasm",
        "name": "System.Net.WebHeaderCollection.k1eyosw4ys.wasm",
        "hash": "sha256-mIPm3LD9w7J1vTwuvICujGmrfmUul9jhoz7yfUmUnxc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebSockets.wasm",
        "name": "System.Net.WebSockets.b02t149joo.wasm",
        "hash": "sha256-azCH2XipBLot2/O4njJAohkraHScODcpRgax/ooLcDU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Numerics.Vectors.wasm",
        "name": "System.Numerics.Vectors.e56mo67yph.wasm",
        "hash": "sha256-XlfurfQoeFqqRtOKfm9BPRrXxnCddAhOERLs034EyLc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ObjectModel.wasm",
        "name": "System.ObjectModel.rw80tgz8gg.wasm",
        "hash": "sha256-G9ryBc6TvrNS8ligBiFLjyK0Hg0Hb5Y3L8PyCc7HIDI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.DataContractSerialization.wasm",
        "name": "System.Private.DataContractSerialization.rfqqbjw1uo.wasm",
        "hash": "sha256-V1/VHoBiokJyzZMyPhkhfhMtHdzbFuobmENZppuNDxA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Uri.wasm",
        "name": "System.Private.Uri.2ahiy7k87n.wasm",
        "hash": "sha256-+kXh7sGJNwYIgPBqJCXPOlrdAqDv/qajRErEndErMSA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.Linq.wasm",
        "name": "System.Private.Xml.Linq.cruwtg6vjm.wasm",
        "hash": "sha256-SP8HpZO7q8XJBKsosJ+wBK62IIHWUGAC5dQYw/5yIww=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.wasm",
        "name": "System.Private.Xml.mpetuizh4r.wasm",
        "hash": "sha256-Rjq5KVtJ/s6o1/ivWNEXPjQrHhn87UfypizN5XmjVg4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.ILGeneration.wasm",
        "name": "System.Reflection.Emit.ILGeneration.89syf399xc.wasm",
        "hash": "sha256-dBFu/gSGVpqZcJLALS7Wjx6Q8f3khZc5eIsXxWGrbgA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.Lightweight.wasm",
        "name": "System.Reflection.Emit.Lightweight.58a52741nd.wasm",
        "hash": "sha256-BQE3T2HSFkpiLmt9Sq1b3ai9k1CWbsmRAk9D3IDp0lI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Metadata.wasm",
        "name": "System.Reflection.Metadata.a0v13b3ior.wasm",
        "hash": "sha256-OvIEawa4jpZE7+E2F6uRs0y4DBAmHoB4ecOPyTH7twg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Primitives.wasm",
        "name": "System.Reflection.Primitives.gg7by1tgyi.wasm",
        "hash": "sha256-gNDMx4txHgNIHHqkfExkH8Dd3jWxbSEhl7+RGWtr0Bg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.CompilerServices.Unsafe.wasm",
        "name": "System.Runtime.CompilerServices.Unsafe.qcdw6vqpki.wasm",
        "hash": "sha256-mo2y/v+zey8ez/bm9LFCX3trJwiFNnnfzdR0LIXvhuw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.wasm",
        "name": "System.Runtime.InteropServices.lhljg4ljg4.wasm",
        "hash": "sha256-iaNtCNyPcvYA0mCLKGClnwSm/GuvxPPTeSYdpNi1AW0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Intrinsics.wasm",
        "name": "System.Runtime.Intrinsics.rbypkd2j9s.wasm",
        "hash": "sha256-B0JDnpg2KSIbHMPxivlRTHtbsDkWd6mci4ZgLNiBlB8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Loader.wasm",
        "name": "System.Runtime.Loader.cwgnr2whf7.wasm",
        "hash": "sha256-tMdmJ/TE7eKQALNP1ymKsjCpZiakF6ziM9/k1Eg7Zj0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Numerics.wasm",
        "name": "System.Runtime.Numerics.tp6fyaqxux.wasm",
        "hash": "sha256-P4ZjD//ZC/rF6R4z8eCwhfPYrMmnR6KS5EAH+I+tAzw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Formatters.wasm",
        "name": "System.Runtime.Serialization.Formatters.ot86pd0m4x.wasm",
        "hash": "sha256-LP+w4ZWgfmJUa8CyCwK8W6rhRNvaqszpiJc9sD1UKjg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Json.wasm",
        "name": "System.Runtime.Serialization.Json.p9itnzyfx9.wasm",
        "hash": "sha256-vcRhv9UC6XYHG3k1U8fUXKcJajSlaQ7gwIP3pcDXCbM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Primitives.wasm",
        "name": "System.Runtime.Serialization.Primitives.pkr8skj39l.wasm",
        "hash": "sha256-mZnuesqsA4pof9Q3GR/bIAwLLcEuHN++DwPewaQkRdk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Xml.wasm",
        "name": "System.Runtime.Serialization.Xml.307x85krip.wasm",
        "hash": "sha256-TSJi2kLBtzb4IZHk5Agpszn1EtkTz2H4RtgiMx1+I7g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.wasm",
        "name": "System.Runtime.8p2xu101o5.wasm",
        "hash": "sha256-FLKiY9nzNmuMjGUVHNGIGe5IlzK3O5IgRsza2Uvg9h0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.wasm",
        "name": "System.Security.Cryptography.8j89hipejf.wasm",
        "hash": "sha256-jCOnZdi5U6ThvdxxSHojXDc1zcg6lYVJDBazvtsQ69A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.ProtectedData.wasm",
        "name": "System.Security.Cryptography.ProtectedData.2yj0iaqwy3.wasm",
        "hash": "sha256-BkHqtpEGgmL8T3LHQ3xBXuG30aVtho8kecNryUecI4k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Principal.Windows.wasm",
        "name": "System.Security.Principal.Windows.drbvib4pl7.wasm",
        "hash": "sha256-t9SlW1AjaRr50fgoh1nOedLbmJuHxZrEC7BqVhSSNaU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.CodePages.wasm",
        "name": "System.Text.Encoding.CodePages.uzfe8vi7di.wasm",
        "hash": "sha256-egw5iX5LbVY+YfKv9bZ84NLuh1MX9Ntq9yfujWCfJvQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.Extensions.wasm",
        "name": "System.Text.Encoding.Extensions.3ij9bf53n6.wasm",
        "hash": "sha256-BZ7a17h78GPxaSQQwOewVRetLoMus/RCXNom144XQsQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encodings.Web.wasm",
        "name": "System.Text.Encodings.Web.crgjoq9nle.wasm",
        "hash": "sha256-mOsV0Hp8sUIp+U3+Cs49SzCQ11zNoKGWRaGBoCsPTi4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Json.wasm",
        "name": "System.Text.Json.gsfsxw3e5h.wasm",
        "hash": "sha256-8C/UqJr9gXYhUS+PHFetp5++cZDzcuLyxdKbfS727ik=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.RegularExpressions.wasm",
        "name": "System.Text.RegularExpressions.dju7oizjxz.wasm",
        "hash": "sha256-WSZHDk4RzwN7mTvoavb3vfzztun0wjWDq0kDMF1gyZk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Channels.wasm",
        "name": "System.Threading.Channels.m0snz4nvrs.wasm",
        "hash": "sha256-lrDrpsQZytkyk/glgvCbTh9k/u8wK49xeWb4n2gUCIQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Parallel.wasm",
        "name": "System.Threading.Tasks.Parallel.v4kdp0vb8x.wasm",
        "hash": "sha256-LxD2OS4IPmRYeiOE+IJ6Sx1JeuYua/xEzs1dLA0ZFb4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Thread.wasm",
        "name": "System.Threading.Thread.odf0u1zo86.wasm",
        "hash": "sha256-JABPL37qWUGH++OAER4XyoyvHmszLmpT8XbaC33EkEk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.ThreadPool.wasm",
        "name": "System.Threading.ThreadPool.foghjsqkkr.wasm",
        "hash": "sha256-r/EhRleozEFSRkyM3eGNQghZZXYovLhe/JQdoHqoXNY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.wasm",
        "name": "System.Threading.nbswook2fg.wasm",
        "hash": "sha256-Y4+GFk+/RrQg0F+A1WwIT19gJYghdMf27lGTWgbPW7s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.Linq.wasm",
        "name": "System.Xml.Linq.224i7l5jfb.wasm",
        "hash": "sha256-CtCEVMVCGg9HoHtIFvGwAiLGJrg+KgImS+oc8purBE8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.ReaderWriter.wasm",
        "name": "System.Xml.ReaderWriter.buxk5iqizj.wasm",
        "hash": "sha256-U7U15nthzloKWY1oqsYBkVjnWPUHFZdzNa5qZbwQ1ys=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XDocument.wasm",
        "name": "System.Xml.XDocument.jkfqn660kl.wasm",
        "hash": "sha256-rX+9M4h/DLe1T8aXXgxZP8zESaPswCSXsvY6gV4KE+o=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XPath.XDocument.wasm",
        "name": "System.Xml.XPath.XDocument.p83v7ycmge.wasm",
        "hash": "sha256-125mSImv4VuRJNz9OxjzX+QmCqkXk4+MTlmCBNtiRtE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XPath.wasm",
        "name": "System.Xml.XPath.je52anz03g.wasm",
        "hash": "sha256-JUL3STlSWyHfncat4FfzpTdCNFwqOFCVL5bZsgOLZ0w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XmlSerializer.wasm",
        "name": "System.Xml.XmlSerializer.fdmjz57vy3.wasm",
        "hash": "sha256-A1+FgmjSWA4YhWp2A2yz1Rd5bOElX9jcblZqtOSpgcE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.wasm",
        "name": "System.1h5i8a3bq8.wasm",
        "hash": "sha256-y7NnWh8z8gPuKAQI4hhBL69y2e7NlYbHZDvyDbFBTHI=",
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
        "name": "netstandard.bxy7s0n55g.wasm",
        "hash": "sha256-j/g5WAJdy6oG+rMV49tO/nBwNi7NMi4B6DDnX0bYgoY=",
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
          "name": "Luxel.Resources.Gallery.resources.cwa19euxmh.wasm",
          "hash": "sha256-lFFPaWqs30C4NH/2to7o+Mu3uxbixOCZXcZeVlFwOoA=",
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
