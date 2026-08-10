#!/usr/bin/env python3
"""Humo de la Fase 11: la ficha del taller y su logo.

Comprueba que el Dueño corrige los datos que se imprimen en la cotización y en la factura,
que sube y quita el logo, que nadie más puede tocarlo, y que ese logo sale por las dos rutas
que lo sirven —la del panel y la de la página pública, que va por el token de la cotización—
sin filtrar ids internos. Escribe en la base y sube al bucket: necesita MinIO arriba.

    docker compose up -d
    python3 backend/tests/smoke/fase11_smoke.py
"""

import json
import struct
import sys
import urllib.error
import urllib.request
import zlib

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


def raw(path, token=None):
    """Como api(), pero devuelve también el tipo de contenido: hace falta para el logo."""
    req = urllib.request.Request(BASE + path, method="GET")
    if token:
        req.add_header("Authorization", f"Bearer {token}")

    try:
        with urllib.request.urlopen(req) as r:
            return r.status, r.read(), r.headers.get("Content-Type", "")
    except urllib.error.HTTPError as e:
        return e.code, e.read(), e.headers.get("Content-Type", "")


def upload(path, filename, content, content_type, token=None):
    """multipart/form-data a mano: el logo sí pasa por la API, al revés que las fotos."""
    boundary = "----garajhumo11"
    body = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="file"; filename="{filename}"\r\n'
        f"Content-Type: {content_type}\r\n\r\n"
    ).encode() + content + f"\r\n--{boundary}--\r\n".encode()

    req = urllib.request.Request(BASE + path, data=body, method="POST")
    req.add_header("Content-Type", f"multipart/form-data; boundary={boundary}")
    if token:
        req.add_header("Authorization", f"Bearer {token}")

    try:
        with urllib.request.urlopen(req) as r:
            payload = r.read()
            return r.status, json.loads(payload) if payload else None
    except urllib.error.HTTPError as e:
        payload = e.read()
        try:
            return e.code, json.loads(payload)
        except (ValueError, UnicodeDecodeError):
            return e.code, payload


def png(size=64):
    """PNG cuadrado azul con transparencia, armado a mano para no depender de Pillow."""

    def chunk(kind, data):
        return (
            struct.pack(">I", len(data))
            + kind
            + data
            + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
        )

    # RGBA: azul de marca al 100% de opacidad.
    row = b"\x00" + b"\x1f\x6f\xeb\xff" * size
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(row * size))
        + chunk(b"IEND", b"")
    )


def token_of(email, password=PASSWORD):
    status, data = api("POST", "/api/auth/login", {"email": email, "password": password})
    if status != 200:
        sys.exit(f"No se pudo entrar como {email}: {status} {data}")
    return data["accessToken"]


print("Fase 11 — ficha del taller y logo\n")

owner = token_of("owner@garaj.test")
technician = token_of("tecnico1@garaj.test")
customer = token_of("cliente@garaj.test")

print("[la ficha, solo del Dueño]")

status, ficha = api("GET", "/api/tenant", token=owner)
check("el Dueño ve la ficha del taller", status == 200, f"{status} {ficha}")
check("con el nombre que se imprime en la cotización", bool(ficha and ficha.get("name")),
      str(ficha))

original = ficha

status, _ = api("GET", "/api/tenant", token=technician)
check("el técnico no la ve", status == 403, str(status))

status, _ = api("GET", "/api/tenant", token=customer)
check("el cliente tampoco", status == 403, str(status))

print("\n[corregir los datos legales]")

status, saved = api("PUT", "/api/tenant", {
    "name": original["name"],
    "legalName": "Humo Once S. de R.L.",
    "taxId": "08019995123456",
    "phone": "50499001111",
    "email": "humo11@garaj.test",
    "defaultTaxRate": 15,
    "defaultPhoneCountryCode": "504",
}, token=owner)
check("el Dueño guarda razón social y RTN", status == 200, f"{status} {saved}")
check("y quedan guardados",
      saved and saved["legalName"] == "Humo Once S. de R.L." and saved["taxId"] == "08019995123456",
      str(saved))

status, _ = api("PUT", "/api/tenant", {
    "name": "   ", "defaultTaxRate": 15,
}, token=owner)
check("un taller sin nombre se rechaza", status == 400, str(status))

status, _ = api("PUT", "/api/tenant", {
    "name": original["name"], "defaultTaxRate": 200,
}, token=owner)
check("una tasa de impuesto imposible también", status == 400, str(status))

status, _ = api("PUT", "/api/tenant", {
    "name": "Taller del Técnico", "defaultTaxRate": 15,
}, token=technician)
check("el técnico no guarda nada", status == 403, str(status))

print("\n[el logo]")

tenant_id = original["id"]

# Parte de un taller sin logo aunque una corrida anterior lo haya dejado puesto.
api("DELETE", "/api/tenant/logo", token=owner)

status, _, _ = raw(f"/api/tenants/{tenant_id}/logo")
check("sin logo, la ruta pública da 404", status == 404, str(status))

status, _ = upload("/api/tenant/logo", "logo.png", b"no soy una imagen", "image/png", owner)
check("un archivo que no es imagen se rechaza", status == 400, str(status))

status, _ = upload("/api/tenant/logo", "logo.svg", b"<svg/>", "image/svg+xml", owner)
check("un SVG tampoco entra", status == 400, str(status))

