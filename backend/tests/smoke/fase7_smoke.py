#!/usr/bin/env python3
"""Humo de la Fase 7: recepción en el mostrador, diagnóstico y documentos en PDF.

Comprueba que el taller —no solo el cliente— puede registrar un requerimiento, que el
diagnóstico se guarda desde la orden, y que la cotización y la factura salen en PDF con la
sesión puesta y solo para quien le corresponde. Escribe en la base: va contra el entorno
local.

    python3 backend/tests/smoke/fase7_smoke.py
"""

import json
import time
import sys
import urllib.error
import urllib.request

BASE = "http://localhost:5080"
PASSWORD = "Garaj123!"

# Espejo de Garaj.Domain.Enums
MOTORCYCLE = 2
PENDING, CONVERTED = 1, 5
DIAGNOSING = 2
CASH = 1

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


print("Fase 7 — recepción, diagnóstico y documentos\n")

owner = login("owner@garaj.test")
tech_matriz = login("tecnico1@garaj.test")
tech_norte = login("tecnico2@garaj.test")
customer = login("cliente@garaj.test")

_, branches = api("GET", "/api/branches", token=owner)
matriz = next(b for b in branches if b["code"] == "MTZ")
norte = next(b for b in branches if b["code"] != "MTZ")

# ------------------------------------------------- el taller registra desde el mostrador

print("[recepción: el técnico da de alta cliente y vehículo]")

# La placa es única en el taller: se le pone un sufijo del reloj para poder repetir el humo
# sin sembrar de nuevo la base.
suffix = str(int(time.time()))[-5:]

status, new_customer = api("POST", "/api/customers", {
    "fullName": f"Recepción Humo {suffix}",
    "phone": f"5049{suffix}1",
}, token=tech_matriz)
check("el técnico registra un cliente", status == 201, f"{status} {new_customer}")

status, new_vehicle = api("POST", "/api/vehicles", {
    "customerId": new_customer["id"],
    "type": MOTORCYCLE,
    "brand": "Honda",
    "model": "CB 125",
    "plate": f"HUM{suffix}",
}, token=tech_matriz)
check("y el vehículo que trae", status == 201, f"{status} {new_vehicle}")

status, request = api("POST", "/api/service-requests", {
    "branchId": matriz["id"],
    "vehicleId": new_vehicle["id"],
    "description": "Cambio de aceite y revisión de cadena",
    "reportedSymptoms": "Se salta la cadena en segunda",
    "mileage": 12500,
}, token=tech_matriz)
check("y el requerimiento en su sucursal", status == 201, f"{status} {request}")
check("queda pendiente de aprobar", request["status"] == PENDING, str(request.get("status")))

status, _ = api("POST", "/api/service-requests", {
    "branchId": norte["id"],
    "vehicleId": new_vehicle["id"],
    "description": "Intento en una sucursal ajena",
}, token=tech_matriz)
check("pero no en una sucursal donde no trabaja", status == 404, str(status))

print("\n[qué ve cada uno de esa bandeja]")

_, page = api("GET", "/api/service-requests?pageSize=100", token=tech_matriz)
ids = [r["id"] for r in page["items"]]
check("el técnico ve lo que acaba de recibir", request["id"] in ids, str(len(ids)))
check("y solo de sus sucursales",
      all(r["branchId"] == matriz["id"] for r in page["items"]),
      str({r["branchName"] for r in page["items"]}))

_, other = api("GET", "/api/service-requests?pageSize=100", token=tech_norte)
check("el técnico de otra sucursal no lo ve",
      request["id"] not in [r["id"] for r in other["items"]])

status, _ = api("POST", f"/api/service-requests/{request['id']}/approve", {},
                token=tech_matriz)
check("y aprobar sigue siendo del Dueño", status == 403, str(status))

status, _ = api("POST", "/api/customers", {
    "fullName": "Cliente Intruso", "phone": "50497770002",
}, token=customer)
check("un cliente no registra clientes", status == 403, str(status))

# ------------------------------------------------------------------ el diagnóstico

print("\n[diagnóstico]")

status, approved = api("POST", f"/api/service-requests/{request['id']}/approve", {
    "assignedTechnicianId": None,
}, token=owner)
check("el Dueño lo convierte en orden", status == 200, f"{status} {approved}")
order_id = approved["workOrderId"]

_, order = api("GET", f"/api/work-orders/{order_id}", token=owner)
check("la orden nace sin diagnóstico", order.get("diagnosis") is None, str(order.get("diagnosis")))

_, order = api("PUT", f"/api/work-orders/{order_id}", {
    "description": order["description"],
    "diagnosis": "Cadena estirada fuera de tolerancia; corona con dientes gastados.",
    "promisedAt": order.get("promisedAt"),
}, token=owner)
check("el Dueño lo escribe", "Cadena estirada" in (order.get("diagnosis") or ""),
      str(order.get("diagnosis")))
check("y no le borra el motivo de ingreso", "aceite" in order["description"])

