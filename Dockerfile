FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
COPY src/Basil.Domain/*.csproj src/Basil.Domain/
COPY src/Basil.Protocol/*.csproj src/Basil.Protocol/
COPY src/Basil.Application/*.csproj src/Basil.Application/
COPY src/Basil.Infrastructure/*.csproj src/Basil.Infrastructure/
COPY src/Basil.Web/*.csproj src/Basil.Web/
RUN dotnet restore src/Basil.Web/Basil.Web.csproj
COPY src/ ./src/
RUN dotnet publish src/Basil.Web/Basil.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=dotnet-build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Basil.Web.dll"]
