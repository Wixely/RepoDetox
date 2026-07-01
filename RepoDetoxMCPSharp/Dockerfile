# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Copy shared build infrastructure first for layer caching.
COPY NuGet.config global.json Directory.Build.props Directory.Build.targets Directory.Packages.props ./
COPY RepoDetox.Core/RepoDetox.Core.csproj RepoDetox.Core/
COPY RepoDetoxMCPSharp/RepoDetoxMCPSharp.csproj RepoDetoxMCPSharp/
ARG TARGETARCH
RUN arch="${TARGETARCH:-amd64}"; \
    if [ "$arch" = "amd64" ]; then arch="x64"; fi; \
    rid="linux-$arch"; \
    dotnet restore RepoDetoxMCPSharp/RepoDetoxMCPSharp.csproj \
    -r "$rid" \
    -p:PublishSingleFile=true \
    -p:SelfContained=false \
    -p:EnableCompressionInSingleFile=false

COPY RepoDetox.Core/ RepoDetox.Core/
COPY RepoDetoxMCPSharp/ RepoDetoxMCPSharp/
RUN arch="${TARGETARCH:-amd64}"; \
    if [ "$arch" = "amd64" ]; then arch="x64"; fi; \
    rid="linux-$arch"; \
    dotnet publish RepoDetoxMCPSharp/RepoDetoxMCPSharp.csproj \
    -c Release \
    --no-restore \
    -r "$rid" \
    --self-contained false \
    -o /app/publish \
    -p:PublishSingleFile=true \
    -p:EnableCompressionInSingleFile=false \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:IncludeAllContentForSelfExtract=true \
    -p:IsTransformWebConfigDisabled=true \
    -p:StaticWebAssetsEnabled=false \
    -p:DebugType=none \
    -p:DebugSymbols=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app

# RepoDetox drives git via subprocess — needs git on PATH inside the container.
RUN apt-get update && apt-get install -y --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_ENVIRONMENT=Production \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    REPODETOXMCP_Server__Host=0.0.0.0 \
    REPODETOXMCP_Server__Port=5714 \
    REPODETOXMCP_Server__Path=/mcp \
    REPODETOXMCP_Server__Password= \
    REPODETOXMCP_RepoDetox__ReadOnly=true \
    REPODETOXMCP_RepoDetox__AllowVacuum=false \
    REPODETOXMCP_RepoDetox__AllowAnonymise=false \
    REPODETOXMCP_RepoDetox__AllowFlatten=false \
    REPODETOXMCP_RepoDetox__AllowExpunge=false

RUN mkdir -p /app/logs && chown -R $APP_UID:0 /app
COPY --from=build --chown=$APP_UID:0 /app/publish ./

USER $APP_UID
EXPOSE 5714
VOLUME ["/app/logs"]

ENTRYPOINT ["./RepoDetoxMCPSharp"]
