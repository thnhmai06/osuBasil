FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN rid=$([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64) \
    && dotnet publish Basil -c Release -r linux-$rid --self-contained true -p:DebugType=none \
        -p:GenerateDocumentationFile=false -o /app/publish \
    && find /app/publish \( -name '*.pdb' -o -name '*.xml' -o -name 'createdump' \) -delete

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .
# Basil runs as the image's non-root user and may write only its data and logs; nginx's read-only mount serves the files.
RUN mkdir -p Data Logs && chown app:app Data Logs
USER app

# Plain HTTP and plain IRC, reachable only inside the compose network; nginx in front ends TLS.
EXPOSE 8080
EXPOSE 6667

HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -sf -H "Host: api.${Basil__Server__Domain:-basil.local}" http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["./Basil"]
