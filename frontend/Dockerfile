# syntax=docker/dockerfile:1

# ---- build ----
FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
# Commercial name baked into the bundle (the API receives the same value as Branding__ProductName).
ARG VITE_PRODUCT_NAME="Nexus Business"
ENV VITE_PRODUCT_NAME=${VITE_PRODUCT_NAME}
RUN npm run build

# ---- runtime: static files + reverse proxy to the API, as a non-root user listening on 8080 ----
FROM nginxinc/nginx-unprivileged:1.29-alpine AS runtime
COPY nginx/default.conf /etc/nginx/conf.d/default.conf
COPY nginx/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
