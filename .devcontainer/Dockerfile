FROM mcr.microsoft.com/dotnet/sdk:10.0-noble

LABEL org.opencontainers.image.source="https://github.com/ikihiki/luxel" \
      org.opencontainers.image.description="Luxel .NET 10 development environment with a remote desktop and Vulkan tooling"

ARG USERNAME=vscode
ARG USER_UID=1000
ARG USER_GID=${USER_UID}

ENV DEBIAN_FRONTEND=noninteractive

RUN rm -f /etc/apt/sources.list.d/yarn.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        binutils \
        build-essential \
        ca-certificates \
        clang \
        cmake \
        curl \
        file \
        fonts-dejavu-core \
        fonts-noto-cjk \
        git \
        gnupg2 \
        jq \
        less \
        grim \
        libasound2t64 \
        libatk-bridge2.0-0t64 \
        libatk1.0-0t64 \
        libatspi2.0-0t64 \
        libcairo2 \
        libcups2t64 \
        libdbus-1-3 \
        libdecor-0-0 \
        libgl1 \
        libdrm2 \
        libgbm1 \
        libglib2.0-0t64 \
        libnspr4 \
        libnss3 \
        libopenal-dev \
        libopenal1 \
        libpango-1.0-0 \
        libvulkan1 \
        libwayland-client0 \
        libwayland-cursor0 \
        libwayland-egl1 \
        libx11-6 \
        libxcb1 \
        libxcomposite1 \
        libxdamage1 \
        libxext6 \
        libxfixes3 \
        libxi6 \
        libxkbcommon0 \
        libxrandr2 \
        mesa-vulkan-drivers \
        ninja-build \
        novnc \
        openssh-client \
        pkg-config \
        procps \
        pulseaudio \
        pulseaudio-utils \
        python3 \
        python3-pip \
        sudo \
        sway \
        unzip \
        vulkan-tools \
        wayland-utils \
        wayvnc \
        websockify \
        wlr-randr \
        zip \
        zlib1g-dev \
    && if id -u ubuntu >/dev/null 2>&1; then \
         usermod --login "${USERNAME}" --home "/home/${USERNAME}" --move-home ubuntu; \
         groupmod --new-name "${USERNAME}" ubuntu; \
       else \
         groupadd --gid "${USER_GID}" "${USERNAME}"; \
         useradd --uid "${USER_UID}" --gid "${USER_GID}" --create-home --shell /bin/bash "${USERNAME}"; \
       fi \
    && echo "${USERNAME} ALL=(root) NOPASSWD:ALL" > "/etc/sudoers.d/${USERNAME}" \
    && chmod 0440 "/etc/sudoers.d/${USERNAME}" \
    && rm -rf /var/lib/apt/lists/*

RUN dotnet workload install wasm-tools

# Install the browser tooling independently from application and test projects so this layer is
# invalidated only when the development image changes its Playwright version.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
COPY .devcontainer/Playwright.csproj /tmp/luxel-playwright/Playwright.csproj
# Browser runtime libraries are declared above. Avoid Playwright's --with-deps helper because it
# installs Xvfb, which would reintroduce an X server into the native-Wayland development image.
RUN dotnet tool install PowerShell --tool-path /usr/local/bin --version 7.6.4 \
    && dotnet build /tmp/luxel-playwright/Playwright.csproj --configuration Release \
    && pwsh /tmp/luxel-playwright/bin/Release/net10.0/playwright.ps1 install chromium \
    && chown -R "${USERNAME}:${USERNAME}" "${PLAYWRIGHT_BROWSERS_PATH}" \
    && rm -rf /tmp/luxel-playwright /root/.nuget/packages /var/lib/apt/lists/*

COPY eng/desktop /opt/luxel/desktop
RUN sed -i 's/\r$//' /opt/luxel/desktop/*.sh \
    && chmod -R a+rX /opt/luxel/desktop \
    && find /opt/luxel/desktop -maxdepth 1 -name '*.sh' -exec chmod a+x {} +

ENV LUXEL_DESKTOP_RENDERER=auto \
    LUXEL_REQUIRE_HARDWARE_VULKAN=1 \
    LUXEL_VULKAN_VENDOR_ID=0x8086 \
    LUXEL_DESKTOP_AUDIO=off \
    LUXEL_NOVNC_PORT=6080

EXPOSE 6080
