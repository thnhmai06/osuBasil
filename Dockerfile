FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN rid=$([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64) \
    && dotnet publish Basil -c Release -r linux-musl-$rid --self-contained true -p:DebugType=none \
        -p:GenerateDocumentationFile=false -o /app/publish \
    && find /app/publish \( -name '*.pdb' -o -name '*.xml' -o -name 'createdump' \) -delete

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine
# The Alpine base turns globalization off and carries no ICU or time zone data; Basil needs cultures, region names
# and the TZ setting. The database driver loads the Kerberos library.
RUN apk add --no-cache ffmpeg curl icu-libs icu-data-full tzdata krb5-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app
COPY --from=build /app/publish .
# Basil runs as the image's non-root user and may write only Data, Cache, Logs and Temp; nginx's read-only mounts of
# Data and Cache serve the files. The shipped files of Data are in Defaults/Data and are copied into Data where missing.
RUN mkdir -p Data Cache Logs Temp && chown app:app Data Cache Logs Temp
USER app

# Plain HTTP and plain IRC, reachable only inside the compose network; nginx in front ends TLS.
EXPOSE 8080
EXPOSE 6667

HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -sf -H "Host: api.${Basil__Server__Domain:-basil.local}" http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["./Basil"]
