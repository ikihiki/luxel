# 出荷検証 (ToDo 27 GE-6) — capstone チェックリスト踏襲:
#   ① Luxel.Player.App --ship (= PlayerShipper: dotnet publish self-contained + project/ コピー)
#   ② リポジトリ外 (%TEMP%) から vk/dx とも --frames 60 で exit 0
# 使い方: .\ship-verify.ps1 [プロジェクトフォルダ (既定 samples/PlayerDemo)]
param([string]$Project = "samples/PlayerDemo")
$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
$out = Join-Path $env:TEMP "luxel-ship-verify"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

Write-Host "== ship: $Project -> $out"
Push-Location $repo
try { dotnet run --project src/Framework/Luxel.Player.App -- --ship $Project $out; if ($LASTEXITCODE -ne 0) { throw "ship 失敗 ($LASTEXITCODE)" } }
finally { Pop-Location }

foreach ($backend in @("vk", "dx")) {
    Write-Host "== smoke ($backend, リポジトリ外 cwd=$out)"
    Push-Location $out   # cwd をリポジトリ外へ — 相対パス/shaders 同梱の穴を検出する
    try {
        & (Join-Path $out "Luxel.Player.App.exe") $backend --frames 60
        if ($LASTEXITCODE -ne 0) { throw "smoke $backend 失敗 ($LASTEXITCODE)" }
    }
    finally { Pop-Location }
}
Write-Host "== ship-verify OK (vk/dx exit 0)"
