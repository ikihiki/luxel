# Luxel Range — capstone ②（3D射的）

Luxelの初心者向けRenderingガイドを終えた後に進む、Windows向けstandalone 3D capstoneです。実ウィンドウとsurfaceへの提示だけでなく、ECS、terrain、camera、物理、静的/skin付き3D描画、glTF animation、billboard particle、入力、設定、audioを1本のアプリで接続します。

Galleryでは `Apps/Game/Range`、基礎となる静的1モデルの経路は `Learn/Graphics/ThreeD/StaticGltf`、配布手順は `Learn/Graphics/ThreeD/Shipping` を参照してください。

## Project構成

- **`LuxelRange.Core`**: GPUやwindowに依存しないゲーム状態とsimulation。射撃、target、terrain、score、settings、events、audio cueを所有し、Gallery storyと単体テストからも利用します。
- **`LuxelRange`**: Win32 window、Vulkan/D3D12 device、`GpuSurface`、入力、実時間`GameLoop`を接続する薄い実行層です。`RangeRealtimeScene`がterrain、Fox glTF、particles、framebufferを描画します。

## 実行

リポジトリrootから実行します。

```powershell
dotnet run --project samples/LuxelRange/LuxelRange -- vk
dotnet run --project samples/LuxelRange/LuxelRange -- dx
```

CIやsmokeでは有限frameで自動終了できます。

```powershell
dotnet run --project samples/LuxelRange/LuxelRange -- vk --frames 60
dotnet run --project samples/LuxelRange/LuxelRange -- dx --frames 60
```

`vk`はVulkan、`dx`はD3D12です。どちらもWindows x64と対応GPU driverが必要です。

## 3D assetとshader

build時にKhronos glTF Sample Assetsの`Fox.glb`とlicenseを固定commitから取得し、SHA-256を検証します。出力とpublishには次が含まれます。

```text
LuxelRange.exe
assets/Fox.glb
licenses/Fox-LICENSE.md
shaders/scene_pbr_lite.spv
shaders/scene_pbr_lite.vs.dxil
shaders/scene_pbr_lite.ps.dxil
shaders/scene_pbr_skinned.spv
shaders/scene_pbr_skinned.vs.dxil
shaders/scene_pbr_skinned.ps.dxil
```

runtimeは次のように**executableの場所**を基準に解決します。processのcurrent working directoryには依存しません。

```csharp
string fox = Path.Combine(AppContext.BaseDirectory, "assets", "Fox.glb");
```

静的モデルの`file → AssetDocument → AssetPrimitive → GPU buffers → one draw`を先に理解したい場合はGalleryの`Learn/Graphics/ThreeD/StaticGltf`から始めてください。Rangeではその先のECS scene、animation、skinningまで使用します。

## Publish

self-containedのfolder配布例です。

```powershell
dotnet publish samples/LuxelRange/LuxelRange `
  -c Release -r win-x64 --self-contained `
  -o publish/LuxelRange
```

publish後は最低限、次を確認します。

```powershell
@(
  "LuxelRange.exe",
  "assets/Fox.glb",
  "licenses/Fox-LICENSE.md",
  "shaders/scene_pbr_lite.spv",
  "shaders/scene_pbr_skinned.spv"
) | ForEach-Object {
  if (-not (Test-Path (Join-Path "publish/LuxelRange" $_))) { throw "missing: $_" }
}
```

リポジトリ外の別cwdからの起動を含む自動確認はrootの`rendering-ship-verify.ps1 -IncludeRange`を使います。exit codeだけではasset同梱を証明できないため、このscriptはshader、Fox、licenseの存在を起動前に検査します。

## 保存先と終了

high scoreなどのユーザー書込データは`%APPDATA%\LuxelRange\`へ保存し、publish directoryやrepositoryには書き込みません。終了時はhostを停止し、GPU queueの使用が終わってからscene resource、surface、deviceを破棄します。
