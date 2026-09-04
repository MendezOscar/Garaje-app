#!/usr/bin/env python3
"""Le carga un poco de trabajo a un taller que está vacío, usando la API como lo haría una
persona desde el mostrador.

Nació de la prueba cerrada de Play: el taller de los verificadores se creó en blanco, y una
pantalla llena de ceros no se puede probar —no se ve si un importe largo desborda, ni si el
gráfico dibuja algo—. Ver docs/pruebas-cerradas.md, punto 10.

**No es el sembrador de demostración.** `POST /api/demo/seed` borra la base entera y no se
corre en producción nunca. Esto solo hace altas por la API, con la sesión de un Dueño, así que
no puede tocar nada de otro taller: el filtro por taller de la API se encarga.

Uso:

    GARAJ_URL=https://garaje-app.onrender.com \
    GARAJ_EMAIL=dueno@tallerprueba.hn \
    GARAJ_PASSWORD='…' \
    python3 backend/tools/poblar-taller.py

Las credenciales van por variables de entorno a propósito: este repositorio es público.

Carga un mes corriente, no un histórico: seis órdenes en distintos estados, tres facturadas y
cobradas, y unas ventas de mostrador repartidas en los días que van del mes. Lo justo para que
las pantallas tengan algo que enseñar.

Se puede volver a correr: lo que ya exista se reusa —catálogo por SKU, servicios por código,
vehículos por placa y órdenes por su motivo—, así que una corrida cortada a medias continúa
donde iba y no deja el taller con el doble de trabajo inventado.
"""
import json
import os
import random
import sys
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone

BASE = os.environ.get("GARAJ_URL", "").rstrip("/")
EMAIL = os.environ.get("GARAJ_EMAIL", "")
PASSWORD = os.environ.get("GARAJ_PASSWORD", "")

# El día del taller, que es lo que la API usa para agrupar la caja y los reportes.
HONDURAS = timezone(timedelta(hours=-6))

_token = None
random.seed(7)


def call(method, path, body=None):
    req = urllib.request.Request(BASE + path, method=method)
    req.add_header("Content-Type", "application/json")
    if _token:
        req.add_header("Authorization", "Bearer " + _token)
    data = json.dumps(body).encode() if body is not None else None
    try:
        with urllib.request.urlopen(req, data) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except json.JSONDecodeError:
            return e.code, raw


def post(path, body, que):
    return _mandar("POST", path, body, que)


def put(path, body, que):
    return _mandar("PUT", path, body, que)


def _mandar(metodo, path, body, que):
    status, data = call(metodo, path, body)
    if status not in (200, 201):
        sys.exit(f"FALLA al {que}: {status} {data}")
    return data


def dia_del_mes(dia, hora=10):
    """Un instante del mes en curso, escrito en UTC.

    La hora se piensa en Honduras —«las cinco de la tarde»— y se manda convertida, que es lo
    que hacen la app y el panel. La API ya acepta las dos formas, pero mandar UTC evita
    depender de que el servidor esté al día.
    """
    hoy = datetime.now(HONDURAS)
    local = datetime(hoy.year, hoy.month, dia, hora, 0, tzinfo=HONDURAS)
    return local.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


REPUESTOS = [
    # sku, nombre, marca, unidad, costo, precio, existencia inicial, mínimo de reposición
    ("ACE-20W50", "Aceite 20W-50 mineral", "Castrol", "litro", 95, 165, 24, 6),
    ("FIL-ACE-01", "Filtro de aceite", "Bosch", "unidad", 70, 140, 12, 4),
    ("PAS-DEL-CB", "Pastillas de freno delanteras", "Brembo", "juego", 320, 580, 6, 2),
    ("BUJ-NGK-01", "Bujía NGK", "NGK", "unidad", 55, 110, 20, 8),
    ("BAT-12V", "Batería 12V", "Bosch", "unidad", 1450, 2200, 3, 2),
    # Entra por debajo de su mínimo a propósito: sin uno así, la alerta de inventario no se ve
    # y no hay forma de saber si se dibuja.
    ("CAD-428", "Cadena 428H", "DID", "unidad", 380, 690, 1, 3),
]

SERVICIOS = [
    ("MO-CAMB-ACE", "Cambio de aceite", 0.5, 250),
    ("MO-FRENOS", "Servicio de frenos", 1.5, 600),
    ("MO-DIAG", "Diagnóstico general", 1.0, 400),
]

CLIENTES = [
    ("Marlon Josué Erazo", "50499112233", "Honda", "CB125F", 2022, "MAB1204", 2),
    ("Karla Vanessa Discua", "50499224455", "Yamaha", "Crypton FI", 2023, "MCD3391", 2),
    ("Distribuidora El Progreso", "50422445566", "Toyota", "Hilux", 2019, "PBX4410", 1),
    ("Óscar Fernando Zelaya", "50499667788", "Bajaj", "Pulsar NS160", 2021, "MEF7712", 2),
    ("Wendy Paola Munguía", "50499880011", "Suzuki", "Swift", 2018, "PCH2038", 1),
]

