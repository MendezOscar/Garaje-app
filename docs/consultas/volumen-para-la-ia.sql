-- Cuánto daría de sí el tope de créditos de la IA, con los talleres que ya están trabajando.
--
-- Contesta la única parte que se puede saber hoy: cuántas órdenes al mes lleva cada taller y
-- cuántas de ellas traen diagnóstico escrito, que son las candidatas a «Proponer los pasos».
-- Las consultas del técnico NO se pueden estimar aquí —esa función no existe todavía— y solo
-- salen midiendo el primer mes real.
--
-- Se corre contra la base del entorno que se quiera mirar. Solo lee.
--   psql "$CADENA" -f docs/consultas/volumen-para-la-ia.sql
--
-- Supuestos del cálculo, para poder discutirlos:
--   · Propuesta = 1 crédito · Consulta = 5 créditos · Lectura del negocio = 0.
--   · No toda orden con diagnóstico lleva propuesta: el cambio de aceite sale de un trabajo
--     frecuente. Se asume la mitad, que es lo que hay que comprobar midiendo.
--   · Consultas: dos por técnico por semana, que es una suposición y nada más.

WITH meses AS (
    SELECT
        w.tenant_id,
        date_trunc('month', w.opened_at) AS mes,
        count(*)                                                              AS ordenes,
        count(*) FILTER (WHERE coalesce(btrim(w.diagnosis), '') <> '')        AS con_diagnostico
    FROM work_orders w
    WHERE w.opened_at >= date_trunc('month', now()) - interval '6 months'
      AND w.opened_at <  date_trunc('month', now())          -- el mes en curso va a medias
    GROUP BY 1, 2
),
promedios AS (
    SELECT
        tenant_id,
        count(*)                        AS meses_con_datos,
        round(avg(ordenes), 1)          AS ordenes_al_mes,
        round(avg(con_diagnostico), 1)  AS con_diagnostico_al_mes,
        max(ordenes)                    AS su_mejor_mes
    FROM meses
    GROUP BY 1
),
tecnicos AS (
    SELECT u.tenant_id, count(*) AS tecnicos
    FROM users u
    JOIN user_roles ur ON ur.user_id = u.id
    JOIN roles      r  ON r.id = ur.role_id
    WHERE r.name = 'Technician'
      AND u.is_active
    GROUP BY 1
),
-- Cuánto se apoya el taller en los trabajos frecuentes. Es de toda su vida, no del periodo:
-- no hay forma de saber de qué plantilla salió una orden concreta, así que esto es una señal
-- de hábito, no un dato exacto.
plantillas AS (
    SELECT tenant_id,
           count(*)                        AS plantillas,
           coalesce(sum(usage_count), 0)   AS veces_usadas
    FROM job_templates
    GROUP BY 1
)
SELECT
    t.name                                                   AS taller,
    coalesce(x.tecnicos, 0)                                  AS tecnicos,
    p.meses_con_datos,
    p.ordenes_al_mes,
    p.su_mejor_mes,
    p.con_diagnostico_al_mes,
    CASE WHEN p.ordenes_al_mes > 0
         THEN round(100 * p.con_diagnostico_al_mes / p.ordenes_al_mes)
    END                                                      AS pct_con_diagnostico,
    coalesce(j.plantillas, 0)                                AS trabajos_frecuentes,
    coalesce(j.veces_usadas, 0)                              AS plantillas_usadas,

    -- Lo que importa: los créditos que gastaría al mes con el uso de hoy.
    round(p.con_diagnostico_al_mes / 2)                      AS propuestas_estimadas,
    coalesce(x.tecnicos, 0) * 2 * 4.3                        AS consultas_supuestas,
    round(p.con_diagnostico_al_mes / 2
          + coalesce(x.tecnicos, 0) * 2 * 4.3 * 5)           AS creditos_al_mes,
    round(100 * (p.con_diagnostico_al_mes / 2
          + coalesce(x.tecnicos, 0) * 2 * 4.3 * 5) / 500)    AS pct_del_tope_de_500
FROM promedios p
JOIN tenants t ON t.id = p.tenant_id
LEFT JOIN tecnicos   x ON x.tenant_id = p.tenant_id
LEFT JOIN plantillas j ON j.tenant_id = p.tenant_id
ORDER BY creditos_al_mes DESC;
