---
display_name: Luxel GPU Development
description: Prebuilt .NET 10 workspace with an authenticated noVNC desktop and Intel Vulkan
icon: /emojis/1f5a5-fe0f.png
maintainer_github: ikihiki
tags: [kubernetes, dotnet, vulkan, intel-gpu]
---

# Luxel GPU development workspace

This template follows the existing **Kubernetes Dev Container (mux)** GPU setup. It runs
`ghcr.io/ikihiki/luxel-devcontainer:latest`, clones Luxel into `/home/vscode/luxel`, and exposes
the noVNC desktop as an owner-only Coder app.

The Pod requests one `gpu.intel.com/i915` extended resource from the Intel GPU Device Plugin.
The plugin assigns the GPU and injects the required `/dev/dri` device nodes. The template does
not mount `/dev` or `/dev/dri` itself and does not depend on a host render-group ID.

## Cluster prerequisites

- The Kubernetes provisioner used by the existing Coder templates.
- Intel GPU Device Plugin nodes advertising `gpu.intel.com/i915`.
- A default StorageClass, or an environment-specific StorageClass added to the PVC.
- A public GHCR package, or an image pull secret in the namespace.

The node may expose llvmpipe as well as the Intel GPU. Workspace startup requires an Intel Vulkan
device with vendor ID `0x8086`, stores its Vulkan device index, and passes that index to `vkcube`.
Detecting any CPU Vulkan implementation is not treated as proof that hardware works.

## Relation to the existing template

The existing **Kubernetes Dev Container (mux)** template already offers a **Shared Intel GPU
(development only)** preset using the same `gpu.intel.com/i915` resource. This Luxel template is
a project-specific variant with the Luxel image, repository clone, noVNC app, and strict Vulkan
startup check. It is intentionally published as a separate template; the existing template and
its presets remain unchanged.

## Publish the template

From this directory, while authenticated to the target Coder deployment:

```sh
coder templates push luxel
```

`.github/workflows/publish-devcontainer.yml` publishes `latest` and the Git commit SHA. For strict
rollouts, use the commit tag or a published `sha256:` digest.

## Runtime behavior

- The home PVC persists while the workspace Pod is recreated on stop/start.
- noVNC listens on loopback and is reachable only through the owner-only Coder app.
- Kubernetes assigns GPU device files; there is no privileged Pod or host-device mount.
- CI and local Dev Containers use Mesa lavapipe unless hardware passthrough is configured.
- Audio remains disabled by default.
