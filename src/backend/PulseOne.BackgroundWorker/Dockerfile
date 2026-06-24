# PulseOne Hangfire background worker (consumer) — multi-stage build.
# Built remotely by `az acr build` in the worker-consumer-deploy workflow. The worker scales on
# the KEDA MSSQL trigger (Phase 3); ACA does not route ingress to it. Build context is the backend
# solution root (src/backend).
#
# No HTTP ingress is exposed (the worker is a Worker SDK host, not a web app). NO secrets baked in;
# SQL/Storage connection strings come from Key Vault via Managed Identity at runtime.

ARG DOTNET_VERSION=10.0
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY . .

RUN dotnet restore PulseOne.BackgroundWorker/PulseOne.BackgroundWorker.csproj
RUN dotnet publish PulseOne.BackgroundWorker/PulseOne.BackgroundWorker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION} AS final
WORKDIR /app

USER $APP_UID

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PulseOne.BackgroundWorker.dll"]
