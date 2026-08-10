FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Basil.Web -c Release -r linux-x64 --self-contained true \
    -p:OpenApiGenerateDocumentsOnBuild=true -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 443
EXPOSE 6667

ENTRYPOINT ["./Basil.Web"]
