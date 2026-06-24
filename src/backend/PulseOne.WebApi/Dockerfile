# PulseOne producer API (stateless) — multi-stage build.
# Built remotely by `az acr build` in the api-producer-deploy workflow (no local Docker daemon
# required in CI). The build context is the backend solution root (src/backend) so every
# referenced project (Infrastructure, Application, CoreDomain, SharedKernel) is available.
#
# Pinned to the .NET 10 images. Final stage runs as a non-root user and exposes 8080 (ACA default
# ingress target). NO secrets are baked in — connection strings and Razorpay keys come from Key
# Vault via Managed Identity at runtime (CLAUDE.md security rule #1).

ARG DOTNET_VERSION=10.0
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Copy central package management + props first so restore layers cache on lockfile changes only.
COPY Directory.Build.props Directory.Packages.props ./
COPY . .

RUN dotnet restore PulseOne.WebApi/PulseOne.WebApi.csproj
RUN dotnet publish PulseOne.WebApi/PulseOne.WebApi.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app

# Run as the built-in non-root user shipped in the aspnet image.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PulseOne.WebApi.dll"]
