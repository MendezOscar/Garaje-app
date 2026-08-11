#!/usr/bin/env python3
"""Humo de la Fase 12: facturación con CAI.

Comprueba que el Dueño registra el rango autorizado por el SAR, que la factura con CAI consume
un correlativo y solo uno, que un rango agotado o vencido no emite, que un número consumido no
vuelve ni cuando la factura se anula, y que sin CAI todo sigue como antes. Escribe en la base:
va contra el entorno local.

    python3 backend/tests/smoke/fase12_smoke.py
"""

import json
import sys
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone

BASE = "http://localhost:5080"
PASSWORD = "Garaj123!"

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


def token_of(email, password=PASSWORD):
    status, data = api("POST", "/api/auth/login", {"email": email, "password": password})
    if status != 200:
        sys.exit(f"No se pudo entrar como {email}: {status} {data}")
    return data["accessToken"]


def iso(days):
    return (datetime.now(timezone.utc) + timedelta(days=days)).isoformat()


def rango(branch_id, desde, hasta, dias=365, punto="001"):
    return {
        "branchId": branch_id,
        "cai": "A1B2C3-D4E5F6-A1B2C3-D4E5F6-A1B2C3-01",
        "establishmentCode": "000",
        "pointOfSaleCode": punto,
        "documentType": "01",
        "rangeStart": desde,
        "rangeEnd": hasta,
        "issueDeadline": iso(dias),
    }


print("Fase 12 — facturación con CAI\n")

owner = token_of("owner@garaj.test")
technician = token_of("tecnico1@garaj.test")

_, branches = api("GET", "/api/branches", token=owner)
matriz = next(b for b in branches if b["code"] == "MTZ")

_, parts = api("GET", "/api/parts?pageSize=5", token=owner)
part = parts["items"][0]

_, customers = api("GET", "/api/customers?pageSize=5", token=owner)
customer = customers["items"][0]


def venta(fiscal=False, rtn=None, customer_id=None):
    """Venta de mostrador de una unidad: lo mínimo para que exista una factura."""
    body = {
        "branchId": matriz["id"],
        "customerId": customer_id,
        "paymentMethod": CASH,
        "lines": [{"lineType": 1, "partId": part["id"], "quantity": 1}],
        "fiscal": fiscal,
    }
    if rtn is not None:
        body["customerTaxId"] = rtn

    return api("POST", "/api/sales", body, token=owner)


print("[registrar el rango]")

status, _ = api("POST", "/api/tenant/fiscal-ranges", rango(matriz["id"], 1, 5), token=technician)
check("el técnico no registra rangos", status == 403, str(status))

status, _ = api("POST", "/api/tenant/fiscal-ranges", rango(matriz["id"], 10, 5), token=owner)
check("un rango al revés se rechaza", status == 400, str(status))

status, _ = api("POST", "/api/tenant/fiscal-ranges",
                rango(matriz["id"], 1, 5, dias=-1), token=owner)
check("una fecha límite pasada se rechaza", status == 400, str(status))

corto = dict(rango(matriz["id"], 1, 5))
corto["cai"] = "A1B2"
status, _ = api("POST", "/api/tenant/fiscal-ranges", corto, token=owner)
check("un CAI que no tiene forma de CAI se rechaza", status == 400, str(status))

status, creado = api("POST", "/api/tenant/fiscal-ranges",
                     rango(matriz["id"], 101, 102), token=owner)
check("el Dueño registra el rango", status == 200, f"{status} {creado}")
check("el próximo número es el primero del rango",
      creado and creado["nextFiscalNumber"] == "000-001-01-00000101",
      str(creado.get("nextFiscalNumber") if creado else None))
check("y dice cuántos quedan", creado and creado["remaining"] == 2, str(creado.get("remaining")))

status, listado = api("GET", "/api/tenant/fiscal-ranges", token=owner)
check("el listado lo trae activo", status == 200 and any(r["isActive"] for r in listado),
      str(status))

print("\n[facturar]")

status, sin_cai = venta(fiscal=False)
check("una venta sin CAI se emite igual que antes", status in (200, 201), str(status))
check("y no trae número fiscal", sin_cai["fiscalNumber"] is None, str(sin_cai.get("fiscalNumber")))

_, despues = api("GET", "/api/tenant/fiscal-ranges", token=owner)
activo = next(r for r in despues if r["isActive"])
check("ni consume del rango", activo["remaining"] == 2, str(activo["remaining"]))

status, primera = venta(fiscal=True, rtn="08019995123456")
check("la venta con CAI se emite", status in (200, 201), f"{status} {primera}")
check("con el primer número del rango", primera["fiscalNumber"] == "000-001-01-00000101",
      str(primera.get("fiscalNumber")))
check("guarda el CAI", bool(primera["fiscalCai"]), str(primera.get("fiscalCai")))
check("y el rango autorizado impreso",
      primera["fiscalRangeText"] == "000-001-01-00000101 a 000-001-01-00000102",
      str(primera.get("fiscalRangeText")))
check("con el RTN que se le pasó", primera["customerTaxId"] == "08019995123456",
      str(primera.get("customerTaxId")))

status, pdf = api("GET", f"/api/sales/{primera['id']}/pdf", token=owner)
check("el PDF sale con CAI", status == 200 and isinstance(pdf, bytes) and pdf[:4] == b"%PDF",
      str(status))

print("\n[el número no se reutiliza]")

status, _ = api("POST", f"/api/sales/{primera['id']}/void", {"reason": "Prueba de humo"},
                token=owner)
check("se anula la factura fiscal", status == 200, str(status))

