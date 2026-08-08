#!/usr/bin/env python3
"""Humo de la Fase 6: avisos, dispositivos y la cita que pide el cliente.

Comprueba que cada hecho del taller genera el aviso correcto en la campana de quien
corresponde, que nadie ve ni marca los avisos de otro, y que el Cliente puede abrir un
requerimiento con foto desde su app. Escribe en la base: va contra el entorno local.

    python3 backend/tests/smoke/fase6_smoke.py
"""

import json
import sys
import urllib.error
import urllib.request

BASE = "http://localhost:5080"
PASSWORD = "Garaj123!"

# Espejo de Garaj.Domain.Enums
SERVICE_REQUEST_CREATED, WORK_ORDER_ASSIGNED, WORK_ORDER_STATUS_CHANGED = 1, 2, 3
QUOTE_SENT, QUOTE_ANSWERED = 4, 5
ANDROID = 1
DIAGNOSING = 2
SERVICE_REQUEST_MEDIA = 1

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


def api(method, path, body=None, token=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")

    try:
        with urllib.request.urlopen(req) as r:
            payload = r.read()
            try:
                return r.status, json.loads(payload) if payload else None
            except (ValueError, UnicodeDecodeError):
                return r.status, payload
    except urllib.error.HTTPError as e:
        payload = e.read()
        try:
            return e.code, json.loads(payload)
        except (ValueError, UnicodeDecodeError):
            return e.code, payload


def login(email):
    status, data = api("POST", "/api/auth/login", {"email": email, "password": PASSWORD})
    if status != 200:
        sys.exit(f"No se pudo entrar como {email}: {status} {data}")
    return data["accessToken"]


def inbox(token, only_unread=False):
    query = "?pageSize=50" + ("&onlyUnread=true" if only_unread else "")
    _, page = api("GET", "/api/notifications" + query, token=token)
    return page["items"]


def has(items, kind, *, contains=None):
    return any(
        n["type"] == kind and (contains is None or contains in n["title"] + n["body"])
        for n in items
    )


print("Fase 6 — avisos y app del cliente\n")

owner = login("owner@garaj.test")
technician = login("tecnico1@garaj.test")
customer = login("cliente@garaj.test")

# ---------------------------------------------------------------- la campana existe

print("[campana]")

status, page = api("GET", "/api/notifications?pageSize=5", token=owner)
check("la bandeja del Dueño responde", status == 200, f"{status} {page}")
check("viene paginada", isinstance(page, dict) and "items" in page and "total" in page)

status, count = api("GET", "/api/notifications/unread-count", token=technician)
check("el contador de no leídos responde", status == 200, f"{status} {count}")
check("devuelve un número", isinstance(count.get("unread"), int), str(count))

status, _ = api("GET", "/api/notifications", token=None)
check("sin sesión no hay avisos", status == 401, str(status))

# ------------------------------------------------- el cliente pide una cita desde la app

print("\n[el cliente pide cita]")

_, vehicles = api("GET", "/api/vehicles?pageSize=10", token=customer)
check("el cliente ve sus vehículos", len(vehicles["items"]) >= 1, str(vehicles.get("total")))
vehicle = vehicles["items"][0]

_, branches = api("GET", "/api/branches", token=customer)
check("y las sucursales donde dejarlo", len(branches) >= 1, str(len(branches)))
# La Matriz a propósito: es donde trabaja tecnico1, y más adelante hay que asignarle la
# orden. Un técnico de otra sucursal la API lo rechaza, y con razón.
branch = next(b for b in branches if b["code"] == "MTZ")

before = len(inbox(owner, only_unread=True))

status, request = api("POST", "/api/service-requests", {
    "branchId": branch["id"],
    "vehicleId": vehicle["id"],
    "description": "Revisión de frenos: chilla al frenar en bajada",
    "reportedSymptoms": "Un chillido metálico, sobre todo en frío",
    "mileage": (vehicle.get("mileage") or 0) + 500,
}, token=customer)
check("crea el requerimiento", status == 201, f"{status} {request}")
check("queda pendiente", request["status"] == 1, str(request.get("status")))

owner_inbox = inbox(owner)
check("al Dueño le llega el aviso",
      has(owner_inbox, SERVICE_REQUEST_CREATED, contains="frenos"),
      str([n["title"] for n in owner_inbox[:3]]))
check("y le sube el contador de no leídos",
      len(inbox(owner, only_unread=True)) > before)

check("el aviso apunta al requerimiento",
      any(n["serviceRequestId"] == request["id"] for n in owner_inbox),
      request["id"])

# El técnico no participa en esta etapa: no debe enterarse de nada.
check("el Técnico no recibe ese aviso",
      not has(inbox(technician), SERVICE_REQUEST_CREATED, contains="frenos"))

status, presigned = api("POST", "/api/media/upload-url", {
    "ownerType": SERVICE_REQUEST_MEDIA,
    "ownerId": request["id"],
    "contentType": "image/jpeg",
    "sizeBytes": 4096,
    "fileName": "frenos.jpg",
}, token=customer)
check("el cliente puede adjuntar foto a su requerimiento", status == 200, f"{status} {presigned}")

# --------------------------------------------------------- asignar avisa al técnico

print("\n[asignación]")

_, users = api("GET", "/api/users?role=Technician", token=owner)
tech_id = next(u["id"] for u in users if u["email"] == "tecnico1@garaj.test")

status, work_order_id = api(
    "POST", f"/api/service-requests/{request['id']}/approve",
    {"assignedTechnicianId": tech_id}, token=owner)
check("el Dueño aprueba y abre la orden", status == 200, f"{status} {work_order_id}")
order_id = work_order_id["workOrderId"]

tech_inbox = inbox(technician)
check("al Técnico le llega la asignación",
      has(tech_inbox, WORK_ORDER_ASSIGNED),
      str([n["title"] for n in tech_inbox[:3]]))
check("el aviso lleva a la orden",
      any(n["workOrderId"] == order_id for n in tech_inbox), order_id)

# --------------------------------------------- el cambio de estado avisa al cliente

print("\n[cambio de estado]")

status, _ = api("POST", f"/api/work-orders/{order_id}/status", {
    "status": DIAGNOSING,
    "note": "Entró a diagnóstico",
    "isVisibleToCustomer": True,
}, token=technician)
check("el Técnico cambia el estado", status == 200, str(status))

customer_inbox = inbox(customer)
check("al Cliente le llega el avance",
      has(customer_inbox, WORK_ORDER_STATUS_CHANGED),
      str([n["title"] for n in customer_inbox[:3]]))

hidden_before = len(inbox(customer))
status, _ = api("POST", f"/api/work-orders/{order_id}/status", {
    "status": 5,
    "note": "Nota interna: el cliente regatea",
    "isVisibleToCustomer": False,
}, token=technician)
check("un cambio marcado como interno se acepta", status == 200, str(status))
check("pero no genera aviso al cliente", len(inbox(customer)) == hidden_before,
      f"{hidden_before} → {len(inbox(customer))}")

# -------------------------------------------------------------- cotización

print("\n[cotización]")

status, quote = api("POST", "/api/quotes/from-work-order",
                    {"workOrderId": order_id, "includeParts": True, "includeTasks": True},
                    token=owner)
check("se crea la cotización", status in (200, 201), f"{status} {quote}")

_, labor = api("GET", "/api/labor-services", token=owner)
api("POST", f"/api/quotes/{quote['id']}/lines", {
    "lineType": 2,
    "laborServiceId": labor[0]["id"],
    "quantity": 1,
    "description": labor[0]["name"],
}, token=owner)

status, link = api("POST", f"/api/quotes/{quote['id']}/send", token=owner)
check("se envía", status == 200, f"{status} {link}")

check("al Cliente le llega la cotización",
      has(inbox(customer), QUOTE_SENT, contains=quote["number"]),
      quote["number"])

owner_before = len(inbox(owner, only_unread=True))

_, detail = api("GET", f"/api/quotes/{quote['id']}", token=owner)
# El detalle expone el link, no el token: se saca del final de la URL, que es como lo
# recibe el cliente en WhatsApp.
token = detail["publicUrl"].rsplit("/", 1)[-1]
status, public = api("POST", f"/public/quotes/{token}/respond",
                     {"approve": True, "note": "De acuerdo, procedan"})
check("el cliente responde desde el link público", status == 200, f"{status} {public}")

check("al Dueño le avisa la respuesta",
      has(inbox(owner), QUOTE_ANSWERED, contains=quote["number"]),
      quote["number"])
check("y le sube el contador", len(inbox(owner, only_unread=True)) > owner_before)

# --------------------------------------------------------- marcar leído y aislamiento

print("\n[leído y aislamiento]")

owner_items = inbox(owner)
target = owner_items[0]

status, _ = api("POST", f"/api/notifications/{target['id']}/read", token=owner)
check("el Dueño marca uno leído", status == 204, str(status))
check("queda marcado", next(n for n in inbox(owner) if n["id"] == target["id"])["isRead"])

# Marcar el aviso de otro devuelve 404 y no 403: un 403 confirmaría que ese id existe.
status, _ = api("POST", f"/api/notifications/{target['id']}/read", token=technician)
check("el aviso ajeno no existe para el Técnico", status == 404, str(status))

check("y no aparece en su bandeja",
      not any(n["id"] == target["id"] for n in inbox(technician)))

status, marked = api("POST", "/api/notifications/read-all", token=customer)
check("el Cliente marca todo leído", status == 200, f"{status} {marked}")
check("le queda el contador en cero", len(inbox(customer, only_unread=True)) == 0)

# ------------------------------------------------------------------ dispositivos

print("\n[dispositivos]")

status, _ = api("POST", "/api/notifications/devices",
                {"token": "token-de-humo-fase6", "platform": ANDROID}, token=technician)
check("el Técnico registra su aparato", status == 204, str(status))

# Reenviar el mismo token en cada arranque es lo normal: no debe duplicar ni fallar.
status, _ = api("POST", "/api/notifications/devices",
                {"token": "token-de-humo-fase6", "platform": ANDROID}, token=technician)
check("reenviarlo no duplica ni falla", status == 204, str(status))

# El mismo aparato en manos de otro usuario se reasigna, no se rechaza.
status, _ = api("POST", "/api/notifications/devices",
                {"token": "token-de-humo-fase6", "platform": ANDROID}, token=owner)
check("cambiar de dueño reasigna el aparato", status == 204, str(status))

status, _ = api("DELETE", "/api/notifications/devices/token-de-humo-fase6", token=owner)
check("y al salir se da de baja", status == 204, str(status))

status, _ = api("POST", "/api/notifications/devices",
                {"token": "   ", "platform": ANDROID}, token=owner)
check("un token vacío se rechaza", status == 400, str(status))

# ------------------------------------------------------------------ siembra de demo

print("\n[siembra de demostración]")

# Apagada por defecto: sin `Demo:AllowSeeding` el endpoint no existe para nadie, ni siquiera
# para el Dueño. Es lo que impide borrar una base real por equivocación.
status, _ = api("POST", "/api/demo/seed", {"confirm": "BORRAR Y SEMBRAR"}, token=owner)
check("apagada, la siembra no existe", status == 404, str(status))

status, _ = api("POST", "/api/demo/seed", {"confirm": "BORRAR Y SEMBRAR"}, token=technician)
check("y un Técnico nunca puede llamarla", status == 403, str(status))

print(f"\n{ok} comprobaciones correctas, {len(failed)} fallidas")
if failed:
    for name in failed:
        print(f"  - {name}")
    sys.exit(1)
