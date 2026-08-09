#!/usr/bin/env python3
"""Humo de la Fase 9: el precio de la mano de obra.

Comprueba que un paso de la orden se cobra por el servicio del catálogo que lleve asignado,
que la orden muestra lo que suma esa mano de obra, y que al facturar se puede cobrar la de la
cotización que el cliente aprobó en lugar de la de los pasos. Escribe en la base: va contra el
entorno local.

    python3 backend/tests/smoke/fase9_smoke.py
"""

import json
import sys
import time
import urllib.error
import urllib.request

BASE = "http://localhost:5080"
PASSWORD = "Garaj123!"

# Espejo de Garaj.Domain.Enums
MOTORCYCLE = 2
CASH = 1
PART, LABOR = 1, 2

ok = 0
failed = []


def check(name, condition, detail=""):
    global ok
    if condition:
        ok += 1
        print(f"  ok   {name}")
    else:
        failed.append(name)
        print(f"  FALLA {name}" + (f" — {detail}" if detail else ""))


def api(method, path, body=None, token=None, raw=False):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")

    try:
        with urllib.request.urlopen(req) as r:
            payload = r.read()
            if raw:
                return r.status, payload
            try:
                return r.status, json.loads(payload) if payload else None
            except (ValueError, UnicodeDecodeError):
                return r.status, payload
    except urllib.error.HTTPError as e:
        payload = e.read()
        if raw:
            return e.code, payload
        try:
            return e.code, json.loads(payload)
        except (ValueError, UnicodeDecodeError):
            return e.code, payload


def login(email):
    status, data = api("POST", "/api/auth/login", {"email": email, "password": PASSWORD})
    if status != 200:
        sys.exit(f"No se pudo entrar como {email}: {status} {data}")
    return data["accessToken"]


print("Fase 9 — precio de la mano de obra\n")

owner = login("owner@garaj.test")
technician = login("tecnico1@garaj.test")
customer = login("cliente@garaj.test")

_, branches = api("GET", "/api/branches", token=owner)
matriz = next(b for b in branches if b["code"] == "MTZ")

# Placas y teléfonos únicos para poder repetir el humo sin sembrar de nuevo la base.
suffix = str(int(time.time()))[-5:]
counter = [0]

_, staff = api("GET", "/api/users?role=Technician", token=owner)
tech_id = next(t["id"] for t in staff if t["email"] == "tecnico1@garaj.test")


def new_order(labor_ids):
    """Un vehículo nuevo con su orden abierta y un paso por cada servicio indicado."""
    counter[0] += 1
    tag = f"{suffix}{counter[0]}"

    _, buyer = api("POST", "/api/customers", {
        "fullName": f"Mano de Obra {tag}", "phone": f"5049{tag}",
    }, token=owner)

    _, vehicle = api("POST", "/api/vehicles", {
        "customerId": buyer["id"], "type": MOTORCYCLE,
        "brand": "Yamaha", "model": "FZ 150", "plate": f"MDO{tag}",
    }, token=owner)

    _, request = api("POST", "/api/service-requests", {
        "branchId": matriz["id"], "vehicleId": vehicle["id"],
        "description": "Servicio completo",
    }, token=owner)

    _, approved = api("POST", f"/api/service-requests/{request['id']}/approve", {},
                      token=owner)
    order_id = approved["workOrderId"]

    # Asignada al técnico: si no, su bandeja no la incluye y todo lo suyo daría 404.
    api("PUT", f"/api/work-orders/{order_id}/assign", {"technicianId": tech_id}, token=owner)

    for i, labor_id in enumerate(labor_ids, start=1):
        api("POST", f"/api/work-orders/{order_id}/tasks", {
            "title": f"Paso {i}", "laborServiceId": labor_id,
        }, token=owner)

    return order_id


# ------------------------------------------------------- el catálogo le pone precio al paso

print("[el precio vive en el paso]")

status, services = api("GET", "/api/labor-services", token=owner)
check("el Dueño ve el catálogo de mano de obra", status == 200, str(status))
priced = [s for s in services if s["price"] > 0]
check("y tiene servicios con precio", len(priced) >= 2, str(len(priced)))

first, second = priced[0], priced[1]

status, _ = api("GET", "/api/labor-services", token=technician)
check("el técnico también lo ve", status == 200, str(status))

