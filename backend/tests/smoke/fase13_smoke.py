#!/usr/bin/env python3
"""Humo de la Fase 13: cobro de la suscripción.

Vigila las dos cosas caras de equivocar: cortarle el sistema a un taller que sí pagó, y que la
llave maestra —el perfil Plataforma— pueda leer datos de un taller. Escribe en la base y deja
el taller como lo encontró: va contra el entorno local.

    python3 backend/tests/smoke/fase13_smoke.py
"""

import json
import sys
import urllib.error
import urllib.request
from datetime import date, timedelta

BASE = "http://localhost:5080"
PASSWORD = "Garaj123!"

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
            return r.status, (json.loads(payload) if payload else None)
    except urllib.error.HTTPError as e:
        payload = e.read()
        try:
            return e.code, json.loads(payload)
        except (ValueError, UnicodeDecodeError):
            return e.code, payload


def login(email, password=PASSWORD):
    return api("POST", "/api/auth/login", {"email": email, "password": password})


def token_of(email):
    status, data = login(email)
    if status != 200:
        sys.exit(f"No se pudo entrar como {email}: {status} {data}")
    return data["accessToken"]


def dia(delta):
    return (date.today() + timedelta(days=delta)).isoformat()


def vence(token_plataforma, tenant_id, paid_through, grace_days=5):
    """Deja el taller con esa fecha de vencimiento. Es como se simula el paso del tiempo."""
    return api("PUT", f"/api/platform/tenants/{tenant_id}/subscription", {
        "planName": "Básico",
        "monthlyFee": 1200,
        "paidThrough": paid_through,
        "graceDays": grace_days,
    }, token=token_plataforma)


def suscripcion_de(token):
    _, me = api("GET", "/api/auth/me", token=token)
    return me.get("subscription")


def escribe(token, etiqueta):
    """Un alta cualquiera del Dueño. Sirve de canario: o el taller puede trabajar, o no."""
    return api("POST", "/api/customers", {
        "fullName": f"Cobro {etiqueta}",
        "phone": "50499000000",
    }, token=token)


print("Fase 13 — cobro de la suscripción\n")

plataforma = token_of("plataforma@garaj.test")
owner = token_of("owner@garaj.test")
technician = token_of("tecnico1@garaj.test")
customer = token_of("cliente@garaj.test")

_, yo = api("GET", "/api/auth/me", token=owner)
tenant_id = yo["tenantId"]

# ------------------------------------------------------------------ la llave maestra

print("[el perfil Plataforma no ve datos de ningún taller]")

# No se le niega la entrada: se le devuelve vacío, que es más fuerte. El aislamiento no
# depende de que alguien se acuerde de poner un atributo en cada endpoint nuevo, sino del
# filtro por taller, y su token no tiene taller.
status, ordenes = api("GET", "/api/work-orders", token=plataforma)
check("no ve ni una orden de ningún taller",
      status == 200 and ordenes["total"] == 0, f"{status} {ordenes}")

status, clientes = api("GET", "/api/customers", token=plataforma)
check("ni un cliente", status == 200 and clientes["total"] == 0, f"{status} {clientes}")

status, _ = api("GET", "/api/stock", token=plataforma)
check("el inventario ni lo abre", status == 403, str(status))

status, _ = api("GET", "/api/tenant", token=plataforma)
check("y la ficha de un taller la tiene prohibida", status == 403, str(status))

status, _ = api("GET", "/api/platform/tenants", token=owner)
check("el Dueño de un taller no administra cobros", status == 403, str(status))

status, _ = api("GET", "/api/platform/tenants", token=technician)
check("el técnico tampoco", status == 403, str(status))

status, talleres = api("GET", "/api/platform/tenants", token=plataforma)
check("la plataforma sí lista los talleres", status == 200 and len(talleres) >= 1, str(status))

# ------------------------------------------------------------------ al día

print("\n[al día]")

status, _ = vence(plataforma, tenant_id, dia(30))
check("se le fija el vencimiento", status == 200, str(status))

info = suscripcion_de(owner)
check("el Dueño no recibe aviso estando al día", info["state"] == "Active", str(info))
check("y puede escribir", info["canWrite"] is True, str(info))

status, _ = escribe(owner, "al dia")
check("el taller trabaja", status in (200, 201), str(status))

check("el técnico no recibe datos de cobro", suscripcion_de(technician) is None)
check("el cliente tampoco", suscripcion_de(customer) is None)

# ------------------------------------------------------------------ por vencer

print("\n[por vencer]")

vence(plataforma, tenant_id, dia(3))
info = suscripcion_de(owner)
check("avisa que faltan pocos días", info["state"] == "DueSoon", str(info))
check("con los días contados", info["daysLeft"] == 3, str(info))
check("pero sigue trabajando", info["canWrite"] is True, str(info))

# ------------------------------------------------------------------ gracia

print("\n[vencido, dentro de la gracia]")

vence(plataforma, tenant_id, dia(-2), grace_days=5)
info = suscripcion_de(owner)
check("queda en gracia", info["state"] == "Grace", str(info))

