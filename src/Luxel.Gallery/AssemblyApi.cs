// 型 API リファレンスの対象 (Docs/Api* が TypeApiRegistry から実行時にページを組み立てる)。
// 追加したい名前空間はここに並べる — 対象アセンブリは GenerateDocumentationFile=true が必要
// (Directory.Build.props で全プロジェクト有効)。
using Luxel.UI;

// コア / GPU 抽象 + バックエンド
[assembly: GenerateAssemblyApi("Luxel")]
[assembly: GenerateAssemblyApi("Luxel.Diagnostics")]
[assembly: GenerateAssemblyApi("Luxel.Graphics")]
[assembly: GenerateAssemblyApi("Luxel.Graphics.Abstraction")]
[assembly: GenerateAssemblyApi("Luxel.Graphics.Vulkan")]
[assembly: GenerateAssemblyApi("Luxel.Graphics.DirectX12")]
// 2D
[assembly: GenerateAssemblyApi("Luxel.TwoD")]
// UI
[assembly: GenerateAssemblyApi("Luxel.UI")]
[assembly: GenerateAssemblyApi("Luxel.UI.Styling")]
[assembly: GenerateAssemblyApi("Luxel.UI.Tailwind")]
// テキスト / ドキュメント
[assembly: GenerateAssemblyApi("Luxel.Typography")]
[assembly: GenerateAssemblyApi("Luxel.Document")]
[assembly: GenerateAssemblyApi("Luxel.Highlight")]
[assembly: GenerateAssemblyApi("Luxel.Diagram")]
[assembly: GenerateAssemblyApi("Luxel.MathText")]
// アニメーション
[assembly: GenerateAssemblyApi("Luxel.Animation")]
[assembly: GenerateAssemblyApi("Luxel.Animation.UI")]
[assembly: GenerateAssemblyApi("Luxel.Animation.TwoD")]
[assembly: GenerateAssemblyApi("Luxel.Animation.ThreeD")]
// 3D / レンダーグラフ / アセット
[assembly: GenerateAssemblyApi("Luxel.RenderGraph")]
[assembly: GenerateAssemblyApi("Luxel.Ecs")]
[assembly: GenerateAssemblyApi("Luxel.Ecs.Signal")]
[assembly: GenerateAssemblyApi("Luxel.Physics")]
[assembly: GenerateAssemblyApi("Luxel.Assets")]
[assembly: GenerateAssemblyApi("Luxel.AssetsGpu")]
[assembly: GenerateAssemblyApi("Luxel.AssetRuntime")]
[assembly: GenerateAssemblyApi("Luxel.Gltf")]
// ランタイム
[assembly: GenerateAssemblyApi("Luxel.Resources")]
[assembly: GenerateAssemblyApi("Luxel.Imaging")]
[assembly: GenerateAssemblyApi("Luxel.Input")]
[assembly: GenerateAssemblyApi("Luxel.Audio")]
[assembly: GenerateAssemblyApi("Luxel.Audio.Windows")]
[assembly: GenerateAssemblyApi("Luxel.Platform")]
[assembly: GenerateAssemblyApi("Luxel.Platform.Abstraction")]
[assembly: GenerateAssemblyApi("Luxel.Platform.Windows")]
[assembly: GenerateAssemblyApi("Luxel.Platform.Silk")]
[assembly: GenerateAssemblyApi("Luxel.Framework")]
[assembly: GenerateAssemblyApi("Luxel.Scene.UI")]
[assembly: GenerateAssemblyApi("Luxel.DevTools")]
