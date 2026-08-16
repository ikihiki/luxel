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
        dbus-x11 \
        file \
        fonts-dejavu-core \
        fonts-noto-cjk \
        git \
        gnupg2 \
        jq \
        less \
        libgl1 \
        libglx0 \
        libopenal-dev \
        libopenal1 \
        libvulkan1 \
        libx11-6 \
        libxcursor1 \
        libxi6 \
        libxinerama1 \
        libxrandr2 \
        mesa-vulkan-drivers \
        ninja-build \
        novnc \
        openbox \
        openssh-client \
        pkg-config \
        procps \
        pulseaudio \
        pulseaudio-utils \
        python3 \
        python3-pip \
        scrot \
        sudo \
        unzip \
        vulkan-tools \
        websockify \
        x11-utils \
        x11-xserver-utils \
        x11vnc \
        xauth \
        xdotool \
        xvfb \
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

COPY eng/desktop /opt/luxel/desktop
RUN sed -i 's/\r$//' /opt/luxel/desktop/*.sh \
    && chmod -R a+rX /opt/luxel/desktop \
    && find /opt/luxel/desktop -maxdepth 1 -name '*.sh' -exec chmod a+x {} +

ENV LUXEL_DESKTOP_RENDERER=lavapipe \
    LUXEL_DESKTOP_AUDIO=off \
    LUXEL_NOVNC_PORT=6080

EXPOSE 6080
