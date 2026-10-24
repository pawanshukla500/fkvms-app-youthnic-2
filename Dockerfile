# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------
# Youthnic LookupApi — production image (Render / any Docker host).
# Builds ONLY src/Youthnic.LookupApi. The WPF desktop project is not in this image.
#
# Image-size notes:
#   - Framework-dependent (this file): runtime layer is the shared aspnet:8.0 base,
#     so the app layer is only ~30 MB (app + Npgsql). Smallest total footprint on
#     hosts that already pull the base. Chosen over self-contained linux-x64
#     (~90 MB app layer + no shared base reuse).
#   - Memory matters more than size for the 512 MB Render instance; see the
#     DOTNET_GCConserveMemory / DOTNET_GCHeapHardLimit tuning below.
# ---------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only the project file first for layer caching, then restore.
COPY src/Youthnic.LookupApi/Youthnic.LookupApi.csproj src/Youthnic.LookupApi/
RUN dotnet restore src/Youthnic.LookupApi/Youthnic.LookupApi.csproj

# Copy the full API source and publish framework-dependent for linux-x64.
COPY src/Youthnic.LookupApi/ src/Youthnic.LookupApi/
RUN dotnet publish src/Youthnic.LookupApi/Youthnic.LookupApi.csproj \
      -c Release \
      -r linux-x64 \
      --self-contained false \
      --no-restore \
      -o /app/publish \
      /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Low-memory tuning for the 512 MB Render instance:
#   DOTNET_GCConserveMemory=1   - GC favors smaller heap over throughput.
#   DOTNET_GCHeapHardLimit      - hard cap so GC reacts well before the OOM killer.
ENV DOTNET_GCConserveMemory=1 \
    DOTNET_GCHeapHardLimit=300000000 \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish ./

# PORT-aware entrypoint: Render injects $PORT; default 10000. The app binds via
# LOOKUP_API_URL (Program.cs calls UseUrls, which wins over ASPNETCORE_URLS),
# so the entrypoint derives LOOKUP_API_URL from $PORT.
COPY src/Youthnic.LookupApi/docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh \
    && mkdir -p /app/logs \
    && chown -R app:app /app
USER app

EXPOSE 10000
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