status, _ = api("GET", "/api/labor-services", token=customer)
check("el cliente no", status == 403, str(status))

status, _ = api("POST", "/api/labor-services", {
    "code": f"HUM{suffix}", "name": "Precio puesto por el técnico",
    "standardHours": 1, "hourlyRate": 500, "isFixedPrice": False,
    "fixedPrice": 0, "isActive": True,
}, token=technician)
check("y el técnico no pone precios nuevos", status == 403, str(status))

order_id = new_order([])

status, task = api("POST", f"/api/work-orders/{order_id}/tasks", {
    "title": "Revisión general",
}, token=owner)
check("un paso sin servicio se crea igual", status == 200, f"{status} {task}")
check("pero no tiene precio", task["laborPrice"] is None, str(task.get("laborPrice")))

_, order = api("GET", f"/api/work-orders/{order_id}", token=owner)
check("y la orden no cobra mano de obra", order["laborTotal"] == 0, str(order.get("laborTotal")))

status, priced_task = api("POST", f"/api/work-orders/{order_id}/tasks", {
    "title": "Cambio de aceite", "laborServiceId": first["id"],
}, token=owner)
check("un paso con servicio sí lo tiene", priced_task["laborPrice"] == first["price"],
      f'{priced_task.get("laborPrice")} vs {first["price"]}')
check("y trae el nombre del servicio", priced_task["laborServiceName"] == first["name"],
      str(priced_task.get("laborServiceName")))
check("con las horas estándar puestas solas",
      priced_task["estimatedHours"] == first["standardHours"],
      f'{priced_task.get("estimatedHours")} vs {first["standardHours"]}')

_, order = api("GET", f"/api/work-orders/{order_id}", token=owner)
check("la orden ya suma esa mano de obra", order["laborTotal"] == first["price"],
      f'{order["laborTotal"]} vs {first["price"]}')

status, changed = api("PUT", f"/api/work-orders/{order_id}/tasks/{task['id']}", {
    "title": task["title"], "laborServiceId": second["id"],
}, token=technician)
check("el técnico le pone precio al paso suelto", status == 200, f"{status} {changed}")
check("y queda con el del servicio nuevo", changed["laborPrice"] == second["price"],
      f'{changed.get("laborPrice")} vs {second["price"]}')

_, order = api("GET", f"/api/work-orders/{order_id}", token=owner)
check("la orden suma los dos pasos",
      abs(order["laborTotal"] - (first["price"] + second["price"])) < 0.01,
      f'{order["laborTotal"]} vs {first["price"] + second["price"]}')

status, _ = api("PUT", f"/api/work-orders/{order_id}/tasks/{task['id']}", {
    "title": task["title"], "laborServiceId": "00000000-0000-0000-0000-000000000001",
}, token=owner)
check("un servicio inventado se rechaza", status == 404, str(status))

status, cleared = api("PUT", f"/api/work-orders/{order_id}/tasks/{task['id']}", {
    "title": task["title"], "laborServiceId": None,
}, token=owner)
check("y se le puede quitar el cobro", cleared["laborPrice"] is None,
      str(cleared.get("laborPrice")))

status, _ = api("PUT", f"/api/work-orders/{order_id}/tasks/{task['id']}", {
    "title": "El cliente poniendo precios", "laborServiceId": first["id"],
}, token=customer)
check("el cliente no le pone precio a nada", status in (403, 404), str(status))

# --------------------------------------------------- la factura cobra lo que se aprobó

print("\n[la factura y la cotización]")

_, order = api("GET", f"/api/work-orders/{order_id}", token=owner)
tasks_labor = order["laborTotal"]

status, quote = api("POST", "/api/quotes/from-work-order", {
    "workOrderId": order_id, "includeTasks": True, "includeParts": True,
}, token=owner)
check("la cotización se arma con los pasos", status == 201, f"{status} {quote}")

quote_labor = sum(l["total"] for l in quote["lines"] if l["lineType"] == LABOR)
check("y su mano de obra es la de la orden", abs(quote_labor - tasks_labor) < 0.01,
      f"{quote_labor} vs {tasks_labor}")

