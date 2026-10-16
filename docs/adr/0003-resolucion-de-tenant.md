# ADR-0003 — Resolución del tenant por ruta + membresía

**Contexto.** Un usuario puede pertenecer a varias empresas y tener varias pestañas abiertas con empresas distintas.

**Opciones.**
1. Empresa activa guardada en la sesión del servidor: cambiarla en una pestaña cambia silenciosamente las demás → riesgo de escribir en la empresa equivocada.
2. Cabecera `X-Tenant-Id`: funciona, pero es invisible en los logs de acceso y en los enlaces.
3. **Segmento de ruta `/api/v1/tenants/{tenantId}/...`** (elegida).

**Decisión.**
- Todo endpoint de datos empresariales cuelga de `/api/v1/tenants/{tenantId}`. El SPA usa `/c/{slug}/…` en sus propias URL y traduce el slug a id con la lista de membresías del usuario.
- Un filtro (`TenantMembershipFilter`) se ejecuta antes que cualquier acción del grupo: carga la membresía activa de `(usuario autenticado, tenantId)` y los permisos de su rol, y construye un `ITenantContext` inmutable para la petición. Sin membresía activa o con la empresa suspendida: **404** `tenant_not_found` (no se distingue "no existe" de "no eres miembro").
- Cualquier `tenantId` presente en cuerpos o *query strings* se ignora: el único origen es la ruta validada.
- Los endpoints sin tenant (auth, perfil, mis empresas, crear empresa, aceptar invitación, webhooks) no tienen `ITenantContext`; si algo intentara leer datos de negocio desde ellos, el filtro global y RLS devuelven cero filas.

**Consecuencias.**
- (+) Explícito, auditable, seguro con varias pestañas, claves de caché naturales en el SPA.
- (−) Una consulta extra por petición (membresía + permisos), indexada y pequeña.
