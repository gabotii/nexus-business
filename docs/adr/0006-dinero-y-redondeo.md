# ADR-0006 — Dinero, precisión y redondeo

**Decisión.**
- C#: `decimal`. PostgreSQL: `numeric(19,4)` para importes y precios, `numeric(18,3)` para cantidades, `numeric(5,2)` para porcentajes. JSON: los importes viajan como **cadenas decimales** (`"12.50"`) para que JavaScript no los convierta en `double`; el SPA solo los formatea y nunca opera con ellos para decidir nada.
- Cada empresa tiene una moneda ISO 4217; su número de decimales (EUR 2, JPY 0, CLP 0, KWD 3…) define la precisión de redondeo. La moneda no se puede cambiar cuando existen ventas. Cada pedido guarda su moneda.
- Cálculo por línea, en este orden: `bruto = cantidad × precio_unitario` → `descuento = round(bruto × %desc / 100)` → `base = round(bruto) − descuento` → `impuesto = round(base × %impuesto / 100)` → `total_línea = base + impuesto`. Los totales del pedido son la suma de las líneas ya redondeadas (sin redondeos adicionales).
- Redondeo: `MidpointRounding.AwayFromZero` (comercial) a la precisión de la moneda. Los precios unitarios admiten hasta 4 decimales.
- El cliente nunca envía precios ni totales de venta: envía producto, cantidad y descuento; el servidor toma el precio vigente, calcula y guarda una instantánea en la línea.
- Descuentos: porcentaje por línea entre 0 y 100, con un máximo configurable por empresa (por defecto 100 %), aplicado en el servidor.
- Value object `Money(decimal amount, Currency currency)` en el dominio: impide sumar monedas distintas.
