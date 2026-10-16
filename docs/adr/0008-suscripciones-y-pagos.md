# ADR-0008 — Suscripciones y pagos desacoplados del proveedor

**Decisión.**
- El dominio conoce `SubscriptionPlan`, `Subscription` (estados Trialing, Active, PastDue, Canceled) y `SubscriptionEvent`; no conoce a ningún proveedor.
- Puerto `IPaymentProvider`: crear sesión de checkout, abrir el portal del cliente, cancelar al final del periodo y **verificar y traducir** un webhook a eventos de dominio (`SubscriptionActivated`, `PaymentFailed`, `SubscriptionRenewed`, `SubscriptionCanceled`).
- Adaptador **Sandbox** (sin cobros reales): checkout simulado dentro de la propia aplicación, rotulado como "Entorno de pruebas", que emite webhooks firmados con HMAC-SHA256 por el mismo camino que un proveedor real. **No se registra en Production**: la API se niega a arrancar si `Billing:Provider=Sandbox` en ese entorno.
- Un adaptador real (p. ej. Stripe) se implementa solo cuando existan credenciales de prueba y requisitos fiscales definidos.
- Webhooks: firma verificada sobre el cuerpo crudo con comparación en tiempo constante y ventana de 5 minutos; el evento se guarda con unicidad `(provider, provider_event_id)` y se procesa en la misma transacción que lo marca como procesado → las reentregas no duplican efectos.
- Volver del checkout en el navegador **no** activa nada: el SPA muestra "procesando" y consulta el estado hasta que llega el webhook.
- Límites del plan (usuarios, productos, pedidos/mes, funcionalidades) aplicados en los casos de uso bajo `SELECT … FOR UPDATE` de la suscripción, para que dos altas simultáneas no superen el límite. PastDue → periodo de gracia configurable con escritura; Canceled tras el fin del periodo → solo lectura.
