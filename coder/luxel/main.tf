terraform {
  required_version = ">= 1.3"
  required_providers {
    coder      = { source = "coder/coder", version = "~> 2.0" }
    kubernetes = { source = "hashicorp/kubernetes", version = "~> 2.38" }
  }
}

data "coder_provisioner" "me" {}
data "coder_workspace" "me" {}
data "coder_workspace_owner" "me" {}

data "coder_parameter" "image" {
  name         = "image"
  display_name = "Dev Container image"
  description  = "Prebuilt Luxel development image. Use an immutable digest for reproducibility."
  type         = "string"
  default      = "ghcr.io/ikihiki/luxel-devcontainer:latest"
  mutable      = true
  order        = 1
}

data "coder_parameter" "namespace" {
  name         = "namespace"
  display_name = "Kubernetes namespace"
  description  = "Namespace used by the existing Kubernetes Coder provisioner."
  type         = "string"
  default      = "coder"
  mutable      = false
  order        = 2
}

resource "coder_agent" "main" {
  arch = data.coder_provisioner.me.arch
  os   = "linux"

  startup_script = <<-EOT
    set -eu
    export LUXEL_DESKTOP_RENDERER=hardware
    export LUXEL_REQUIRE_HARDWARE_VULKAN=1
    export LUXEL_VULKAN_VENDOR_ID=0x8086
    export LUXEL_DESKTOP_AUDIO=off
    /opt/luxel/desktop/start.sh
    /opt/luxel/desktop/healthcheck.sh
    /opt/luxel/desktop/run-vkcube.sh 120
  EOT

  env = {
    GIT_AUTHOR_NAME               = coalesce(data.coder_workspace_owner.me.full_name, data.coder_workspace_owner.me.name)
    GIT_AUTHOR_EMAIL              = data.coder_workspace_owner.me.email
    GIT_COMMITTER_NAME            = coalesce(data.coder_workspace_owner.me.full_name, data.coder_workspace_owner.me.name)
    GIT_COMMITTER_EMAIL           = data.coder_workspace_owner.me.email
    LUXEL_DESKTOP_RENDERER        = "hardware"
    LUXEL_REQUIRE_HARDWARE_VULKAN = "1"
    LUXEL_VULKAN_VENDOR_ID        = "0x8086"
    LUXEL_DESKTOP_AUDIO           = "off"
  }

  metadata {
    display_name = "CPU Usage"
    key          = "0_cpu_usage"
    script       = "coder stat cpu"
    interval     = 10
    timeout      = 1
  }
  metadata {
    display_name = "RAM Usage"
    key          = "1_ram_usage"
    script       = "coder stat mem"
    interval     = 10
    timeout      = 1
  }
  metadata {
    display_name = "Home Disk"
    key          = "2_home_disk"
    script       = "coder stat disk --path $${HOME}"
    interval     = 60
    timeout      = 1
  }
  metadata {
    display_name = "Vulkan GPU"
    key          = "3_vulkan_gpu"
    script       = <<-EOT
      source /opt/luxel/desktop/common.sh
      gpu_index="$(cat "$${STATE_DIR}/vulkan-gpu.index" 2>/dev/null || true)"
      if [ -n "$${gpu_index}" ]; then
        vulkan_device_name "$${LOG_DIR}/vulkaninfo.log" "$${gpu_index}"
      else
        printf '%s\n' "hardware device unavailable"
      fi
    EOT
    interval     = 60
    timeout      = 5
  }
}

module "git-clone" {
  count    = data.coder_workspace.me.start_count
  source   = "registry.coder.com/coder/git-clone/coder"
  version  = "2.0.2"
  agent_id = coder_agent.main.id
  url      = "https://github.com/ikihiki/luxel.git"
  base_dir = "/home/vscode"
}

module "code-server" {
  count      = data.coder_workspace.me.start_count
  source     = "registry.coder.com/coder/code-server/coder"
  version    = "1.5.2"
  agent_id   = coder_agent.main.id
  order      = 1
  folder     = "/home/vscode/luxel"
  depends_on = [module.git-clone]
}

resource "coder_app" "desktop" {
  agent_id     = coder_agent.main.id
  slug         = "luxel-desktop"
  display_name = "Luxel Desktop"
  icon         = "/emojis/1f5a5-fe0f.png"
  url          = "http://localhost:6080/vnc.html?autoconnect=1&resize=scale"
  share        = "owner"
  subdomain    = true
  order        = 2
  healthcheck {
    url       = "http://localhost:6080/vnc.html"
    interval  = 5
    threshold = 30
  }
}

resource "kubernetes_persistent_volume_claim" "home" {
  metadata {
    name      = "coder-${data.coder_workspace.me.id}-home"
    namespace = data.coder_parameter.namespace.value
    labels = {
      "app.kubernetes.io/name"     = "coder-workspace"
      "app.kubernetes.io/instance" = data.coder_workspace.me.id
      "com.coder.resource"         = "true"
      "com.coder.owner"            = data.coder_workspace_owner.me.name
      "com.coder.owner.id"         = data.coder_workspace_owner.me.id
      "com.coder.workspace.id"     = data.coder_workspace.me.id
    }
  }
  spec {
    access_modes = ["ReadWriteOnce"]
    resources { requests = { storage = "20Gi" } }
  }
  lifecycle { ignore_changes = all }
}

resource "kubernetes_pod" "workspace" {
  count = data.coder_workspace.me.start_count
  metadata {
    name      = "coder-${data.coder_workspace.me.id}"
    namespace = data.coder_parameter.namespace.value
    labels = {
      "app.kubernetes.io/name"     = "coder-workspace"
      "app.kubernetes.io/instance" = data.coder_workspace.me.id
      "com.coder.resource"         = "true"
      "com.coder.owner"            = data.coder_workspace_owner.me.name
      "com.coder.owner.id"         = data.coder_workspace_owner.me.id
      "com.coder.workspace.id"     = data.coder_workspace.me.id
    }
  }
  spec {
    security_context {
      run_as_user     = 1000
      run_as_group    = 1000
      fs_group        = 1000
      run_as_non_root = true
    }
    container {
      name              = "dev"
      image             = data.coder_parameter.image.value
      image_pull_policy = "Always"
      command           = ["sh", "-c", coder_agent.main.init_script]
      working_dir       = "/home/vscode"
      env {
        name  = "CODER_AGENT_TOKEN"
        value = coder_agent.main.token
      }
      env {
        name  = "LUXEL_DESKTOP_RENDERER"
        value = "hardware"
      }
      env {
        name  = "LUXEL_REQUIRE_HARDWARE_VULKAN"
        value = "1"
      }
      env {
        name  = "LUXEL_VULKAN_VENDOR_ID"
        value = "0x8086"
      }
      env {
        name  = "LUXEL_DESKTOP_AUDIO"
        value = "off"
      }
      resources {
        requests = {
          cpu                  = "2"
          memory               = "4Gi"
          "gpu.intel.com/i915" = "1"
        }
        limits = {
          cpu                  = "4"
          memory               = "8Gi"
          "gpu.intel.com/i915" = "1"
        }
      }
      volume_mount {
        name       = "home"
        mount_path = "/home/vscode"
      }
    }
    volume {
      name = "home"
      persistent_volume_claim { claim_name = kubernetes_persistent_volume_claim.home.metadata[0].name }
    }
  }
}