status, anulada = api("GET", f"/api/sales/{primera['id']}", token=owner)
check("y conserva su número fiscal", anulada["fiscalNumber"] == "000-001-01-00000101",
      str(anulada.get("fiscalNumber")))

status, segunda = venta(fiscal=True)
check("la siguiente toma el número que sigue, no el anulado",
      segunda["fiscalNumber"] == "000-001-01-00000102", str(segunda.get("fiscalNumber")))

status, tercera = venta(fiscal=True)
check("agotado el rango, no factura", status == 400, str(status))
check("y lo dice con claridad",
      status == 400 and "agot" in json.dumps(tercera).lower(), str(tercera))

status, sin_cai2 = venta(fiscal=False)
check("pero sí se puede emitir sin CAI", status in (200, 201), str(status))

print("\n[renovar y vencer]")

status, nuevo = api("POST", "/api/tenant/fiscal-ranges",
                    rango(matriz["id"], 201, 205, punto="002"), token=owner)
check("registrar otro rango es posible", status == 200, str(status))

_, todos = api("GET", "/api/tenant/fiscal-ranges", token=owner)
activos = [r for r in todos if r["isActive"] and r["branchId"] == matriz["id"]]
check("y deja uno solo activo por sucursal", len(activos) == 1, str(len(activos)))

status, cuarta = venta(fiscal=True)
check("la numeración sigue en el rango nuevo",
      cuarta["fiscalNumber"] == "000-002-01-00000201", str(cuarta.get("fiscalNumber")))

status, _ = api("DELETE", f"/api/tenant/fiscal-ranges/{nuevo['id']}", token=owner)
check("el Dueño puede desactivarlo", status == 204, str(status))

status, sin_rango = venta(fiscal=True)
check("sin rango activo, la factura fiscal se rechaza", status == 400, str(status))
check("explicando que falta el CAI",
      status == 400 and "CAI" in json.dumps(sin_rango), str(sin_rango))

print("\n[el RTN sale de la ficha del cliente]")

status, ficha = api("PUT", f"/api/customers/{customer['id']}", {
    "fullName": customer["fullName"],
    "phone": customer["phone"],
    "email": customer["email"],
    "documentId": customer["documentId"],
    "address": customer["address"],
    "notes": customer["notes"],
    "isActive": True,
    "taxId": "05011985678901",
}, token=owner)
check("el Dueño guarda el RTN en la ficha", status == 200 and ficha["taxId"] == "05011985678901",
      f"{status} {ficha.get('taxId') if status == 200 else ficha}")

api("POST", "/api/tenant/fiscal-ranges", rango(matriz["id"], 301, 305, punto="003"), token=owner)

status, conRtn = venta(fiscal=True, customer_id=customer["id"])
check("la factura toma el RTN de la ficha sin que se lo pasen",
      conRtn["customerTaxId"] == "05011985678901", str(conRtn.get("customerTaxId")))

status, pdf = api("GET", f"/api/sales/{conRtn['id']}/pdf", token=owner)
check("y el PDF se genera", status == 200 and isinstance(pdf, bytes) and pdf[:4] == b"%PDF",
      str(status))

print("\n[consumidor final arriba de L 10,000]")

grande = {
    "branchId": matriz["id"],
    "paymentMethod": CASH,
    "lines": [{"lineType": 2, "description": "Reparación mayor", "quantity": 1,
               "unitPrice": 12000}],
    "fiscal": True,
}

status, rechazada = api("POST", "/api/sales", grande, token=owner)
check("una factura grande sin identificar al cliente se rechaza", status == 400, str(status))
check("y explica que hace falta el RTN o la identidad",
      status == 400 and "identidad" in json.dumps(rechazada).lower(), str(rechazada))

_, antes = api("GET", "/api/tenant/fiscal-ranges", token=owner)
disponibles = next(r["remaining"] for r in antes if r["isActive"])

status, conIdentidad = api("POST", "/api/sales", dict(grande, customerId=customer["id"]),
                           token=owner)
check("con el cliente identificado sí se emite", status in (200, 201), str(status))

_, luego = api("GET", "/api/tenant/fiscal-ranges", token=owner)
check("y el rechazo anterior no había quemado ningún número",
      next(r["remaining"] for r in luego if r["isActive"]) == disponibles - 1,
      str(disponibles))

print("\n[la ficha del taller]")

_, ficha_taller = api("GET", "/api/tenant", token=owner)
status, guardada = api("PUT", "/api/tenant", {
    "name": ficha_taller["name"],
    "legalName": ficha_taller["legalName"],
    "taxId": ficha_taller["taxId"],
    "phone": ficha_taller["phone"],
    "email": ficha_taller["email"],
    "address": "Bo. El Centro, 3 calle, Comayagüela",
    "defaultTaxRate": ficha_taller["defaultTaxRate"],
    "defaultPhoneCountryCode": ficha_taller["defaultPhoneCountryCode"],
}, token=owner)
check("el Dueño guarda la dirección de la casa matriz",
      status == 200 and guardada["address"] == "Bo. El Centro, 3 calle, Comayagüela",
      f"{status} {guardada}")

# Deja el taller sin rango activo: los demás humos y el móvil esperan la base de demostración.
_, finales = api("GET", "/api/tenant/fiscal-ranges", token=owner)
for r in finales:
    if r["isActive"]:
        api("DELETE", f"/api/tenant/fiscal-ranges/{r['id']}", token=owner)

_, limpio = api("GET", "/api/tenant/fiscal-ranges", token=owner)
check("el taller queda sin rango activo", not any(r["isActive"] for r in limpio), str(limpio))

print(f"\n{ok} comprobaciones bien, {len(failed)} mal")
if failed:
    for name in failed:
        print(f"  · {name}")
    sys.exit(1)
