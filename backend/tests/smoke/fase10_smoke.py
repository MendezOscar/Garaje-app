#!/usr/bin/env python3
"""Humo de la Fase 10: usuarios del taller y acceso de los clientes.

Comprueba que el Dueño da de alta técnicos con sus sucursales, les cambia la contraseña y los
da de baja; que el acceso a la app de un cliente es opcional y se abre desde su ficha; y que
nadie más puede tocar nada de esto. Escribe en la base: va contra el entorno local.

    python3 backend/tests/smoke/fase10_smoke.py
"""

import json
import sys
import time
import urllib.error
import urllib.request

BASE = "http://localhost:5080"
PASSWORD = "Garaj123!"

MOTORCYCLE = 2

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


def login(email, password=PASSWORD):
    return api("POST", "/api/auth/login", {"email": email, "password": password})


def token_of(email, password=PASSWORD):
    status, data = login(email, password)
    if status != 200:
        sys.exit(f"No se pudo entrar como {email}: {status} {data}")
    return data["accessToken"]


print("Fase 10 — usuarios y acceso a la app\n")

owner = token_of("owner@garaj.test")
technician = token_of("tecnico1@garaj.test")
customer = token_of("cliente@garaj.test")

_, branches = api("GET", "/api/branches", token=owner)
matriz = next(b for b in branches if b["code"] == "MTZ")
norte = next(b for b in branches if b["code"] != "MTZ")

suffix = str(int(time.time()))[-5:]

# ------------------------------------------------------------------ alta de técnico

print("[el Dueño da de alta a un técnico]")

new_email = f"tecnico.humo{suffix}@garaj.test"
new_password = "Taller2026!"

status, created = api("POST", "/api/users", {
    "email": new_email,
    "fullName": f"Humo Técnico {suffix}",
    "role": "Technician",
    "password": new_password,
    "branchIds": [matriz["id"]],
}, token=owner)
check("se crea el usuario", status == 201, f"{status} {created}")
check("con perfil de técnico", created["role"] == "Technician", str(created.get("role")))
check("y su sucursal", created["branchIds"] == [matriz["id"]], str(created.get("branchIds")))
check("nace activo", created["isActive"] is True)
check("y sin haber entrado nunca", created["lastLoginAt"] is None)

new_id = created["id"]

status, _ = api("POST", "/api/users", {
    "email": new_email, "fullName": "Correo repetido", "role": "Technician",
    "password": new_password, "branchIds": [matriz["id"]],
}, token=owner)
check("el correo no se repite", status == 409, str(status))

status, _ = api("POST", "/api/users", {
    "email": f"debil{suffix}@garaj.test", "fullName": "Contraseña débil",
    "role": "Technician", "password": "123", "branchIds": [matriz["id"]],
}, token=owner)
check("una contraseña débil se rechaza", status == 400, str(status))

status, _ = api("POST", "/api/users", {
    "email": f"perfil{suffix}@garaj.test", "fullName": "Perfil inventado",
    "role": "Mecanico", "password": new_password,
}, token=owner)
check("un perfil inventado también", status == 400, str(status))

# ------------------------------------------------------------------ entra y trabaja

print("\n[el técnico nuevo entra]")

status, session = login(new_email, new_password)
check("puede entrar con lo que se le entregó", status == 200, str(status))
check("y la app le dice que es técnico",
      session and session["user"]["role"] == "Technician",
      str(session.get("user", {}).get("role") if session else None))

new_token = session["accessToken"]

_, mine = api("GET", "/api/work-orders?onlyOpen=true&pageSize=50", token=new_token)
check("su bandeja arranca vacía: no tiene asignaciones", mine["total"] == 0, str(mine["total"]))

status, _ = api("GET", "/api/users", token=new_token)
check("un técnico no lista usuarios", status == 403, str(status))

status, _ = api("POST", "/api/users", {
    "email": f"colado{suffix}@garaj.test", "fullName": "Colado",
    "role": "Technician", "password": new_password, "branchIds": [matriz["id"]],
}, token=technician)
check("ni crea otros", status == 403, str(status))

status, _ = api("GET", "/api/users", token=customer)
check("un cliente tampoco", status == 403, str(status))

# ------------------------------------------------------------------ el Dueño lo administra

print("\n[cambios sobre el técnico]")

status, moved = api("PUT", f"/api/users/{new_id}", {
    "fullName": f"Humo Técnico {suffix}",
    "isActive": True,
    "branchIds": [matriz["id"], norte["id"]],
}, token=owner)
check("se le agregan sucursales", len(moved["branchIds"]) == 2, str(moved.get("branchIds")))

status, _ = api("POST", f"/api/users/{new_id}/password", {
    "newPassword": "OtraClave2026!",
}, token=owner)
check("el Dueño le cambia la contraseña", status == 204, str(status))

status, _ = login(new_email, new_password)
check("la vieja deja de servir", status == 401, str(status))

status, _ = login(new_email, "OtraClave2026!")
check("y la nueva sirve", status == 200, str(status))

status, disabled = api("PUT", f"/api/users/{new_id}", {
    "fullName": moved["fullName"], "isActive": False, "branchIds": moved["branchIds"],
}, token=owner)
check("se le da de baja", disabled["isActive"] is False, str(disabled.get("isActive")))