# El técnico asignado también: es quien revisa el vehículo y quien lo sabe.
_, _ = api("PUT", f"/api/work-orders/{order_id}/assign",
           {"technicianId": None}, token=owner)
_, techs = api("GET", "/api/users?role=Technician", token=owner)
tech_id = next(t["id"] for t in techs if t["email"] == "tecnico1@garaj.test")
api("PUT", f"/api/work-orders/{order_id}/assign", {"technicianId": tech_id}, token=owner)

status, updated = api("PUT", f"/api/work-orders/{order_id}", {
    "description": order["description"],
    "diagnosis": "Cadena estirada; hay que cambiar el kit de arrastre completo.",
}, token=tech_matriz)
check("el técnico asignado también lo escribe", status == 200, f"{status} {updated}")
check("y queda guardado", "kit de arrastre" in (updated.get("diagnosis") or ""))

status, _ = api("PUT", f"/api/work-orders/{order_id}", {
    "description": order["description"],
    "diagnosis": "El cliente escribiendo su propio diagnóstico",
}, token=customer)
check("el cliente no puede escribirlo", status in (403, 404), str(status))

# ----------------------------------------------------------------- PDF de cotización

print("\n[PDF de la cotización]")

_, quote = api("POST", "/api/quotes/from-work-order", {
    "workOrderId": order_id, "includeTasks": True, "includeParts": True,
}, token=owner)
quote_id = quote["id"]

# Sin líneas no hay nada que cotizar: se agrega una de mano de obra libre.
_, quote = api("POST", f"/api/quotes/{quote_id}/lines", {
    "lineType": 2,
    "description": "Cambio de kit de arrastre",
    "quantity": 1,
    "unitPrice": 850,
}, token=owner)

status, pdf = api("GET", f"/api/quotes/{quote_id}/pdf", token=owner, raw=True)
check("el Dueño lo baja con su sesión", status == 200, str(status))
check("y es un PDF de verdad", isinstance(pdf, bytes) and pdf[:4] == b"%PDF", str(pdf[:16]))

status, _ = api("GET", f"/api/quotes/{quote_id}/pdf", token=None, raw=True)
check("sin sesión responde 401 (era el enlace roto)", status == 401, str(status))

status, _ = api("GET", f"/api/quotes/{quote_id}/pdf", token=tech_matriz, raw=True)
check("el técnico no ve cotizaciones", status in (403, 404), str(status))

_, link = api("POST", f"/api/quotes/{quote_id}/send", token=owner)
token_public = link["url"].split("/q/")[-1].split("?")[0] if "/q/" in link["url"] else None
if token_public is None:
    _, detail = api("GET", f"/api/quotes/{quote_id}", token=owner)
    token_public = detail["publicUrl"].rsplit("/", 1)[-1]

status, pdf = api("GET", f"/public/quotes/{token_public}/pdf", raw=True)
check("la ruta pública lo sirve sin sesión", status == 200, str(status))
check("y también es un PDF", isinstance(pdf, bytes) and pdf[:4] == b"%PDF", str(pdf[:16]))

# ------------------------------------------------------------------- PDF de factura

print("\n[PDF de la factura]")

# Una venta necesita algo que cobrar: se carga un repuesto que haya en la bodega de Matriz.
_, stock = api("GET", f"/api/stock?branchId={matriz['id']}&pageSize=100", token=owner)
available = next(s for s in stock["items"] if s["quantity"] >= 1)
status, _ = api("POST", f"/api/work-orders/{order_id}/parts", {
    "partId": available["partId"],
    "quantity": 1,
}, token=owner)
check("se carga un repuesto a la orden", status in (200, 201), str(status))

status, sale = api("POST", "/api/sales/close-work-order", {
    "workOrderId": order_id,
    "paymentMethod": CASH,
    "includeLabor": True,
    "markAsDelivered": True,
}, token=owner)
check("se cierra la orden y se factura", status == 201, f"{status} {sale}")

status, pdf = api("GET", f"/api/sales/{sale['id']}/pdf", token=owner, raw=True)
check("el Dueño baja la factura", status == 200, str(status))
check("y es un PDF", isinstance(pdf, bytes) and pdf[:4] == b"%PDF", str(pdf[:16]))
check("con peso razonable", isinstance(pdf, bytes) and len(pdf) > 1000, str(len(pdf)))

status, _ = api("GET", f"/api/sales/{sale['id']}/pdf", token=None, raw=True)
check("sin sesión no se baja", status == 401, str(status))

status, _ = api("GET", f"/api/sales/{sale['id']}/pdf", token=tech_matriz, raw=True)
check("el técnico no ve facturas", status in (403, 404), str(status))

status, _ = api("GET", f"/api/sales/{sale['id']}/pdf", token=customer, raw=True)
check("y un cliente ajeno tampoco", status == 404, str(status))

# ---------------------------------------------------------------------------- fin

print(f"\n{ok} comprobaciones bien, {len(failed)} mal")
if failed:
    for name in failed:
        print(f"  · {name}")
    sys.exit(1)
