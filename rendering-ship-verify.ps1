# Rendering tutorial publish verification:
# - publish outside the repository
# - assert shader/assets structurally
# - launch the absolute executable path from a different empty cwd
param(
    [switch]$IncludeRange,
    [int]$Frames = 3
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
$root = Join-Path $env:TEMP "luxel-rendering-ship-verify"
$publish = Join-Path $root "triangle-publish"
$cwd = Join-Path $root "empty-cwd"

if (Test-Path $root) { Remove-Item $root -Recurse -Force }
New-Item $publish,$cwd -ItemType Directory | Out-Null

function Require-File([string]$Root, [string]$Relative) {
    $path = Join-Path $Root $Relative
    if (-not (Test-Path $path -PathType Leaf)) { throw "publish output missing: $path" }
}

function Invoke-Smoke([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory) {
    Write-Host "== smoke: $Executable $Arguments (cwd=$WorkingDirectory)"
    Push-Location $WorkingDirectory
    try {
        & $Executable @Arguments
        if ($LASTEXITCODE -ne 0) { throw "smoke failed ($LASTEXITCODE): $Executable $Arguments" }
    }
    finally { Pop-Location }
}

Push-Location $repo
try {
    Write-Host "== publish LuxelTriangle -> $publish"
    dotnet publish samples/LuxelTriangle/LuxelTriangle.csproj -c Release -o $publish
    if ($LASTEXITCODE -ne 0) { throw "LuxelTriangle publish failed ($LASTEXITCODE)" }
}
finally { Pop-Location }

@(
    "shaders/tutorial_triangle.spv",
    "shaders/tutorial_3d.spv",
    "shaders/compute_tutorial_postprocess.spv"
) | ForEach-Object { Require-File $publish $_ }

$triangle = Join-Path $publish $(if ($IsWindows) { "LuxelTriangle.exe" } else { "LuxelTriangle" })
Require-File $publish $(Split-Path $triangle -Leaf)
Invoke-Smoke $triangle @("vk", "--stage", "post", "--frames", "$Frames") $cwd
if ($IsWindows) { Invoke-Smoke $triangle @("dx", "--stage", "post", "--frames", "$Frames") $cwd }

if ($IncludeRange) {
    if (-not $IsWindows) { throw "LuxelRange publish smoke is Windows-only." }
    $rangePublish = Join-Path $root "range-publish"
    New-Item $rangePublish -ItemType Directory | Out-Null
    Push-Location $repo
    try {
        Write-Host "== publish LuxelRange -> $rangePublish"
        dotnet publish samples/LuxelRange/LuxelRange -c Release -r win-x64 --self-contained -o $rangePublish
        if ($LASTEXITCODE -ne 0) { throw "LuxelRange publish failed ($LASTEXITCODE)" }
    }
    finally { Pop-Location }

    @(
        "LuxelRange.exe",
        "assets/Fox.glb",
        "licenses/Fox-LICENSE.md",
        "shaders/scene_pbr_lite.spv",
        "shaders/scene_pbr_lite.vs.dxil",
        "shaders/scene_pbr_lite.ps.dxil",
        "shaders/scene_pbr_skinned.spv",
        "shaders/scene_pbr_skinned.vs.dxil",
        "shaders/scene_pbr_skinned.ps.dxil"
    ) | ForEach-Object { Require-File $rangePublish $_ }

    $range = Join-Path $rangePublish "LuxelRange.exe"
    Invoke-Smoke $range @("vk", "--frames", "60") $cwd
    Invoke-Smoke $range @("dx", "--frames", "60") $cwd
}

Write-Host "== rendering-ship-verify OK"
