# ADR-0002 — Autenticación por cookie con sesiones en servidor

**Contexto.** SPA servida desde el mismo origen que la API (nginx hace de proxy de `/api`). Se exige gestión y revocación de sesiones, y no guardar credenciales de larga duración en `localStorage`.

**Opciones.**
1. JWT de acceso + refresh token: obliga a guardar tokens accesibles desde JS o a montar igualmente cookies; la revocación exige listas de bloqueo.
2. Cookie de Identity sin estado: sencilla, pero solo permite revocar *todas* las sesiones (security stamp) y no listarlas.
3. **Cookie de Identity con `ITicketStore` en PostgreSQL** (elegida).

**Decisión.**
- Cookie `nexus.session`: `HttpOnly`, `Secure`, `SameSite=Strict`, expiración deslizante (8 h de inactividad; 14 días con "recordarme").
- La cookie solo contiene una clave aleatoria de sesión (cifrada); en `user_sessions` se guarda su SHA-256 y el ticket cifrado con Data Protection. Cada petición valida que la sesión no esté revocada ni caducada. *Implementación (Fase 2):* sin caché en memoria, para que la revocación sea inmediata en todas las réplicas; es una lectura por clave única.
- Iniciar sesión siempre crea una sesión nueva: si la petición de login trae una cookie de sesión, esa sesión se revoca primero (fijación de sesión, R3).
- El usuario puede ver sus sesiones activas y cerrar cualquiera; cambiar o restablecer la contraseña revoca todas las demás.
- CSRF: antiforgery de doble envío (cookie legible `XSRF-TOKEN` → cabecera `X-XSRF-TOKEN`) en todo método no seguro bajo `/api`, ligado a la identidad y reemitido al iniciar y cerrar sesión. Un token rechazado responde 400 `csrf_failed`; el SPA pide uno nuevo y reintenta una vez. Las cookies de antiforgery siguen el esquema de la petición (`Secure` tras el proxy TLS gracias a `X-Forwarded-Proto`); la de sesión es siempre `Secure`.
- Las claves de Data Protection se persisten (volumen) para sobrevivir reinicios.
- La API responde 401/403 con *problem details*; nunca redirige.

**Consecuencias.**
- (+) Revocación inmediata e individual; nada sensible accesible desde JavaScript.
- (−) Una lectura de base de datos por petición (indexada). Si llegara a pesar, una caché corta por instancia retrasaría la revocación como máximo esos segundos.
- (−) Integraciones de terceros (no navegador) necesitarán API keys por empresa: fuera de alcance de la v1.
