# ADR-0007 — Inventario como libro de movimientos y concurrencia

**Decisión.**
- `inventory_movements` es un libro **append-only**: cada cambio de stock es un movimiento con tipo, causa, usuario, fecha y, si aplica, el pedido de origen. `products.stock_on_hand` es un saldo cacheado que solo se modifica en la misma transacción que inserta el movimiento (invariante `Σ delta = saldo`, comprobada en pruebas y con una consulta de diagnóstico).
- Descuento de stock con un `UPDATE` condicional atómico:
  `UPDATE products SET stock_on_hand = stock_on_hand - @q WHERE tenant_id = @t AND id = @p AND (@allowNegative OR stock_on_hand >= @q) RETURNING stock_on_hand`.
  Cero filas → `insufficient_stock` y *rollback* de toda la venta. Las líneas se procesan ordenadas por `product_id`, de modo que dos ventas concurrentes bloquean en el mismo orden (sin interbloqueos).
- La confirmación de una venta es **una transacción**: cambio de estado + líneas congeladas + movimientos + saldos + historial + auditoría.
- Los agregados editables (producto, cliente, pedido en borrador) usan concurrencia optimista con `xmin` → 409 `concurrency_conflict`.
- **Idempotencia**: los `POST` de creación y las transiciones de venta aceptan `Idempotency-Key` (el SPA siempre la envía). Se guarda `(tenant_id, key)` con el hash de la petición y la respuesta, en la misma transacción que la operación; un reintento devuelve la respuesta original y la misma clave con otro cuerpo → 422.
- Pruebas con PostgreSQL real: N confirmaciones concurrentes sobre la última unidad → exactamente una gana; un reintento con la misma clave → un único pedido y un único movimiento.
