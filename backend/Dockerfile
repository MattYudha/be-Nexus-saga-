# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY PhotonBackend.sln ./
COPY src/PhotonBackend.Api/PhotonBackend.Api.csproj src/PhotonBackend.Api/
COPY tests/PhotonBackend.Tests/PhotonBackend.Tests.csproj tests/PhotonBackend.Tests/
RUN dotnet restore
COPY src/ src/
RUN dotnet publish src/PhotonBackend.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=5 \
  CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "PhotonBackend.Api.dll"]
