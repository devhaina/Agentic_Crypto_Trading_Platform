#!/bin/sh
#
# Injects runtime configuration into the built Angular bundle.
#
# The bundle is built once and the same image is promoted through every
# environment, so the gateway URL cannot be baked in at build time — an image
# that differs between staging and production is no longer the artefact that was
# tested. This writes a small config object into index.html at container start,
# which environment.ts reads.

set -eu

: "${API_BASE_URL:=http://localhost:8080}"
: "${APP_ENVIRONMENT:=production}"

INDEX_FILE=/usr/share/nginx/html/index.html

if [ ! -f "$INDEX_FILE" ]; then
    echo "error: $INDEX_FILE is missing; the image was built incorrectly." >&2
    exit 1
fi

# Idempotent: strip any previously injected block before writing a new one, so
# a container restart does not accumulate duplicates.
sed -i '/__AGENTIVA_CONFIG__/d' "$INDEX_FILE"

CONFIG="<script>window.__AGENTIVA_CONFIG__={\"apiBaseUrl\":\"${API_BASE_URL}\",\"environment\":\"${APP_ENVIRONMENT}\"};</script>"

# Injected into <head> so it is evaluated before the application bundle runs.
sed -i "s#</head>#${CONFIG}</head>#" "$INDEX_FILE"

echo "agentiva-dashboard: apiBaseUrl=${API_BASE_URL} environment=${APP_ENVIRONMENT}"

exec "$@"