status, _ = login(new_email, "OtraClave2026!")
check("y ya no entra", status == 401, str(status))

_, users = api("GET", "/api/users?role=Technician", token=owner)
check("pero sigue en la lista, para poder reactivarlo",
      new_id in [u["id"] for u in users], str(len(users)))

owner_id = next(u["id"] for u in api("GET", "/api/users?role=Owner", token=owner)[1])
status, _ = api("PUT", f"/api/users/{owner_id}", {
    "fullName": "Dueño", "isActive": False, "branchIds": [],
}, token=owner)
check("el Dueño no puede desactivarse a sí mismo", status == 400, str(status))

# ------------------------------------------------------------------ acceso del cliente

print("\n[acceso a la app de un cliente]")

_, buyer = api("POST", "/api/customers", {
    "fullName": f"Cliente Sin App {suffix}",
    "phone": f"5049{suffix}7",
}, token=owner)
check("un cliente nace sin acceso", buyer["hasAppAccess"] is False,
      str(buyer.get("hasAppAccess")))
check("y sin correo de entrada", buyer["appUserEmail"] is None, str(buyer.get("appUserEmail")))

client_email = f"cliente.humo{suffix}@garaj.test"

status, _ = api("POST", f"/api/customers/{buyer['id']}/app-access", {
    "email": client_email, "password": new_password,
}, token=technician)
check("el técnico no abre accesos", status == 403, str(status))

status, granted = api("POST", f"/api/customers/{buyer['id']}/app-access", {
    "email": client_email, "password": new_password,
}, token=owner)
check("el Dueño sí", status == 200, f"{status} {granted}")
check("y la ficha lo refleja", granted["hasAppAccess"] is True, str(granted.get("hasAppAccess")))
check("con el correo con el que entra", granted["appUserEmail"] == client_email,
      str(granted.get("appUserEmail")))

status, session = login(client_email, new_password)
check("el cliente entra", status == 200, str(status))
check("con perfil de cliente",
      session and session["user"]["role"] == "Customer",
      str(session.get("user", {}).get("role") if session else None))

client_token = session["accessToken"]

_, vehicles = api("GET", "/api/vehicles?pageSize=50", token=client_token)
check("y solo ve lo suyo, que todavía no es nada", vehicles["total"] == 0,
      str(vehicles["total"]))

status, _ = api("POST", f"/api/customers/{buyer['id']}/app-access", {
    "email": f"otro{suffix}@garaj.test", "password": new_password,
}, token=owner)
check("no se le abre un segundo acceso", status == 409, str(status))

_, listed = api("GET", f"/api/customers?search={suffix}&pageSize=10", token=owner)
found = next((c for c in listed["items"] if c["id"] == buyer["id"]), None)
check("el listado también dice quién tiene app",
      found is not None and found["hasAppAccess"] is True,
      str(found))

status, _ = api("POST", "/api/customers/00000000-0000-0000-0000-000000000009/app-access", {
    "email": f"fantasma{suffix}@garaj.test", "password": new_password,
}, token=owner)
check("un cliente inexistente da 404", status == 404, str(status))

# ------------------------------------------------------------------ borrar la propia cuenta

print("\n[el usuario borra su propia cuenta]")

status, _ = api("POST", "/api/auth/delete-account", {"confirm": "por favor"}, token=client_token)
check("sin la confirmación exacta no se borra nada", status == 400, str(status))

status, _ = login(client_email, new_password)
check("y la cuenta sigue sirviendo", status == 200, str(status))

status, _ = api("POST", "/api/auth/delete-account", {"confirm": "ELIMINAR"}, token=client_token)
check("con la confirmación se borra", status == 204, str(status))

status, _ = login(client_email, new_password)
check("el correo ya no entra", status == 401, str(status))

# Lo que importa del diseño: se borra el acceso, no el cliente del taller. Sus vehículos, sus
# órdenes y sus facturas son del taller y la ley obliga a conservarlas.
status, ficha = api("GET", f"/api/customers/{buyer['id']}", token=owner)
check("el taller conserva la ficha del cliente", status == 200 and ficha["fullName"].startswith("Cliente Sin App"),
      str(status))
check("y queda sin acceso a la app", ficha["hasAppAccess"] is False, str(ficha.get("hasAppAccess")))
check("sin correo de entrada", ficha["appUserEmail"] is None, str(ficha.get("appUserEmail")))

# Y como la ficha quedó libre, el taller puede volver a darle acceso si el cliente vuelve.
status, revivido = api("POST", f"/api/customers/{buyer['id']}/app-access", {
    "email": f"cliente.vuelve{suffix}@garaj.test", "password": new_password,
}, token=owner)
check("el taller puede volver a darle acceso", status == 200, str(status))
check("con el correo nuevo", revivido["appUserEmail"] == f"cliente.vuelve{suffix}@garaj.test",
      str(revivido.get("appUserEmail")))

print(f"\n{ok} comprobaciones bien, {len(failed)} mal")
if failed:
    for name in failed:
        print(f"  · {name}")
    sys.exit(1)
