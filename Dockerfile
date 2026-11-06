FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY .editorconfig global.json Directory.Build.props Directory.Build.targets Directory.Packages.props ./
COPY src/LlmShootout.Arena/LlmShootout.Arena.csproj src/LlmShootout.Arena/
COPY src/LlmShootout.Games/LlmShootout.Games.csproj src/LlmShootout.Games/
COPY src/LlmShootout.Web/LlmShootout.Web.csproj src/LlmShootout.Web/
RUN dotnet restore src/LlmShootout.Web/LlmShootout.Web.csproj

COPY src/ src/
ARG GIT_COMMIT=unknown
RUN dotnet publish src/LlmShootout.Web/LlmShootout.Web.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false /p:SourceRevisionId=${GIT_COMMIT}

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd -r appgroup && useradd -r -g appgroup appuser
COPY --from=build /app/publish .
RUN mkdir -p /data/matches /data/state && chown -R appuser:appgroup /data

USER appuser
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Web__MatchesDirectory=/data/matches \
    Web__DataDirectory=/data/state

HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -fs http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "LlmShootout.Web.dll"]