# Cada orden: motivo, hasta qué estado llega, repuestos que consume, pasos.
ORDENES = [
    ("Cambio de aceite y revisión general", 8, [("ACE-20W50", 3), ("FIL-ACE-01", 1)],
     [("Cambio de aceite", "MO-CAMB-ACE")]),
    ("Frenos delanteros haciendo ruido", 8, [("PAS-DEL-CB", 1)],
     [("Revisión de frenos", "MO-DIAG"), ("Cambio de pastillas", "MO-FRENOS")]),
    ("No enciende, sospecha de batería", 8, [("BAT-12V", 1)],
     [("Diagnóstico eléctrico", "MO-DIAG")]),
    ("Mantenimiento de 10,000 km", 5, [("ACE-20W50", 3), ("BUJ-NGK-01", 2)],
     [("Cambio de aceite", "MO-CAMB-ACE"), ("Cambio de bujías", None)]),
    ("Se calienta en el tráfico", 3, [], [("Diagnóstico de enfriamiento", "MO-DIAG")]),
    ("Cadena floja y ruidosa", 1, [], []),
]


def main():
    global _token

    if not (BASE and EMAIL and PASSWORD):
        sys.exit("Faltan GARAJ_URL, GARAJ_EMAIL o GARAJ_PASSWORD.")

    print(f"Entrando a {BASE} como {EMAIL}")
    status, data = call("POST", "/api/auth/login", {"email": EMAIL, "password": PASSWORD})
    if status != 200:
        sys.exit(f"No se pudo entrar: {status} {data}")
    _token = data["accessToken"]

    status, branches = call("GET", "/api/branches")
    if status != 200 or not branches:
        sys.exit(f"No se pudieron leer las sucursales: {status} {branches}")
    branch = branches[0]["id"]
    print(f"Sucursal: {branches[0]['name']}")


    # Todo lo que sigue reusa lo que ya esté: si una corrida se cortó a medias, volver a
    # correrlo continúa donde iba en vez de chocar con lo que él mismo creó.
    print("\nCatálogo")
    _, listado = call("GET", "/api/parts?pageSize=200")
    partes = {p["sku"]: p["id"] for p in listado.get("items", [])}

    for sku, nombre, marca, unidad, costo, precio, existencia, minimo in REPUESTOS:
        if sku in partes:
            call("PUT", "/api/stock/settings", {
                "branchId": branch, "partId": partes[sku],
                "minQuantity": minimo, "location": None,
            })
            print(f"  {sku}: ya estaba")
            continue
        parte = post("/api/parts", {
            "sku": sku, "name": nombre, "description": None, "brand": marca,
            "category": "Repuestos", "unit": unidad,
            "costPrice": costo, "salePrice": precio,
        }, f"crear el repuesto {sku}")
        partes[sku] = parte["id"]
        post("/api/stock/receive", {
            "branchId": branch, "partId": parte["id"], "quantity": existencia,
            "unitCost": costo, "reference": "Carga inicial", "notes": None,
        }, f"recibir existencias de {sku}")
        # El mínimo va aparte de la entrada: es un ajuste de la ficha, no un movimiento.
        put("/api/stock/settings", {
            "branchId": branch, "partId": parte["id"],
            "minQuantity": minimo, "location": None,
        }, f"poner el mínimo de {sku}")
        print(f"  {sku}: {existencia} en bodega, mínimo {minimo}")

    _, catalogo = call("GET", "/api/labor-services")
    servicios = {s["code"]: s["id"] for s in (catalogo or [])}

    for codigo, nombre, horas, precio in SERVICIOS:
        if codigo in servicios:
            print(f"  {codigo}: ya estaba")
            continue
        servicio = post("/api/labor-services", {
            "code": codigo, "name": nombre, "description": None, "category": "Taller",
            "standardHours": horas, "hourlyRate": 0, "isFixedPrice": True,
            "fixedPrice": precio,
        }, f"crear el servicio {codigo}")
        servicios[codigo] = servicio["id"]
        print(f"  {codigo}: L {precio}")

    print("\nClientes y vehículos")
    _, flota = call("GET", "/api/vehicles?pageSize=200")
    porPlaca = {v["plate"]: (v["customerId"], v["id"]) for v in flota.get("items", [])
                if v.get("plate")}

    vehiculos = []
    for nombre, telefono, marca, modelo, anio, placa, tipo in CLIENTES:
        if placa in porPlaca:
            cliente_id, vehiculo_id = porPlaca[placa]
            vehiculos.append((cliente_id, vehiculo_id, placa))
            print(f"  {placa}: ya estaba")
            continue
        cliente = post("/api/customers", {
            "fullName": nombre, "phone": telefono, "email": None, "documentId": None,
            "address": None, "notes": None,
        }, f"crear al cliente {nombre}")
        vehiculo = post("/api/vehicles", {
            "customerId": cliente["id"], "type": tipo, "brand": marca, "model": modelo,
            "year": anio, "plate": placa, "vin": None, "color": None,
            "mileage": random.randrange(8000, 60000, 500), "notes": None,
        }, f"crear el vehículo {placa}")
        vehiculos.append((cliente["id"], vehiculo["id"], placa))
        print(f"  {nombre} · {marca} {modelo} {placa}")

    print("\nÓrdenes")
    # Se salta las que ya existen por su motivo: correrlo dos veces no deja el taller con el
    # doble de trabajo inventado, y una corrida cortada a medias se puede continuar.
    _, abiertas = call("GET", "/api/work-orders?pageSize=200")
    yaEstan = {o.get("description") for o in abiertas.get("items", [])}

    for i, (motivo, hasta, repuestos, pasos) in enumerate(ORDENES):
        if motivo in yaEstan:
            print(f"  «{motivo[:38]}…»: ya estaba")
            continue

        cliente_id, vehiculo_id, placa = vehiculos[i % len(vehiculos)]
        orden = post("/api/work-orders", {
            "branchId": branch, "vehicleId": vehiculo_id, "description": motivo,
            "assignedTechnicianId": None,
            "mileageIn": random.randrange(8000, 60000, 500),
            "promisedAt": dia_del_mes(min(28, datetime.now(HONDURAS).day + 1), 17),
        }, f"abrir la orden de {placa}")
        oid = orden["id"]

        for titulo, servicio in pasos:
            paso = post(f"/api/work-orders/{oid}/tasks", {
                "title": titulo, "description": None, "assignedTechnicianId": None,
                "laborServiceId": servicios.get(servicio) if servicio else None,
                "estimatedHours": None,
            }, f"agregar el paso «{titulo}»")
            if hasta >= 5:
                post(f"/api/work-orders/{oid}/tasks/{paso['id']}/complete", {
                    "isDone": True, "actualHours": None,
                    "technicianNotes": "Trabajo terminado.",
                }, "cerrar el paso")

        for sku, cantidad in repuestos:
            post(f"/api/work-orders/{oid}/parts", {
                "partId": partes[sku], "quantity": cantidad, "unitPrice": None,
                "workOrderTaskId": None,
            }, f"consumir {sku}")

        # El flujo válido es Recibido → Diagnóstico → Espera aprobación → Reparando →
        # Prueba → Listo → Entregado; saltarse un paso lo rechaza la API.
        for estado in [2, 3, 5, 6, 7][: max(0, [1, 2, 3, 5, 6, 7, 8].index(hasta))]:
            call("POST", f"/api/work-orders/{oid}/status", {"status": estado, "note": None})

        if hasta == 8:
            # Cerrar la orden ya cobra: sin `initialPayment` la venta queda pagada del todo,
            # así que registrar un abono aparte sería cobrarla dos veces.
            #
            # La tercera se deja a crédito a propósito: sin una venta con saldo, la pantalla
            # de cuentas por cobrar también sale vacía.
            aCredito = i == 2
            venta = post("/api/sales/close-work-order", {
                "workOrderId": oid, "paymentMethod": 3 if i % 2 else 1, "notes": None,
                "taxRate": 0, "includeLabor": True, "markAsDelivered": True,
                "initialPayment": 1000 if aCredito else None,
                "dueDate": dia_del_mes(28, 17) if aCredito else None,
            }, "facturar la orden")
            total = venta.get("total", 0)
            print(f"  {placa}: facturada L {total}"
                  + (" · abonó L 1000, queda a crédito" if aCredito else " · pagada"))
        else:
            print(f"  {placa}: {motivo[:40]}…")

    print("\nVentas de mostrador")
    _, ventas = call("GET", "/api/sales?pageSize=200")
    conMostrador = {v["saleDate"][:10] for v in ventas.get("items", [])
                    if not v.get("workOrderId")}

    hoy = datetime.now(HONDURAS).day
    for dia in sorted({1, max(1, hoy - 2), max(1, hoy - 1), hoy}):
        fecha = dia_del_mes(dia, 11)
        if fecha[:10] in conMostrador:
            print(f"  día {dia}: ya estaba")
            continue
        sku, cantidad = random.choice([("ACE-20W50", 2), ("BUJ-NGK-01", 1), ("FIL-ACE-01", 1)])
        venta = post("/api/sales", {
            "branchId": branch, "customerId": None, "paymentMethod": 1,
            "saleDate": fecha, "notes": None, "taxRate": 0,
            "lines": [{"partId": partes[sku], "quantity": cantidad, "unitPrice": None,
                       "description": None, "lineType": 1}],
        }, f"vender {sku} en mostrador")
        print(f"  día {dia}: {sku} × {cantidad} · L {venta.get('total', 0)}")

    print("\nListo. El taller ya tiene con qué probar.")


if __name__ == "__main__":
    main()
