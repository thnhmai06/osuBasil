FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Basil.Web -c Release -r linux-x64 --self-contained true \
    -p:OpenApiGenerateDocumentsOnBuild=false -o /app/publish

# runtime-deps (not the full runtime image) is enough for a self-contained publish — it bundles
# its own .NET runtime, it only needs the native OS libraries (libicu, libssl, ...) this image provides.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# Data/, Logs/, and Settings.toml are all created/read next to the executable at runtime — see
# docs/run-deployment.md. Mount volumes over them (docker-compose.yml does this) so they survive
# a container recreate.
EXPOSE 443

ENTRYPOINT ["./Basil.Web"]
