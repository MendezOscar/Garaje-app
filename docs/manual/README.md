# Manual de GarajApp

Qué hace cada pantalla de la aplicación, con la foto de la pantalla al lado. Está escrito para
el taller —el dueño, el que atiende el mostrador, el técnico—, no para quien programa.

Las capturas salen de la aplicación de verdad corriendo contra un taller de demostración. Si
una pantalla cambia, se vuelven a sacar con `tools/capturar-manual.sh` y el manual queda al
día; no hay dibujos que se desactualicen por su cuenta.

> El panel de la web tiene los mismos módulos y los mismos nombres. Lo que aquí se explica del
> teléfono vale allá, con el menú a la izquierda en lugar de la barra de abajo.

## Quién ve qué

| | Dueño | Técnico | Cliente |
| --- | --- | --- | --- |
| Lo que abre al entrar | Hoy | Mi trabajo | Mi vehículo |
| Puede | todo el taller | sus órdenes y la bodega | ver sus vehículos y responder cotizaciones |
| Ve precios | sí | solo si el dueño lo permite | los suyos |

## Índice

**Empezar**

1. [Entrar y moverse por la app](01-primeros-pasos.md)
2. [Hoy: el resumen del día](02-hoy.md)

**El trabajo**

3. [Requerimientos: las citas que pide el cliente](03-requerimientos.md)
4. [Recibir un vehículo](04-recibir-vehiculo.md)
5. [Órdenes: la lista y el detalle](05-ordenes.md)
6. [Cotizaciones](06-cotizaciones.md)
7. [Reclamos y garantía](07-reclamos.md)
8. [Recordatorios de servicio](08-recordatorios.md)

**El dinero**

9. [Venta de mostrador](09-mostrador.md)
10. [Ventas](10-ventas.md)
11. [Por cobrar](11-por-cobrar.md)
12. [Cierre de caja](12-caja.md)
13. [Reportes](13-reportes.md)
14. [Resultados y gastos](14-resultados-y-gastos.md)

**Catálogos y taller**

15. [Clientes](15-clientes.md)
16. [Inventario](16-inventario.md)
17. [Mano de obra](17-mano-de-obra.md)
18. [Trabajos frecuentes](18-trabajos-frecuentes.md)
19. [Usuarios](19-usuarios.md)
20. [Ajustes del taller](20-ajustes-del-taller.md)
21. [Avisos](21-avisos.md)

**Los otros dos perfiles**

22. [La app del técnico](22-el-tecnico.md)
23. [La app del cliente](23-el-cliente.md)

## Cómo se rehacen las capturas

```bash
docker compose --profile local-db up -d postgres
cd backend/src/Garaj.Api && ASPNETCORE_ENVIRONMENT=Development Demo__AllowSeeding=true \
  dotnet run --urls http://localhost:5199        # sembrar con POST /api/demo/seed
xcrun simctl boot "iPhone 16" && open -a Simulator
tools/capturar-manual.sh                          # deja los PNG en docs/manual/img
```
