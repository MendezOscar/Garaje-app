#!/usr/bin/env python3
"""Humo de la Fase 1: recorre el flujo real del taller y comprueba el alcance por perfil."""
import json
import sys
import urllib.error
import urllib.request

BASE = "http://localhost:5080"
passed, failed = 0, 0


def call(method, path, token=None, body=None):
    req = urllib.request.Request(BASE + path, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
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


def check(label, expected, actual):
    global passed, failed
    if expected == actual:
        print(f"  OK    {label}")
        passed += 1
    else:
        print(f"  FALLA {label} — esperaba {expected!r}, obtuvo {actual!r}")
        failed += 1


def login(user):
    status, data = call("POST", "/api/auth/login",
                        body={"email": f"{user}@garaj.test", "password": "Garaj123!"})
    assert status == 200, f"login {user} devolvió {status}: {data}"
    return data["accessToken"]


owner, tech1, tech2, client = (login(u) for u in ("owner", "tecnico1", "tecnico2", "cliente"))

print("== Alcance de sucursales ==")
check("Dueño ve las 2 sucursales", 2, len(call("GET", "/api/branches", owner)[1]))
check("Técnico de Matriz ve solo la suya", 1, len(call("GET", "/api/branches", tech1)[1]))
check("Cliente ve todas (elige dónde deja el vehículo)", 2, len(call("GET", "/api/branches", client)[1]))

print("== Alcance de clientes ==")
check("Dueño ve los 3 clientes", 3, call("GET", "/api/customers", owner)[1]["total"])
check("Cliente solo se ve a sí mismo", 1, call("GET", "/api/customers", client)[1]["total"])

print("== Búsqueda ==")
found = call("GET", "/api/customers?search=pbh-1234", owner)[1]["items"]
check("buscar por placa con guiones encuentra al dueño del vehículo", "María Torres",
      found[0]["fullName"] if found else None)
check("buscar por teléfono parcial", 1, call("GET", "/api/customers?search=98881111", owner)[1]["total"])

print("== Alcance de órdenes de trabajo ==")
check("Dueño ve las 2 órdenes", 2, call("GET", "/api/work-orders", owner)[1]["total"])
check("Técnico 1 ve solo la suya", 1, call("GET", "/api/work-orders", tech1)[1]["total"])
check("Técnico 2 ve solo la suya", 1, call("GET", "/api/work-orders", tech2)[1]["total"])
check("Cliente ve las de sus vehículos", 1, call("GET", "/api/work-orders", client)[1]["total"])

wo1 = call("GET", "/api/work-orders", tech1)[1]["items"][0]["id"]
wo2 = call("GET", "/api/work-orders", tech2)[1]["items"][0]["id"]

print("== Aislamiento entre técnicos ==")
check("Técnico 1 no abre la orden del Técnico 2 (404, no 403: no revela que existe)",
      404, call("GET", f"/api/work-orders/{wo2}", tech1)[0])

print("== Transiciones de estado ==")
check("InProgress → Ready es válido", 200,
      call("POST", f"/api/work-orders/{wo1}/status", tech1,
           {"status": 7, "note": "Trabajo terminado"})[0])
check("Ready → Diagnosing es inválido (409)", 409,
      call("POST", f"/api/work-orders/{wo1}/status", tech1, {"status": 2})[0])
check("Cliente no cambia estados (403)", 403,
      call("POST", f"/api/work-orders/{wo1}/status", client, {"status": 8})[0])

print("== Línea de tiempo curada para el cliente ==")
call("POST", f"/api/work-orders/{wo1}/status", owner,
     {"status": 8, "note": "Nota interna", "isVisibleToCustomer": False})
owner_tl = len(call("GET", f"/api/work-orders/{wo1}", owner)[1]["timeline"])
client_tl = len(call("GET", f"/api/work-orders/{wo1}", client)[1]["timeline"])
check("la nota interna no llega al cliente", client_tl + 1, owner_tl)
check("el cliente no recibe estados sugeridos", [],
      call("GET", f"/api/work-orders/{wo1}", client)[1]["allowedNextStatuses"])

print("== Requerimiento → orden de trabajo ==")
vehicle = call("GET", "/api/vehicles", client)[1]["items"][0]["id"]
branch = call("GET", "/api/branches", client)[1][0]["id"]
status, req = call("POST", "/api/service-requests", client, {
    "branchId": branch, "vehicleId": vehicle,
    "description": "Ruido en suspensión", "reportedSymptoms": "Al pasar túmulos", "mileage": 79000})
check("el Cliente crea el requerimiento", 201, status)
check("nace pendiente", 1, req["status"])

techs = call("GET", "/api/users?role=Technician", owner)[1]
matriz = call("GET", "/api/branches", tech1)[1][0]["id"]
tech_matriz = next(t for t in techs if matriz in t["branchIds"])
tech_otra = next(t for t in techs if matriz not in t["branchIds"])

status, approved = call("POST", f"/api/service-requests/{req['id']}/approve", owner,
                        {"assignedTechnicianId": tech_matriz["id"]})
check("aprobar devuelve la orden creada", 200, status)
new_wo = approved["workOrderId"]
check("aprobar dos veces da 409", 409,
      call("POST", f"/api/service-requests/{req['id']}/approve", owner, {})[0])
check("el requerimiento queda enlazado", new_wo,
      call("GET", f"/api/service-requests/{req['id']}", owner)[1]["workOrderId"])

detail = call("GET", f"/api/work-orders/{new_wo}", owner)[1]
check("el número lleva prefijo de sucursal", True,
      detail["number"].split("-")[0] in ("MTZ", "SPS"))
check("nace en Recibida", 1, detail["status"])
check("la línea de tiempo arranca con la recepción", 1, len(detail["timeline"]))

print("== Pasos de la orden ==")
status, task = call("POST", f"/api/work-orders/{new_wo}/tasks", tech1,
                    {"title": "Revisar amortiguadores", "estimatedHours": 1.5})
check("el Técnico agrega un paso", 200, status)
check("el paso hereda el técnico de la orden", tech_matriz["id"], task["assignedTechnicianId"])
done = call("POST", f"/api/work-orders/{new_wo}/tasks/{task['id']}/complete", tech1,
            {"isDone": True, "actualHours": 2})[1]
check("completar registra la hora de inicio aunque no se haya iniciado", True,
      done["startedAt"] is not None)
check("y la de fin", True, done["completedAt"] is not None)

print("== Validaciones de asignación ==")
check("asignar a un técnico de otra sucursal falla (400)", 400,
      call("PUT", f"/api/work-orders/{new_wo}/assign", owner,
           {"technicianId": tech_otra["id"]})[0])
check("el Técnico no puede asignar (403)", 403,
      call("PUT", f"/api/work-orders/{new_wo}/assign", tech1,
           {"technicianId": tech_matriz["id"]})[0])

print("== Validaciones de vehículo ==")
cust = call("GET", "/api/customers", owner)[1]["items"][0]["id"]
check("placa repetida con otro formato da 409", 409,
      call("POST", "/api/vehicles", owner,
           {"customerId": cust, "type": 1, "brand": "Kia", "model": "Rio", "plate": "pbh 1234"})[0])
check("el Técnico no registra vehículos (403)", 403,
      call("POST", "/api/vehicles", tech1,
           {"customerId": cust, "type": 1, "brand": "Kia", "model": "Rio"})[0])

print("== Normalización de teléfono ==")
status, created = call("POST", "/api/customers", owner,
                       {"fullName": "Prueba Formato", "phone": "9876-5432"})
check("el teléfono local se guarda en E.164 para wa.me", "50498765432", created["phone"])

print()
print("==========================")
print(f" {passed} OK, {failed} fallas")
print("==========================")
sys.exit(1 if failed else 0)