# Lo que el Dueño hace de verdad: agrega a mano lo que no estaba en los pasos.
_, quote = api("POST", f"/api/quotes/{quote['id']}/lines", {
    "lineType": LABOR,
    "description": "Ajuste de válvulas",
    "quantity": 1,
    "unitPrice": 600,
}, token=owner)
quote_labor = sum(l["total"] for l in quote["lines"] if l["lineType"] == LABOR)
check("se le puede agregar mano de obra a mano", quote_labor > tasks_labor,
      f"{quote_labor} vs {tasks_labor}")

status, sale = api("POST", "/api/sales/close-work-order", {
    "workOrderId": order_id,
    "paymentMethod": CASH,
    "laborFromQuoteId": quote["id"],
}, token=owner)
check("se factura con la mano de obra de la cotización", status == 201, f"{status} {sale}")

sale_labor = sum(l["total"] for l in sale["lines"] if l["lineType"] == LABOR)
check("y es la de la cotización, no la de los pasos",
      abs(sale_labor - quote_labor) < 0.01, f"{sale_labor} vs {quote_labor}")
check("que era justo lo que faltaba", sale_labor > tasks_labor,
      f"{sale_labor} vs {tasks_labor}")
check("la línea agregada a mano viaja a la factura",
      any(l["description"] == "Ajuste de válvulas" for l in sale["lines"]),
      str([l["description"] for l in sale["lines"]]))

status, pdf = api("GET", f"/api/sales/{sale['id']}/pdf", token=owner, raw=True)
check("la factura se imprime", status == 200 and pdf[:4] == b"%PDF", str(status))

# ------------------------------------------------------------- las otras dos maneras

print("\n[cobrar los pasos, o no cobrar]")

second_order = new_order([first["id"], second["id"]])
_, detail = api("GET", f"/api/work-orders/{second_order}", token=owner)
expected = detail["laborTotal"]

status, sale = api("POST", "/api/sales/close-work-order", {
    "workOrderId": second_order, "paymentMethod": CASH,
}, token=owner)
check("sin cotización se cobran los pasos", status == 201, f"{status} {sale}")
sale_labor = sum(l["total"] for l in sale["lines"] if l["lineType"] == LABOR)
check("por lo que suman", abs(sale_labor - expected) < 0.01, f"{sale_labor} vs {expected}")

status, _ = api("POST", "/api/sales/close-work-order", {
    "workOrderId": second_order, "paymentMethod": CASH,
}, token=owner)
check("y la orden ya no se vuelve a facturar", status == 409, str(status))

third_order = new_order([first["id"]])

status, _ = api("POST", "/api/sales/close-work-order", {
    "workOrderId": third_order, "paymentMethod": CASH,
    "laborFromQuoteId": quote["id"],
}, token=owner)
check("una cotización de otra orden se rechaza", status == 400, str(status))

status, _ = api("POST", "/api/sales/close-work-order", {
    "workOrderId": third_order, "paymentMethod": CASH,
    "laborFromQuoteId": "00000000-0000-0000-0000-000000000002",
}, token=owner)
check("y una que no existe también", status == 404, str(status))

# Sin mano de obra y sin repuestos no hay nada que cobrar: la orden no se cierra.
status, _ = api("POST", "/api/sales/close-work-order", {
    "workOrderId": third_order, "paymentMethod": CASH, "includeLabor": False,
}, token=owner)
check("sin nada que cobrar no se factura", status == 400, str(status))

_, stock = api("GET", f"/api/stock?branchId={matriz['id']}&pageSize=100", token=owner)
available = next(s for s in stock["items"] if s["quantity"] >= 1)
api("POST", f"/api/work-orders/{third_order}/parts", {
    "partId": available["partId"], "quantity": 1,
}, token=owner)

status, sale = api("POST", "/api/sales/close-work-order", {
    "workOrderId": third_order, "paymentMethod": CASH, "includeLabor": False,
}, token=owner)
check("con repuestos sí, y sin mano de obra", status == 201, f"{status} {sale}")
check("la factura solo trae repuestos",
      all(l["lineType"] == PART for l in sale["lines"]),
      str([l["lineType"] for l in sale["lines"]]))

print(f"\n{ok} comprobaciones bien, {len(failed)} mal")
if failed:
    for name in failed:
        print(f"  · {name}")
    sys.exit(1)