status, _ = escribe(owner, "en gracia")
check("y todavía trabaja", status in (200, 201), str(status))
check("sabiendo qué día se le corta", info["readOnlyOn"] == dia(4), str(info))

# ------------------------------------------------------------------ solo lectura

print("\n[vencido, pasada la gracia]")

vence(plataforma, tenant_id, dia(-10), grace_days=5)
info = suscripcion_de(owner)
check("queda en solo lectura", info["state"] == "ReadOnly", str(info))
check("y el aviso lo dice", info["canWrite"] is False, str(info))

status, error = escribe(owner, "vencido")
check("no puede registrar trabajo nuevo", status == 402, str(status))
check("y se le explica que es la mensualidad",
      isinstance(error, dict) and "mensualidad" in error.get("detail", "").lower(), str(error))

status, ordenes = api("GET", "/api/work-orders", token=owner)
check("pero sigue viendo sus órdenes", status == 200, str(status))

status, _ = api("GET", "/api/customers", token=owner)
check("y sus clientes", status == 200, str(status))

status, _ = api("POST", "/api/service-requests", {
    "customerId": None, "vehicleId": None, "reason": "prueba",
}, token=technician)
check("el técnico también queda sin escribir", status == 402, str(status))

# El cliente final no paga nada nuestro: su lado sigue funcionando.
status, _ = api("GET", "/api/auth/me", token=customer)
check("el cliente del taller sigue entrando", status == 200, str(status))

# ------------------------------------------------------------------ acuerdo de pago

print("\n[acuerdo de pago]")

status, _ = api("PUT", f"/api/platform/tenants/{tenant_id}/agreement", {
    "unblockedThrough": dia(7),
    "note": "Paga el 30, habló el 15",
}, token=plataforma)
check("se registra el acuerdo", status == 200, str(status))

info = suscripcion_de(owner)
check("el taller vuelve a poder escribir", info["canWrite"] is True, str(info))
check("y se le dice hasta cuándo", info["agreementThrough"] == dia(7), str(info))
check("con el motivo anotado", "30" in (info["agreementNote"] or ""), str(info))

status, _ = escribe(owner, "con acuerdo")
check("y de hecho trabaja", status in (200, 201), str(status))

status, _ = api("PUT", f"/api/platform/tenants/{tenant_id}/agreement", {
    "unblockedThrough": dia(-1), "note": "hacia atrás",
}, token=plataforma)
check("un acuerdo hacia el pasado se rechaza", status == 400, str(status))

# ------------------------------------------------------------------ el pago

print("\n[registrar el pago]")

status, detalle = api("POST", f"/api/platform/tenants/{tenant_id}/payments", {
    "amount": 1200, "method": "Transferencia", "reference": "TR-001", "months": 1,
}, token=plataforma)
check("se registra el pago", status == 200, str(status))

# Venía vencido al día -10: el mes corre desde ahí, no desde hoy. Quien paga tarde no pierde
# los días que ya había pagado, y quien arrastra meses sigue debiendo.
check("la fecha corre desde el vencimiento anterior, no desde hoy",
      detalle["tenant"]["paidThrough"] > dia(-10), str(detalle["tenant"]["paidThrough"]))
check("el acuerdo se cancela al pagar",
      detalle["tenant"]["unblockedThrough"] is None, str(detalle["tenant"]))
check("y el pago queda en el historial",
      any(p["reference"] == "TR-001" for p in detalle["payments"]), str(detalle["payments"]))

status, error = api("POST", f"/api/platform/tenants/{tenant_id}/payments", {
    "amount": 0, "months": 1,
}, token=plataforma)
check("un pago de cero se rechaza", status == 400, str(status))

# ------------------------------------------------------------------ suspensión

print("\n[suspensión]")

vence(plataforma, tenant_id, dia(30))

status, _ = api("POST", f"/api/platform/tenants/{tenant_id}/suspend", token=plataforma)
check("se suspende el taller", status == 200, str(status))

status, _ = login("owner@garaj.test")
check("el Dueño ya no entra", status == 401, str(status))

status, _ = login("tecnico1@garaj.test")
check("el técnico tampoco", status == 401, str(status))

status, _ = api("POST", f"/api/platform/tenants/{tenant_id}/reactivate", token=plataforma)
check("se reactiva", status == 200, str(status))

status, _ = login("owner@garaj.test")
check("y el taller vuelve a entrar", status == 200, str(status))

# ------------------------------------------------------------------ se deja como estaba

print("\n[queda como estaba]")

status, _ = vence(plataforma, tenant_id, None)
_, final = api("GET", f"/api/platform/tenants/{tenant_id}", token=plataforma)
check("el taller queda sin fecha de cobro", final["tenant"]["paidThrough"] is None, str(status))
check("y activo", final["tenant"]["isActive"] is True, str(final["tenant"]))

owner = token_of("owner@garaj.test")
check("sin aviso de cobro", suscripcion_de(owner)["state"] == "Active")

print(f"\n{ok} comprobaciones bien, {len(failed)} mal")
if failed:
    for name in failed:
        print(f"  · {name}")
    sys.exit(1)