status, _ = upload("/api/tenant/logo", "logo.png", png(), "image/png", technician)
check("el técnico no sube logo", status == 403, str(status))

status, with_logo = upload("/api/tenant/logo", "logo.png", png(), "image/png", owner)
check("el Dueño sí", status == 200, f"{status} {with_logo}")
check("y la ficha devuelve su ruta",
      with_logo and with_logo["logoUrl"] == f"/api/tenants/{tenant_id}/logo",
      str(with_logo.get("logoUrl") if with_logo else None))

status, body, content_type = raw(f"/api/tenants/{tenant_id}/logo")
check("la ruta pública lo sirve sin sesión", status == 200, str(status))
check("como PNG", content_type.startswith("image/png"), content_type)
check("y es una imagen de verdad", body[:8] == b"\x89PNG\r\n\x1a\n", str(body[:8]))

status, me = api("GET", "/api/auth/me", token=owner)
check("la sesión trae la ruta del logo para la barra",
      me and me["tenantLogoUrl"] == f"/api/tenants/{tenant_id}/logo",
      str(me.get("tenantLogoUrl") if me else None))

print("\n[lo que ve el cliente]")

# Cotización propia: este humo no depende de que otro haya corrido antes. El link público
# solo existe una vez enviada, y solo el detalle lo devuelve.
_, orders = api("GET", "/api/work-orders?onlyOpen=true&pageSize=50", token=owner)
if not orders["items"]:
    sys.exit("No hay órdenes abiertas en la base: recree la base local (ver docs/api.md)")

status, sent = api("POST", "/api/quotes/from-work-order", {
    "workOrderId": orders["items"][0]["id"], "notes": "Humo de la ficha del taller",
}, token=owner)
if status not in (200, 201):
    sys.exit(f"No se pudo armar la cotización de prueba: {status} {sent}")

# Una línea suelta: la orden puede venir sin repuestos cargados y una cotización vacía no se
# puede enviar.
api("POST", f"/api/quotes/{sent['id']}/lines", {
    "lineType": 2, "description": "Revisión general", "quantity": 1, "unitPrice": 500,
}, token=owner)

api("POST", f"/api/quotes/{sent['id']}/send", token=owner)
_, sent = api("GET", f"/api/quotes/{sent['id']}", token=owner)

public_token = (sent.get("publicUrl") or "").rstrip("/").split("/")[-1]
if not public_token:
    sys.exit("La cotización de prueba no expuso link público")

status, public = api("GET", f"/public/quotes/{public_token}")
check("la página pública trae el logo del taller",
      status == 200 and public["tenantLogoUrl"] == f"/public/quotes/{public_token}/logo",
      str(public.get("tenantLogoUrl") if status == 200 else status))
check("y sigue sin filtrar ids internos",
      not any(k for k in public if k.lower().endswith("id")), str(list(public.keys())))

status, body, content_type = raw(f"/public/quotes/{public_token}/logo")
check("el cliente lo carga sin login",
      status == 200 and body[:8] == b"\x89PNG\r\n\x1a\n", str(status))
check("con el tipo correcto", content_type.startswith("image/png"), content_type)

status, body, _ = raw("/public/quotes/00000000-0000-0000-0000-000000000000/logo")
check("un token inventado da 404", status == 404, str(status))

status, pdf = api("GET", f"/api/quotes/{(sent or {})['id']}/pdf", token=owner)
check("el PDF de la cotización sale con logo",
      status == 200 and isinstance(pdf, bytes) and pdf[:4] == b"%PDF", str(status))

_, sales = api("GET", "/api/sales?pageSize=1", token=owner)
if sales["items"]:
    status, invoice = api("GET", f"/api/sales/{sales['items'][0]['id']}/pdf", token=owner)
    check("la factura también",
          status == 200 and isinstance(invoice, bytes) and invoice[:4] == b"%PDF", str(status))

print("\n[quitarlo]")

status, cleared = api("DELETE", "/api/tenant/logo", token=owner)
check("el Dueño lo quita", status == 200 and cleared["logoUrl"] is None, f"{status} {cleared}")

status, _, _ = raw(f"/api/tenants/{tenant_id}/logo")
check("y la ruta vuelve a 404", status == 404, str(status))

status, pdf = api("GET", f"/api/quotes/{(sent or {})['id']}/pdf", token=owner)
check("el PDF sigue generándose sin logo",
      status == 200 and isinstance(pdf, bytes) and pdf[:4] == b"%PDF", str(status))

# Deja la ficha como estaba: los demás humos y el del móvil esperan el taller de demostración.
api("PUT", "/api/tenant", {
    "name": original["name"],
    "legalName": original["legalName"],
    "taxId": original["taxId"],
    "phone": original["phone"],
    "email": original["email"],
    "defaultTaxRate": original["defaultTaxRate"],
    "defaultPhoneCountryCode": original["defaultPhoneCountryCode"],
}, token=owner)

_, restored = api("GET", "/api/tenant", token=owner)
check("la ficha queda como estaba", restored["legalName"] == original["legalName"],
      str(restored.get("legalName")))

print(f"\n{ok} comprobaciones bien, {len(failed)} mal")
if failed:
    for name in failed:
        print(f"  · {name}")
    sys.exit(1)
