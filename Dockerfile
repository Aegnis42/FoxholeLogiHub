# Dockerfile pour déployer FoxholeLogiHub.Api sur Railway (ou tout hébergeur Docker).
# Build multi-étapes : on ne compile que l'API + les contrats (pas le code Windows/WPF).

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore (copie d'abord les .csproj pour profiter du cache de couches)
COPY src/FoxholeLogiHub.Contracts/FoxholeLogiHub.Contracts.csproj src/FoxholeLogiHub.Contracts/
COPY src/FoxholeLogiHub.Api/FoxholeLogiHub.Api.csproj src/FoxholeLogiHub.Api/
RUN dotnet restore src/FoxholeLogiHub.Api/FoxholeLogiHub.Api.csproj

# Build + publish
COPY src/FoxholeLogiHub.Contracts/ src/FoxholeLogiHub.Contracts/
COPY src/FoxholeLogiHub.Api/ src/FoxholeLogiHub.Api/
RUN dotnet publish src/FoxholeLogiHub.Api/FoxholeLogiHub.Api.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Railway fournit la variable PORT ; l'app écoute sur 0.0.0.0:$PORT (cf. Program.cs).
ENTRYPOINT ["dotnet", "FoxholeLogiHub.Api.dll"]
