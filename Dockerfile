# syntax=docker/dockerfile:1

FROM node:24-alpine AS frontend-build
WORKDIR /src
COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci
COPY src/frontend/ ./
RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS api-build
ARG TARGETARCH
WORKDIR /src
COPY src/database/AlexDirectorConsole.V2.Database.csproj src/database/
COPY src/backend/AlexDirectorConsole.V2.Api/AlexDirectorConsole.V2.Api.csproj src/backend/AlexDirectorConsole.V2.Api/
RUN case "$TARGETARCH" in \
        amd64) runtime_id=linux-x64 ;; \
        arm64) runtime_id=linux-arm64 ;; \
        *) echo "Unsupported target architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac \
    && dotnet restore src/backend/AlexDirectorConsole.V2.Api/AlexDirectorConsole.V2.Api.csproj \
        --runtime "$runtime_id"
COPY src/database/ src/database/
COPY src/backend/AlexDirectorConsole.V2.Api/ src/backend/AlexDirectorConsole.V2.Api/
RUN case "$TARGETARCH" in \
        amd64) runtime_id=linux-x64 ;; \
        arm64) runtime_id=linux-arm64 ;; \
        *) echo "Unsupported target architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac \
    && dotnet publish src/backend/AlexDirectorConsole.V2.Api/AlexDirectorConsole.V2.Api.csproj \
        --configuration Release \
        --no-restore \
        --runtime "$runtime_id" \
        --self-contained false \
        --output /app/publish \
    && mkdir -p /app/publish/App_Data/DataProtection
COPY --from=frontend-build /src/dist/ /app/publish/wwwroot/

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
COPY --from=api-build --chown=app:app /app/publish/ ./
VOLUME ["/app/App_Data"]
USER app
ENTRYPOINT ["dotnet", "AlexDirectorConsole.V2.Api.dll"]
