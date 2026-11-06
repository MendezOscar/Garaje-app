-- Saca de la base los casos con los que se prueba «Proponer los pasos», y la respuesta
-- correcta: los pasos y repuestos que el taller de verdad cargó en esa orden.
--
--   psql "$CADENA" -v taller='Taller Demo' -v casos=10 -t -A -f 1-casos.sql -o casos.json
--
-- Solo lee. No sale ningún precio a propósito: la IA propone qué pasos y qué repuestos, y el
-- precio lo pone el catálogo del taller después. Si los precios no entran al prompt, no hay
-- forma de que salga un número inventado en una cotización.

SELECT json_build_object(
  'taller', t.name,

  -- El catálogo del taller. Va una sola vez, no por caso: es lo que se guardaría en caché.
  'catalogo', json_build_object(
    'manoDeObra', (
      SELECT coalesce(json_agg(json_build_object(
               'codigo',   l.code,
               'nombre',   l.name,
               'categoria', l.category,
               'horas',    l.standard_hours) ORDER BY l.name), '[]'::json)
      FROM labor_services l
      WHERE l.tenant_id = t.id AND l.is_active
    ),
    'repuestos', (
      SELECT coalesce(json_agg(r), '[]'::json) FROM (
        SELECT json_build_object(
                 'sku',      p.sku,
                 'nombre',   p.name,
                 'marca',    p.brand,
                 'categoria', p.category,
                 'unidad',   p.unit,
                 -- Para qué vehículos sirve. Es lo que evita ofrecer una pastilla de otro carro.
                 'aplicaA', (
                   SELECT coalesce(json_agg(trim(
                            coalesce(a.brand,'') || ' ' || coalesce(a.model,'') || ' ' ||
                            coalesce(a.year_from::text,'') || '-' || coalesce(a.year_to::text,'') || ' ' ||
                            coalesce(a.engine,''))), '[]'::json)
                   FROM part_applications a WHERE a.part_id = p.id
                 )) AS r
        FROM parts p
        WHERE p.tenant_id = t.id AND p.is_active
        ORDER BY p.name
        LIMIT 150          -- si el taller tiene más, el prompt de verdad los filtra por vehículo
      ) q
    )
  ),

  'casos', (
    SELECT coalesce(json_agg(c ORDER BY c->>'numero'), '[]'::json) FROM (
      SELECT json_build_object(
        'numero',      w.number,
        'vehiculo',    trim(coalesce(v.brand,'') || ' ' || coalesce(v.model,'') || ' ' || coalesce(v.year::text,'')),
        'kilometraje', w.mileage_in,
        'diagnostico', w.diagnosis,

        -- La respuesta correcta: lo que el taller cargó de verdad.
        'pasosReales', (
          SELECT coalesce(json_agg(json_build_object(
                   'titulo', k.title,
                   'horas',  k.estimated_hours) ORDER BY k.sequence), '[]'::json)
          FROM work_order_tasks k WHERE k.work_order_id = w.id
        ),
        'repuestosReales', (
          SELECT coalesce(json_agg(json_build_object(
                   'sku',      coalesce(p.sku, '—'),
                   'nombre',   coalesce(p.name, wp.description),
                   'cantidad', wp.quantity)), '[]'::json)
          FROM work_order_parts wp
          LEFT JOIN parts p ON p.id = wp.part_id
          WHERE wp.work_order_id = w.id
        )) AS c
      FROM work_orders w
      LEFT JOIN vehicles v ON v.id = w.vehicle_id
      WHERE w.tenant_id = t.id
        AND coalesce(btrim(w.diagnosis), '') <> ''
        -- Solo órdenes que llegaron a tener pasos: sin ellos no hay con qué comparar.
        AND EXISTS (SELECT 1 FROM work_order_tasks k WHERE k.work_order_id = w.id)
      ORDER BY w.opened_at DESC
      LIMIT :casos
    ) z
  )
)
FROM tenants t
WHERE t.name = :'taller';
