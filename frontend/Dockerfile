#
# Agentiva — Dashboard (Angular 22)
#
# Build from THIS directory:
#   docker build -t agentiva/dashboard:dev frontend

# -----------------------------------------------------------------------------
# Stage 1 — build
#
# Node 22: the Angular 22 CLI requires >= 22.22.3 and refuses to run on Node 20.
# -----------------------------------------------------------------------------
FROM node:22-alpine AS build

WORKDIR /app

# Lockfile and manifest first, so the dependency layer is reused across every
# source change. `npm ci` rather than `npm install`: it installs exactly what
# the lockfile pins, which is what makes the build reproducible.
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .

RUN npm run build -- --configuration production

# -----------------------------------------------------------------------------
# Stage 2 — serve
# -----------------------------------------------------------------------------
FROM nginx:1.27-alpine AS runtime

RUN apk add --no-cache curl

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh

# Angular 22 emits into dist/<project>/browser.
COPY --from=build /app/dist/agentiva-dashboard/browser /usr/share/nginx/html

# Listens on 8080 rather than 80 so the process needs no privileged port and
# can run as the unprivileged nginx user.
RUN sed -i 's|^user .*|# user directive removed: runs as the nginx user|' /etc/nginx/nginx.conf \
    && sed -i 's|/var/run/nginx.pid|/tmp/nginx.pid|' /etc/nginx/nginx.conf \
    && chown -R nginx:nginx /usr/share/nginx/html /var/cache/nginx \
    && chmod +x /usr/local/bin/docker-entrypoint.sh

USER nginx

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=3 \
    CMD curl --fail --silent --show-error http://localhost:8080/alive || exit 1

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
CMD ["nginx", "-g", "daemon off;"]
