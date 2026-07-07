FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
COPY src/OpenOsuTournament.Bancho.Domain/*.csproj src/OpenOsuTournament.Bancho.Domain/
COPY src/OpenOsuTournament.Bancho.Protocol/*.csproj src/OpenOsuTournament.Bancho.Protocol/
COPY src/OpenOsuTournament.Bancho.Application/*.csproj src/OpenOsuTournament.Bancho.Application/
COPY src/OpenOsuTournament.Bancho.Infrastructure/*.csproj src/OpenOsuTournament.Bancho.Infrastructure/
COPY src/OpenOsuTournament.Bancho.Web/*.csproj src/OpenOsuTournament.Bancho.Web/
RUN dotnet restore src/OpenOsuTournament.Bancho.Web/OpenOsuTournament.Bancho.Web.csproj
COPY src/ ./src/
RUN dotnet publish src/OpenOsuTournament.Bancho.Web/OpenOsuTournament.Bancho.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=dotnet-build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OpenOsuTournament.Bancho.Web.dll"]
