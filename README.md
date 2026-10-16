# NEXUS BUSINESS

Plataforma SaaS de gestión empresarial **multiempresa** para pymes: clientes, catálogo e inventario, ventas, equipo y permisos, informes, auditoría y suscripciones. *El nombre es provisional y se cambia por configuración (`PRODUCT_NAME`).*

> **Estado: Fase 2 (Identidad y multiempresa) completada y verificada.** Cuentas, sesiones revocables, empresas, equipo, roles con permisos e invitaciones, con aislamiento entre empresas en cuatro capas (filtro EF, interceptor de escritura, FK compuestas y RLS). Aún no hay módulos de negocio (clientes, catálogo, ventas): llegan a partir de la Fase 3. Ver [Estado y limitaciones](#estado-y-limitaciones).

## Documentación

| Documento | Contenido |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Arquitectura, módulos, modelo de datos, estrategia multiempresa, riesgos, plan por fases, criterios del MVP |
| [docs/adr/](docs/adr/) | Decisiones de arquitectura (ADR-0001 … 0010) |

## Stack

- **Frontend**: React 19, TypeScript 6 (estricto), Vite 8, React Router 8, TanStack Query 5, Tailwind CSS 4 + shadcn/ui (Radix), Vitest, Testing Library, MSW, Playwright.
- **Backend**: ASP.NET Core sobre .NET 10 (LTS), EF Core 10 + Npgsql, FluentValidation, Swashbuckle (OpenAPI), health checks, rate limiting, logs JSON.
- **Datos**: PostgreSQL 17 con dos roles (propietario `nexus` / ejecución `nexus_app`).
- **Infraestructura**: Docker Compose (db → migrate → api → web/nginx), GitHub Actions.

## Estructura

```text
backend/    Nexus.slnx · src/{Nexus.Domain, Nexus.Application, Nexus.Infrastructure, Nexus.Api} · tests/{UnitTests, IntegrationTests}
frontend/   src/{app, layouts, components, features, services, lib, test} · e2e/ · nginx/
docker/     postgres/initdb (creación de roles en el primer arranque)
docs/       ARCHITECTURE.md · adr/
```

## Desarrollo local

Requisitos: .NET SDK 10, Node.js ≥ 22.22, PostgreSQL 17 (local o en Docker).

### 1. Base de datos (una vez)

Con un superusuario de PostgreSQL:

```sql
CREATE ROLE nexus LOGIN CREATEDB PASSWORD 'nexus_dev';          -- propietario; CREATEDB para las BD de prueba
CREATE ROLE nexus_app LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD 'nexus_app_dev';  -- rol de ejecución de la API
CREATE DATABASE nexus OWNER nexus;
```

Estas credenciales son las de `appsettings.Development.json` y **solo valen para una base de datos local desechable** en `127.0.0.1`. Ningún otro entorno las usa: producción recibe las cadenas de conexión por variables de entorno.

### 2. API

```bash
cd backend
dotnet run --project src/Nexus.Api      # http://127.0.0.1:5100 · Swagger en /swagger
```

En Development aplica las migraciones al arrancar con el rol propietario (`Database:MigrateOnStartup`). En cualquier otro entorno esto está prohibido y la API no arranca si se activa.

En Development los correos (verificación, recuperación de contraseña, invitaciones) no se envían: se escriben como JSON en `backend/src/Nexus.Api/mail-pickup/` (ignorado por git). Los enlaces apuntan a `App:PublicBaseUrl` (`http://localhost:5173`). El inicio de sesión y el registro están limitados a 10 peticiones por minuto e IP; para pruebas manuales intensivas, `RateLimiting__CredentialsPerIpPerMinute=1000`.

### 3. Web

```bash
cd frontend
npm ci
npm run dev                             # http://localhost:5173 (proxy de /api y /health a la API)
```

## Pruebas

| Ámbito | Comando | Qué verifica |
|---|---|---|
| Backend | `cd backend && ./scripts/test.sh` | Unitarias (dominio, matriz de permisos, lista blanca de `IgnoreQueryFilters`) + integración contra PostgreSQL real: la API se ejecuta como `nexus_app` sobre una BD creada y migrada por el propietario para cada ejecución. Incluye acceso cruzado entre empresas por endpoint, escalada de privilegios, RLS por SQL directo, FK compuestas, reutilización de conexiones del pool (R17), sesiones, CSRF y anti-enumeración |
| Frontend | `npm run lint && npm run format:check && npm run typecheck && npm test` | Lint (incluye prohibición de `dangerouslySetInnerHTML` y de `localStorage` fuera de preferencias), tipos, componentes, guardas de sesión, UI según permisos y cliente HTTP (antiforgery) con MSW |
| E2E | API con `RateLimiting__CredentialsPerIpPerMinute=1000` + `npm run build && npm run preview`, luego `npm run e2e` | Registro → verificación por correo → empresa → invitación → aceptación, cierre de sesión (escritorio y móvil). Lee los correos de `mail-pickup/` |
| E2E sobre Docker | `E2E_BASE_URL=http://localhost:8080 E2E_MAILPIT_URL=http://127.0.0.1:8025 npm run e2e` | Lo mismo contra el stack de Compose, leyendo los correos de Mailpit |

Variables de las pruebas de integración: `NEXUS_TEST_DB_OWNER` (cadena de un rol con `CREATEDB`; por defecto el rol local `nexus`) y `NEXUS_TEST_DB_APP_PASSWORD` (por defecto `nexus_app_dev`). `E2E_CHANNEL=msedge` usa el Edge instalado en lugar de descargar Chromium.

`scripts/test.sh` recompila y reintenta si Windows Smart App Control bloquea una DLL recién compilada (error `0x800711C7`); en Linux/CI equivale a `dotnet build && dotnet test`.

## Docker Compose

```bash
cp .env.example .env        # sustituye TODOS los secretos (p. ej. openssl rand -hex 24)
docker compose up -d --build --wait
```

1. `db` crea en su primer arranque los roles `nexus` y `nexus_app` y la base `nexus` (`docker/postgres/initdb`).
2. `migrate` aplica las migraciones como `nexus` y termina.
3. `api` arranca solo si la migración terminó bien; se conecta como `nexus_app` y su *readiness* falla si ese rol pudiera saltarse RLS.
4. `web` (nginx sin privilegios) sirve el SPA y hace de proxy de `/api` y `/health`. Es el único puerto publicado para la aplicación.
5. `mailpit` (perfil `mailpit`, activo en `.env.example` con `COMPOSE_PROFILES=mailpit`) captura los correos en local; su interfaz está en `http://127.0.0.1:8025`. En un despliegue real se configura `SMTP_HOST`/`SMTP_PORT`/credenciales del proveedor con `SMTP_ENABLE_SSL=true` y se quita el perfil.

Las contraseñas de base de datos se aplican solo al crear el volumen `pgdata`; cambiarlas después requiere `ALTER ROLE`. Las claves de Data Protection (cifran los tickets de sesión y los enlaces de correo) viven en el volumen `dpkeys`: si se pierde, todas las sesiones se cierran y los enlaces pendientes dejan de funcionar.

## Configuración

| Clave (API) | Variable Compose | Por defecto | Notas |
|---|---|---|---|
| `ConnectionStrings:Default` | — (compuesta) | — | **Obligatoria.** Rol `nexus_app`. La API no arranca sin ella. |
| `ConnectionStrings:Migrations` | — (compuesta) | — | Rol propietario. Solo para `migrate` (y Development). |
| `Database:MigrateOnStartup` | — | `false` | Rechazada en Production. |
| `Branding:ProductName` | `PRODUCT_NAME` | `Nexus Business` | También se inyecta en el build del SPA (`VITE_PRODUCT_NAME`). |
| `RateLimiting:PerIpPerMinute` | `RATE_LIMIT_PER_IP_PER_MINUTE` | `600` | Por instancia (ADR-0001). |
| `RateLimiting:CredentialsPerIpPerMinute` | `RATE_LIMIT_CREDENTIALS_PER_MINUTE` | `10` | Inicio de sesión, registro, verificación y recuperación. |
| `App:PublicBaseUrl` | `PUBLIC_BASE_URL` | — | **Obligatoria.** URL pública del SPA para los enlaces de los correos. |
| `Email:Delivery` | (fijo `Smtp` en Compose) | `Smtp` | `Pickup` (archivos JSON) solo fuera de Production. |
| `Email:From`, `Email:SmtpHost`, `Email:SmtpPort`, `Email:SmtpUsername`, `Email:SmtpPassword`, `Email:SmtpEnableSsl` | `EMAIL_FROM`, `SMTP_*` | puerto `587`, SSL `true` | `SmtpHost` obligatorio con `Smtp`. Los correos salen de una *outbox* en la misma transacción que el cambio. |
| `DataProtection:KeysPath` | (fijo `/keys`, volumen `dpkeys`) | — | **Obligatoria en Production**: sin ella las sesiones no sobreviven a un reinicio. |
| `Swagger:Enabled` | `SWAGGER_ENABLED` | `false` | Siempre activo en Development. |
| `ForwardedHeaders:Enabled` | (fijo `true` en Compose) | `false` | Solo detrás de un proxy de confianza. |
| `Cors:AllowedOrigins` | — | `[]` | Innecesario con el despliegue de origen único. |

Toda la configuración se valida al arrancar: un valor inválido detiene la API con un mensaje explícito en lugar de fallar más tarde.

## Migraciones

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Nombre> -p src/Nexus.Infrastructure -s src/Nexus.Api -o Persistence/Migrations
dotnet run --project src/Nexus.Api -- migrate     # o el servicio "migrate" de Compose
```

Revisa siempre el SQL generado. Las migraciones destructivas se hacen en dos despliegues y con copia de seguridad previa (ADR-0010). CI falla si el modelo tiene cambios sin migración.

## Dependencias

Versiones exactas (NuGet centralizado en `Directory.Packages.props`; npm con `package-lock.json` y sin rangos). Política: versiones estables publicadas hace al menos unos días y compatibles entre sí. Decisiones del 2026-10-14: TypeScript 6.0.3 y no 7.x, porque typescript-eslint solo admite `<6.1`; React Router 8.4. CI ejecuta `dotnet list package --vulnerable` y `npm audit --omit=dev --audit-level=high`.

## Estado y limitaciones

**Verificado (2026-10-15, Windows 11 + PostgreSQL 17.6 local + Docker Desktop):**

- Backend: compilación sin avisos (warnings como errores, analizadores `latest-recommended`); 43 pruebas unitarias + 76 de integración en verde contra PostgreSQL real con el rol `nexus_app`.
- Frontend: lint, formato y tipos limpios; 43 pruebas Vitest; build de producción.
- E2E: 8/8 en Edge (escritorio y móvil), contra `vite preview` + API (correos por *pickup*) y contra Docker Compose (correos por SMTP a Mailpit).
- Compose: `db → migrate → api → web` sano; la migración de la Fase 2 se aplicó sobre el volumen existente de la Fase 1; RLS activado y forzado en `tenants`, `tenant_memberships`, `roles`, `role_permissions`, `invitations` y `audit_log`; claves de Data Protection persistidas en `dpkeys` con el usuario sin privilegios; sin errores en los logs de la API.
- Sin cambios de modelo pendientes de migración; sin paquetes vulnerables conocidos (NuGet y npm).

**Pendiente / no verificado:**

- El workflow de CI está escrito pero **no se ha ejecutado en GitHub**.
- **SMTP real no probado**: solo Mailpit. La entrega real depende del proveedor (SPF/DKIM, límites).
- La cookie de sesión es siempre `Secure`: funciona en `http://localhost` (los navegadores lo tratan como contexto seguro), pero cualquier otro host exige TLS delante de `web`. HSTS y `real_ip` detrás de otro balanceador: Fase 7.
- Quien se registra desde una invitación debe volver a abrir el enlace del correo tras verificar su cuenta (el registro no conserva el destino).
- Iniciar sesión sobre una sesión existente la revoca aunque las credenciales sean incorrectas (rotación de sesión, R3).
- Los límites de peticiones son en memoria y por instancia (ADR-0001). Cada petición autenticada lee su sesión de la base de datos (sin caché; ver ADR-0002).
- La tabla `users` no tiene RLS: se protege en la capa de aplicación (ADR-0004).
- Sin eliminación de cuenta ni exportación de datos personales; sin visor de auditoría (Fase 6); sin ajustes de empresa ni logo (Fase 3).
- El bundle del SPA supera los 500 kB sin dividir por rutas; carga diferida pendiente.
- Copias de seguridad, restauración y guía de operación: Fase 7 (`docs/OPERATIONS.md`).
