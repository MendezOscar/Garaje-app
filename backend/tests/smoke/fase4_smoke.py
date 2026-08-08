#!/usr/bin/env python3
"""Humo de la Fase 4: cotizaciones y WhatsApp.

Recorre el circuito completo —armar desde la orden, enviar, que el cliente la abra por el
link público sin login y la apruebe— y comprueba que la aprobación se propaga al taller.
Escribe en la base: va contra el entorno local.

    python3 backend/tests/smoke/fase4_smoke.py
"""

import json
import sys
import urllib.error
import urllib.request

BASE = "http://localhost:5080"
PASSWORD = "Garaj123!"

# Espejo de Garaj.Domain.Enums
PART, LABOR = 1, 2
DRAFT, SENT, APPROVED, REJECTED, EXPIRED = 1, 2, 3, 4, 5

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


def request(method, url, body=None, token=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
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


def api(method, path, body=None, token=None):
    return request(method, BASE + path, body=body, token=token)


def login(email):
    status, data = api("POST", "/api/auth/login", {"email": email, "password": PASSWORD})
    if status != 200:
        sys.exit(f"No se pudo entrar como {email}: {status} {data}")
    return data["accessToken"]


print("Fase 4 — cotizaciones y WhatsApp\n")

owner = login("owner@garaj.test")
tech1 = login("tecnico1@garaj.test")
customer = login("cliente@garaj.test")

print("[catálogo de mano de obra]")

status, services = api("GET", "/api/labor-services", token=owner)
check("el catálogo del seeder responde", status == 200 and len(services) > 0, f"{status} {services}")

fixed = next((s for s in services if not s["isFixedPrice"]), services[0])
check("el precio por horas viene resuelto",
      fixed["price"] == fixed["standardHours"] * fixed["hourlyRate"],
      f"{fixed['price']} vs {fixed['standardHours']}×{fixed['hourlyRate']}")

status, created = api("POST", "/api/labor-services", {
    "code": "MO-PRUEBA", "name": "Revisión de prueba", "category": "Diagnóstico",
    "standardHours": 1.5, "hourlyRate": 600, "isFixedPrice": False, "fixedPrice": 0,
}, owner)
if status == 409:
    created = next(s for s in services if s["code"] == "MO-PRUEBA")
    status = 200
check("el Dueño da de alta un servicio", status == 200, f"{status} {created}")
check("y su precio sale de horas × tarifa", created["price"] == 900, str(created.get("price")))

status, denied = api("POST", "/api/labor-services", {
    "code": "MO-X", "name": "X", "standardHours": 1, "hourlyRate": 1,
    "isFixedPrice": False, "fixedPrice": 0,
}, tech1)
check("el técnico no administra el catálogo", status == 403, str(status))

print("\n[armar la cotización desde la orden]")

_, orders = api("GET", "/api/work-orders?onlyOpen=true&pageSize=50", token=owner)
order = orders["items"][0]
order_id = order["id"]

# Se carga un repuesto para que la cotización tenga algo que arrastrar.
_, parts_page = api("GET", "/api/parts?pageSize=5", token=owner)
part = parts_page["items"][0]
api("POST", f"/api/work-orders/{order_id}/parts", {"partId": part["id"], "quantity": 2}, owner)

status, quote = api("POST", "/api/quotes/from-work-order", {
    "workOrderId": order_id, "notes": "Cotización de prueba de humo",
}, owner)
check("el Dueño arma la cotización desde la orden", status in (200, 201), f"{status} {quote}")

quote_id = quote["id"]
check("hereda el número correlativo de la sucursal", quote["number"].startswith("COT-"),
      str(quote.get("number")))
check("arrastra el repuesto consumido",
      any(l["lineType"] == PART for l in quote["lines"]), str(quote["lines"]))
check("nace en borrador", quote["status"] == DRAFT, str(quote.get("status")))
check("un borrador no expone link público", quote["publicUrl"] is None, str(quote.get("publicUrl")))
check("toma el impuesto por defecto del taller", quote["taxRate"] == 15, str(quote.get("taxRate")))

print("\n[líneas y totales]")

status, quote = api("POST", f"/api/quotes/{quote_id}/lines", {
    "lineType": LABOR, "laborServiceId": created["id"], "quantity": 1,
}, owner)
check("agrega mano de obra desde el catálogo", status == 200, f"{status} {quote}")

labor_line = next(l for l in quote["lines"] if l["laborServiceId"] == created["id"])
check("con el precio del servicio", labor_line["unitPrice"] == 900, str(labor_line.get("unitPrice")))

expected_subtotal = sum(l["quantity"] * l["unitPrice"] for l in quote["lines"])
check("el subtotal suma las líneas", abs(quote["subtotal"] - expected_subtotal) < 0.01,
      f"{quote['subtotal']} vs {expected_subtotal}")
check("el impuesto sale del subtotal menos descuentos",
      abs(quote["taxTotal"] - round((quote["subtotal"] - quote["discountTotal"]) * 0.15, 2)) < 0.02,
      str(quote.get("taxTotal")))
check("el total cierra",
      abs(quote["total"] - (quote["subtotal"] - quote["discountTotal"] + quote["taxTotal"])) < 0.02,
      str(quote.get("total")))

status, quote = api("PUT", f"/api/quotes/{quote_id}/lines/{labor_line['id']}", {
    "lineType": LABOR, "laborServiceId": created["id"], "quantity": 1,
    "unitPrice": 900, "discount": 100,
}, owner)
check("un descuento se refleja en el total", quote["discountTotal"] == 100,
      str(quote.get("discountTotal")))

status, quote = api("POST", f"/api/quotes/{quote_id}/lines", {
    "lineType": LABOR, "description": "Línea suelta para borrar", "quantity": 1, "unitPrice": 250,
}, owner)
extra = next(l for l in quote["lines"] if l["description"] == "Línea suelta para borrar")
check("acepta una línea libre sin catálogo", status == 200, str(status))

before = quote["total"]
status, quote = api("DELETE", f"/api/quotes/{quote_id}/lines/{extra['id']}", token=owner)
check("al quitarla el total baja", quote["total"] < before, f"{before} → {quote['total']}")

status, error = api("POST", f"/api/quotes/{quote_id}/lines", {
    "lineType": LABOR, "description": "X", "quantity": 0, "unitPrice": 1,
}, owner)
check("una cantidad en cero se rechaza", status == 400, f"{status} {error}")

print("\n[envío por WhatsApp]")

status, link = api("POST", f"/api/quotes/{quote_id}/send", token=owner)
check("enviar responde con el link", status == 200, f"{status} {link}")
check("es un link de wa.me", link["url"].startswith("https://wa.me/"), str(link.get("url")))
check("va al teléfono del cliente en E.164", link["phone"] == "50498881111", str(link.get("phone")))
check("el mensaje lleva el número de cotización", quote["number"] in link["message"])
check("y el enlace público", "/q/" in link["message"], str(link.get("message")))

status, quote = api("GET", f"/api/quotes/{quote_id}", token=owner)
check("la cotización queda como enviada", quote["status"] == SENT, str(quote.get("status")))
check("y ahora sí expone el link público", quote["publicUrl"] is not None)

token_value = quote["publicUrl"].rsplit("/", 1)[-1]

status, again = api("GET", f"/api/quotes/{quote_id}/whatsapp-link", token=owner)
check("se puede reenviar sin cambiar nada", status == 200 and again["url"] == link["url"], str(status))

status, denied = api("POST", f"/api/quotes/{quote_id}/send", token=customer)
check("el cliente no envía cotizaciones", status == 403, str(status))

print("\n[PDF]")

status, pdf = api("GET", f"/api/quotes/{quote_id}/pdf", token=owner)
check("el PDF se genera", status == 200 and isinstance(pdf, bytes) and pdf[:4] == b"%PDF",
      f"{status} {type(pdf)}")
check("y pesa algo razonable", isinstance(pdf, bytes) and len(pdf) > 2000,
      str(len(pdf) if isinstance(pdf, bytes) else 0))

print("\n[link público, sin autenticación]")

status, public = api("GET", f"/public/quotes/{token_value}")
check("el cliente la abre sin login", status == 200, f"{status} {public}")
check("ve el nombre del taller", public["tenantName"] == "Taller Garaj", str(public.get("tenantName")))
check("ve las líneas", len(public["lines"]) == len(quote["lines"]), str(len(public["lines"])))
check("ve el total", abs(public["total"] - quote["total"]) < 0.01)
check("puede responder", public["canRespond"] is True, str(public.get("canRespond")))
check("no se filtra ningún id interno",
      not any(k for k in public if k.lower().endswith("id")), str(list(public.keys())))

status, missing = api("GET", "/public/quotes/00000000-0000-0000-0000-000000000000")
check("un token inventado devuelve 404", status == 404, str(status))

status, public_pdf = api("GET", f"/public/quotes/{token_value}/pdf")
check("el PDF también se descarga sin login",
      status == 200 and isinstance(public_pdf, bytes) and public_pdf[:4] == b"%PDF", str(status))

print("\n[aprobación y propagación]")

status, responded = api("POST", f"/public/quotes/{token_value}/respond", {
    "approve": True, "note": "De acuerdo, procedan.",
})
check("el cliente aprueba desde el link", status == 200, f"{status} {responded}")
check("queda aprobada", responded["status"] == APPROVED, str(responded.get("status")))
check("y ya no admite otra respuesta", responded["canRespond"] is False)

status, twice = api("POST", f"/public/quotes/{token_value}/respond", {"approve": False})
check("responder dos veces se rechaza", status == 409, f"{status} {twice}")

status, error = api("POST", f"/api/quotes/{quote_id}/lines", {
    "lineType": LABOR, "description": "Después de aprobada", "quantity": 1, "unitPrice": 10,
}, owner)
check("una cotización aprobada ya no se edita", status == 409, f"{status} {error}")
check("y el mensaje dice qué hacer", "nueva" in str(error.get("detail", "")).lower(), str(error))

status, detail = api("GET", f"/api/work-orders/{order_id}", token=owner)
approval = [t for t in detail["timeline"] if "aprobó la cotización" in (t.get("note") or "")]
check("la aprobación queda en la línea de tiempo de la orden", len(approval) == 1, str(len(approval)))
check("y el cliente la ve", approval and approval[0]["isVisibleToCustomer"])

print("\n[alcance por perfil]")

status, mine = api("GET", "/api/quotes", token=customer)
check("el cliente ve sus cotizaciones", status == 200 and mine["total"] > 0, f"{status} {mine}")
check("todas son suyas", all(q["customerId"] == mine["items"][0]["customerId"] for q in mine["items"]))

status, none_for_tech = api("GET", "/api/quotes", token=tech1)
check("el técnico no participa en la parte comercial",
      status == 200 and none_for_tech["total"] == 0, f"{status} {none_for_tech}")

status, denied = api("POST", "/api/quotes", {"customerId": mine["items"][0]["customerId"]}, customer)
check("el cliente no crea cotizaciones", status == 403, str(status))

print("\n[rechazo]")

status, second = api("POST", "/api/quotes/from-work-order", {"workOrderId": order_id}, owner)
second_id = second["id"]
api("POST", f"/api/quotes/{second_id}/send", token=owner)

_, second = api("GET", f"/api/quotes/{second_id}", token=owner)
second_token = second["publicUrl"].rsplit("/", 1)[-1]

status, rejected = api("POST", f"/public/quotes/{second_token}/respond", {
    "approve": False, "note": "Muy caro por ahora.",
})
check("el cliente puede rechazar", status == 200 and rejected["status"] == REJECTED,
      f"{status} {rejected}")

status, detail = api("GET", f"/api/work-orders/{order_id}", token=owner)
rejection = [t for t in detail["timeline"] if "rechazó la cotización" in (t.get("note") or "")]
check("el rechazo también queda en la orden, con el motivo",
      len(rejection) == 1 and "Muy caro" in rejection[0]["note"], str(rejection))

print("\n[vencimiento]")

status, old = api("POST", "/api/quotes/from-work-order", {
    "workOrderId": order_id, "validUntil": "2020-01-01T00:00:00Z",
}, owner)
api("POST", f"/api/quotes/{old['id']}/send", token=owner)

_, old = api("GET", f"/api/quotes/{old['id']}", token=owner)
check("una cotización pasada de fecha se marca vencida", old["isExpired"] is True,
      str(old.get("isExpired")))

old_token = old["publicUrl"].rsplit("/", 1)[-1]
status, public_old = api("GET", f"/public/quotes/{old_token}")
check("y el cliente no puede aprobarla", public_old["canRespond"] is False)

status, error = api("POST", f"/public/quotes/{old_token}/respond", {"approve": True})
check("intentarlo devuelve 409", status == 409, f"{status} {error}")
check("diciendo que pida una actualizada", "venció" in str(error.get("detail", "")), str(error))

print(f"\n{ok} comprobaciones correctas, {len(failed)} fallidas")
if failed:
    for name in failed:
        print(f"  - {name}")
    sys.exit(1)
