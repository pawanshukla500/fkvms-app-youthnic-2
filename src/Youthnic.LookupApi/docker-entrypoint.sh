#!/bin/sh
# PORT-aware launcher: Render injects PORT (default 10000).
# The API binds via LOOKUP_API_URL (Program.cs UseUrls overrides ASPNETCORE_URLS).
set -eu

PORT="${PORT:-10000}"
export LOOKUP_API_URL="http://0.0.0.0:${PORT}"
export ASPNETCORE_URLS="http://0.0.0.0:${PORT}"

exec dotnet /app/Youthnic.LookupApi.dll
