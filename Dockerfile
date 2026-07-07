# Builds bancho.py's native pp/difficulty engine (Rust cdylib) and the .NET app, then assembles
# a runtime image with both. Debian bookworm is used for the Rust build stage to match glibc with
# the official aspnet runtime image (also bookworm-based) — the compiled .so must be ABI-compatible
# with the container it actually runs in.

FROM rust:1-bookworm AS native-build
WORKDIR /src/native/bancho-pp-ffi
COPY native/bancho-pp-ffi/Cargo.toml native/bancho-pp-ffi/Cargo.lock ./
COPY native/bancho-pp-ffi/src/ ./src/
RUN cargo build --release

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
COPY src/Bancho.Domain/*.csproj src/Bancho.Domain/
COPY src/Bancho.Protocol/*.csproj src/Bancho.Protocol/
COPY src/Bancho.Application/*.csproj src/Bancho.Application/
COPY src/Bancho.Infrastructure/*.csproj src/Bancho.Infrastructure/
COPY src/Bancho.Web/*.csproj src/Bancho.Web/
RUN dotnet restore src/Bancho.Web/Bancho.Web.csproj
COPY src/ ./src/
RUN dotnet publish src/Bancho.Web/Bancho.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=dotnet-build /app/publish .
COPY --from=native-build /src/native/bancho-pp-ffi/target/release/libbancho_pp_ffi.so ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bancho.Web.dll"]
