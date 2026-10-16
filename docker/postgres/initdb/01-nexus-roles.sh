#!/bin/sh
# Runs once, on the first start of an empty data volume (official postgres image convention).
# Creates the two roles of ADR-0010 and the application database:
#   nexus      schema owner, used only by the one-shot "migrate" service
#   nexus_app  runtime role of the API: no SUPERUSER, no BYPASSRLS, DML only (granted by the Baseline migration)
set -eu

: "${NEXUS_DB_OWNER_PASSWORD:?NEXUS_DB_OWNER_PASSWORD is required}"
: "${NEXUS_DB_APP_PASSWORD:?NEXUS_DB_APP_PASSWORD is required}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v owner_password="$NEXUS_DB_OWNER_PASSWORD" \
  -v app_password="$NEXUS_DB_APP_PASSWORD" <<'SQL'
CREATE ROLE nexus LOGIN NOSUPERUSER NOCREATEROLE NOCREATEDB PASSWORD :'owner_password';
CREATE ROLE nexus_app LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB PASSWORD :'app_password';
CREATE DATABASE nexus OWNER nexus;
REVOKE CONNECT ON DATABASE nexus FROM PUBLIC;
GRANT CONNECT ON DATABASE nexus TO nexus, nexus_app;
SQL
