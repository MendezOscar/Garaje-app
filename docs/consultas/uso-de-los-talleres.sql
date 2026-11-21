-- Cómo está usando GarajApp cada taller.
--
-- Una fila por taller, con lo de los últimos 30 días al lado de lo de siempre. Sirve para
-- contestar tres cosas antes de llamar a nadie:
--
--   · ¿Entran? — última sesión y cuántos de sus usuarios entraron este mes.
--   · ¿Trabajan adentro? — órdenes abiertas, facturas emitidas, fotos subidas.
--   · ¿Qué partes no tocan? — cotizaciones, escáner, asistente, citas, armado.
--
-- Un taller que factura pero no cotiza no está en problemas: está usando la mitad. Un taller
-- que no abre órdenes hace dos semanas sí, aunque haya pagado.
--
-- Se corre contra la base del entorno que se quiera mirar. Solo lee.
--   psql "$CADENA" -f docs/consultas/uso-de-los-talleres.sql

WITH ventana AS (
    SELECT (now() - interval '30 days') AS desde
),

-- La gente del taller y cuándo entró. `last_login_at` lo escribe el login, así que es el
-- dato bueno: no depende de que el refresh token siga vivo.
gente AS (
    SELECT
        u.tenant_id,
        count(*) FILTER (WHERE u.is_active)                             AS usuarios_activos,
        count(*) FILTER (WHERE NOT u.is_active)                         AS usuarios_de_baja,
        count(*) FILTER (WHERE u.is_active AND ro.name = 'Technician')  AS tecnicos_activos,
        max(u.last_login_at)                                            AS ultimo_ingreso,
        count(*) FILTER (WHERE u.last_login_at >= v.desde)              AS usuarios_del_mes
    FROM users u
    LEFT JOIN user_roles ur ON ur.user_id = u.id
    LEFT JOIN roles ro      ON ro.id = ur.role_id
    CROSS JOIN ventana v
    GROUP BY u.tenant_id
),

ordenes AS (
    SELECT
        w.tenant_id,
        count(*)                                            AS ordenes_total,
        count(*) FILTER (WHERE w.opened_at >= v.desde)      AS ordenes_30d,
        max(w.opened_at)                                    AS ultima_orden
    FROM work_orders w
    CROSS JOIN ventana v
    GROUP BY w.tenant_id
),

facturas AS (
    SELECT
        s.tenant_id,
        count(*) FILTER (WHERE s.sale_date >= v.desde AND s.voided_at IS NULL)        AS ventas_30d,
        coalesce(sum(s.total) FILTER (WHERE s.sale_date >= v.desde
                                        AND s.voided_at IS NULL), 0)                 AS facturado_30d,
        max(s.sale_date) FILTER (WHERE s.voided_at IS NULL)                          AS ultima_venta
    FROM sales s
    CROSS JOIN ventana v
    GROUP BY s.tenant_id
),

cotizaciones AS (
    SELECT q.tenant_id, count(*) AS cotizaciones_30d
    FROM quotes q CROSS JOIN ventana v
    WHERE q.created_at >= v.desde
    GROUP BY q.tenant_id
),

fotos AS (
    SELECT m.tenant_id, count(*) AS fotos_30d
    FROM media_attachments m CROSS JOIN ventana v
    WHERE m.created_at >= v.desde
    GROUP BY m.tenant_id
),

escaner AS (
    SELECT e.tenant_id, count(*) AS escaneos_30d
    FROM vehicle_scans e CROSS JOIN ventana v
    WHERE e.scanned_at >= v.desde
    GROUP BY e.tenant_id
),

asistente AS (
    SELECT a.tenant_id, count(*) AS asistente_30d
    FROM assistant_calls a CROSS JOIN ventana v
    WHERE a.created_at >= v.desde
    GROUP BY a.tenant_id
),

citas AS (
    SELECT c.tenant_id, count(*) AS citas_30d
    FROM appointments c CROSS JOIN ventana v
    WHERE c.created_at >= v.desde
    GROUP BY c.tenant_id
),

armado AS (
    SELECT m.tenant_id, count(*) AS motos_30d
    FROM motorcycle_assemblies m CROSS JOIN ventana v
    WHERE m.created_at >= v.desde
    GROUP BY m.tenant_id
)

SELECT
    t.name                                           AS taller,
    t.plan,
    t.is_active                                      AS activo,
    coalesce(g.usuarios_activos, 0)                  AS usuarios,
    coalesce(g.usuarios_de_baja, 0)                  AS de_baja,
    coalesce(g.tecnicos_activos, 0)                  AS tecnicos,
    coalesce(g.usuarios_del_mes, 0)                  AS entraron_30d,
    date_trunc('minute', g.ultimo_ingreso)           AS ultimo_ingreso,
    coalesce(o.ordenes_30d, 0)                       AS ordenes_30d,
    coalesce(o.ordenes_total, 0)                     AS ordenes_total,
    date_trunc('day', o.ultima_orden)                AS ultima_orden,
    coalesce(f.ventas_30d, 0)                        AS ventas_30d,
    round(coalesce(f.facturado_30d, 0), 2)           AS facturado_30d,
    coalesce(c.cotizaciones_30d, 0)                  AS cotiza_30d,
    coalesce(p.fotos_30d, 0)                         AS fotos_30d,
    coalesce(e.escaneos_30d, 0)                      AS escaner_30d,
    coalesce(a.asistente_30d, 0)                     AS asistente_30d,
    coalesce(ci.citas_30d, 0)                        AS citas_30d,
    coalesce(ar.motos_30d, 0)                        AS motos_30d
FROM tenants t
LEFT JOIN gente        g  ON g.tenant_id  = t.id
LEFT JOIN ordenes      o  ON o.tenant_id  = t.id
LEFT JOIN facturas     f  ON f.tenant_id  = t.id
LEFT JOIN cotizaciones c  ON c.tenant_id  = t.id
LEFT JOIN fotos        p  ON p.tenant_id  = t.id
LEFT JOIN escaner      e  ON e.tenant_id  = t.id
LEFT JOIN asistente    a  ON a.tenant_id  = t.id
LEFT JOIN citas        ci ON ci.tenant_id = t.id
LEFT JOIN armado       ar ON ar.tenant_id = t.id
-- El que menos se mueve, arriba: es a quien hay que llamar.
ORDER BY coalesce(o.ordenes_30d, 0), t.name;
