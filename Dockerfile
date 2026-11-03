# Salvarr: build the app, then run it on the ASP.NET image with ffprobe and mediainfo.
ARG FFMPEG_VERSION=8.1

# Static ffprobe from BtbN (linked from ffmpeg.org), the same build Muxarr uses.
FROM debian:13-slim AS ffprobe
ARG FFMPEG_VERSION
ARG TARGETARCH
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates xz-utils \
    && rm -rf /var/lib/apt/lists/*
RUN set -eux; \
    case "${TARGETARCH:-amd64}" in \
        amd64) arch=linux64 ;; \
        arm64) arch=linuxarm64 ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    curl -fsSL "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n${FFMPEG_VERSION}-latest-${arch}-gpl-${FFMPEG_VERSION}.tar.xz" -o /tmp/ffmpeg.tar.xz; \
    mkdir -p /out; \
    tar -xJf /tmp/ffmpeg.tar.xz -C /out --strip-components=2 --wildcards '*/bin/ffprobe'; \
    /out/ffprobe -version

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/Salvarr/Salvarr.csproj src/Salvarr/
RUN dotnet restore src/Salvarr/Salvarr.csproj
COPY src/ src/
RUN dotnet publish src/Salvarr/Salvarr.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends mediainfo tzdata \
    && rm -rf /var/lib/apt/lists/*
COPY --from=ffprobe /out/ffprobe /usr/local/bin/ffprobe

WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_HTTP_PORTS=8184 \
    TZ=Etc/UTC
EXPOSE 8184

# Read-only: Salvarr never writes to the library. Compose can pick the user (user: 1000:1000).
USER $APP_UID
ENTRYPOINT ["dotnet", "Salvarr.dll"]
