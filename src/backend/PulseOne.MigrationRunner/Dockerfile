# PulseOne MigrationRunner — one-shot EF Core migration job (multi-stage build).
# Run as an Azure Container Apps Job (NOT a long-running app). The api-producer-deploy workflow
# starts this job and blocks on its execution status before shifting API traffic — migrations
# NEVER run at app startup (CLAUDE.md / global-context constraint #6). Build context is the backend
# solution root (src/backend).
#
# Exit code contract (see Program.cs): 0 = all migrations succeeded, 1 = failure → ACA Job marks the
# execution Failed and the deploy workflow aborts. NO secrets baked in; connection strings come from
# Key Vault via Managed Identity at runtime.

ARG DOTNET_VERSION=10.0
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY . .

RUN dotnet restore PulseOne.MigrationRunner/PulseOne.MigrationRunner.csproj
RUN dotnet publish PulseOne.MigrationRunner/PulseOne.MigrationRunner.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION} AS final
WORKDIR /app

USER $APP_UID

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PulseOne.MigrationRunner.dll"]
