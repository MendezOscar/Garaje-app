#!/usr/bin/env python3
"""Humo de la Fase 8: crédito, abonos y costo en el inventario.

Comprueba que una venta puede quedar con saldo, que los abonos lo van bajando sin pasarse,
que las cuentas por cobrar y el tablero cuadran con lo que falta, y que el listado de
existencias trae el precio de compra. Escribe en la base: va contra el entorno local.

    python3 backend/tests/smoke/fase8_smoke.py
"""

import json
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

BASE = "http://localhost:5080"
PASSWORD = "Garaj123!"

# Espejo de Garaj.Domain.Enums
MOTORCYCLE = 2
CASH, CARD, TRANSFER = 1, 2, 3

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


print("Fase 8 — crédito, abonos y costo en existencias\n")

owner = login("owner@garaj.test")
technician = login("tecnico1@garaj.test")
customer = login("cliente@garaj.test")

_, branches = api("GET", "/api/branches", token=owner)
matriz = next(b for b in branches if b["code"] == "MTZ")

# ------------------------------------------------------- el costo en las existencias

print("[costo en el inventario]")

_, stock = api("GET", f"/api/stock?branchId={matriz['id']}&pageSize=20", token=owner)
item = next(s for s in stock["items"] if s["quantity"] >= 3)
check("el listado trae el precio de compra", "costPrice" in item, str(list(item)))
check("y también el de venta", "salePrice" in item)
check("el costo es menor que la venta", item["costPrice"] < item["salePrice"],
      f'{item["costPrice"]} vs {item["salePrice"]}')

_, part = api("GET", f"/api/parts/{item['partId']}", token=owner)
check("y coincide con la ficha del repuesto", part["costPrice"] == item["costPrice"],
      f'{part["costPrice"]} vs {item["costPrice"]}')

# ------------------------------------------------------------ venta de contado

print("\n[venta de contado]")

status, cash_sale = api("POST", "/api/sales", {
    "branchId": matriz["id"],
    "paymentMethod": CASH,
    "lines": [{"lineType": 1, "partId": item["partId"], "quantity": 1}],
}, token=owner)
check("se crea la venta", status == 201, f"{status} {cash_sale}")
check("queda sin saldo", cash_sale["balance"] == 0, str(cash_sale.get("balance")))
check("y con lo cobrado igual al total",
      cash_sale["amountPaid"] == cash_sale["total"],
      f'{cash_sale["amountPaid"]} vs {cash_sale["total"]}')
check("el contado también deja su abono", len(cash_sale["payments"]) == 1,
      str(cash_sale.get("payments")))
check("no tiene fecha de vencimiento", cash_sale["dueDate"] is None)

status, _ = api("POST", f"/api/sales/{cash_sale['id']}/payments", {
    "amount": 10, "method": CASH,
}, token=owner)
check("no admite abonos si ya está pagada", status == 409, str(status))

# ------------------------------------------------------------ venta a crédito

print("\n[venta a crédito]")

_, customers = api("GET", "/api/customers?pageSize=5", token=owner)
buyer = customers["items"][0]

status, sale = api("POST", "/api/sales", {
    "branchId": matriz["id"],
    "customerId": buyer["id"],
    "paymentMethod": CASH,
    "dueDate": "2099-12-31T00:00:00Z",
    "initialPayment": 100,
    "lines": [{"lineType": 2, "description": "Servicio a crédito", "quantity": 1, "unitPrice": 1000}],
}, token=owner)
check("se crea con prima", status == 201, f"{status} {sale}")
check("cobra la prima y no más", sale["amountPaid"] == 100, str(sale.get("amountPaid")))
check("y deja el resto como saldo", sale["balance"] == sale["total"] - 100,
      f'{sale["balance"]} vs {sale["total"] - 100}')
check("con la fecha acordada", sale["dueDate"] is not None)
check("que todavía no vence", sale["isOverdue"] is False)

sale_id = sale["id"]
balance = sale["balance"]

status, _ = api("POST", f"/api/sales/{sale_id}/payments", {
    "amount": balance + 1, "method": CASH,
}, token=owner)
check("un abono mayor al saldo se rechaza", status == 400, str(status))

status, _ = api("POST", f"/api/sales/{sale_id}/payments", {
    "amount": 0, "method": CASH,
}, token=owner)
check("y uno de cero también", status == 400, str(status))

status, after = api("POST", f"/api/sales/{sale_id}/payments", {
    "amount": 300, "method": TRANSFER, "reference": "TRF-9001",
}, token=owner)
check("se registra un abono", status == 200, f"{status} {after}")
check("baja el saldo", after["balance"] == balance - 300, str(after.get("balance")))
check("y sube lo cobrado", after["amountPaid"] == 400, str(after.get("amountPaid")))
check("guarda la referencia",
      any(p["reference"] == "TRF-9001" for p in after["payments"]),
      str([p.get("reference") for p in after["payments"]]))
check("y deja constancia de quién lo recibió",
      any(p["registeredByName"] for p in after["payments"]),
      str([p.get("registeredByName") for p in after["payments"]]))

status, _ = api("POST", f"/api/sales/{sale_id}/payments", {
    "amount": 50, "method": CASH,
}, token=technician)
check("el técnico no registra abonos", status in (403, 404), str(status))

status, _ = api("POST", f"/api/sales/{sale_id}/payments", {
    "amount": 50, "method": CASH,
}, token=customer)
check("el cliente tampoco", status in (403, 404), str(status))

# ------------------------------------------------------------ cuentas por cobrar

print("\n[cuentas por cobrar]")

_, pending = api("GET", "/api/sales?onlyUnpaid=true&pageSize=50", token=owner)
ids = [s["id"] for s in pending["items"]]
check("la venta a crédito aparece", sale_id in ids, str(len(ids)))
check("la de contado no", cash_sale["id"] not in ids)
check("todas traen saldo mayor que cero",
      all(s["balance"] > 0 for s in pending["items"]))

# La búsqueda de la sección Por cobrar: el cliente llama y quien contesta lo encuentra con
# lo que tenga a mano, no con un identificador.
def por_cobrar(extra=""):
    _, page = api("GET", f"/api/sales?onlyUnpaid=true&pageSize=50{extra}", token=owner)
    return [s["id"] for s in page["items"]]


nombre = urllib.parse.quote(buyer["fullName"].split()[0])
check("se busca por el nombre del cliente", sale_id in por_cobrar(f"&search={nombre}"))

check("por el número de la venta",
      por_cobrar(f"&search={urllib.parse.quote(sale['number'])}") == [sale_id])

telefono = buyer["phone"][-8:]
check("y por su teléfono", sale_id in por_cobrar(f"&search={telefono}"))

check("lo que no existe no devuelve nada", por_cobrar("&search=zzzznoexiste") == [])

check("la venta sin vencer sale en «por vencer»", sale_id in por_cobrar("&overdue=false"))
check("y no en «vencidas»", sale_id not in por_cobrar("&overdue=true"))

_, dashboard = api("GET", "/api/reports/dashboard", token=owner)
check("el tablero suma lo por cobrar",
      abs(dashboard["receivables"] - sum(s["balance"] for s in pending["items"])) < 0.01,
      f'{dashboard["receivables"]} vs {sum(s["balance"] for s in pending["items"])}')
check("y separa lo vencido", dashboard["overdueReceivables"] >= 0,
      str(dashboard.get("overdueReceivables")))

# El reporte de ingresos es de lo facturado, no de lo cobrado: la venta a crédito cuenta
# entera desde el día en que se emitió.
_, report = api("GET", "/api/reports/revenue?groupBy=1", token=owner)
check("el reporte factura la venta a crédito completa",
      report["total"] >= sale["total"], f'{report["total"]} vs {sale["total"]}')

# ------------------------------------------------------------ correcciones

print("\n[correcciones]")

payment = next(p for p in after["payments"] if p["reference"] == "TRF-9001")
status, fixed = api("DELETE", f"/api/sales/{sale_id}/payments/{payment['id']}", token=owner)
check("se borra un abono mal capturado", status == 200, f"{status} {fixed}")
check("y el saldo vuelve a subir", fixed["balance"] == balance, str(fixed.get("balance")))

status, paid = api("POST", f"/api/sales/{sale_id}/payments", {
    "amount": balance, "method": CARD,
}, token=owner)
check("se termina de pagar", status == 200, f"{status} {paid}")
check("y el saldo queda en cero", paid["balance"] == 0, str(paid.get("balance")))

_, pending = api("GET", "/api/sales?onlyUnpaid=true&pageSize=50", token=owner)
check("ya no está en cuentas por cobrar",
      sale_id not in [s["id"] for s in pending["items"]])

# ------------------------------------------------------------ estado de cuenta

print("\n[estado de cuenta]")

status, denied = api("GET", f"/api/customers/{buyer['id']}/statement", token=technician)
check("el técnico no ve el estado de cuenta de nadie", status == 403, str(status))

status, denied = api("GET", f"/api/customers/{buyer['id']}/statement", token=customer)
check("el cliente tampoco entra por la ruta del taller", status == 403, str(status))

status, faltante = api("GET", f"/api/customers/{buyer['id']}/statement/whatsapp", token=owner)
check("sin saldo no se manda un estado de cuenta en cero", status == 400, f"{status} {faltante}")

# Una venta a crédito nueva, ya que la de arriba quedó pagada.
status, aCredito = api("POST", "/api/sales", {
    "branchId": matriz["id"],
    "customerId": buyer["id"],
    "paymentMethod": CASH,
    "dueDate": "2099-12-31T00:00:00Z",
    "initialPayment": 300,
    "lines": [{"lineType": 2, "description": "Servicio para estado de cuenta",
               "quantity": 1, "unitPrice": 2000}],
}, token=owner)
check("se crea otra venta a crédito", status == 201, f"{status} {aCredito}")

api("POST", f"/api/sales/{aCredito['id']}/payments",
    {"amount": 200, "method": CARD, "reference": "REC-777"}, token=owner)

status, estado = api("GET", f"/api/customers/{buyer['id']}/statement", token=owner)
check("el Dueño ve el estado de cuenta", status == 200, f"{status} {estado}")
check("con el saldo total del cliente",
      abs(estado["total"] - (aCredito["total"] - 500)) < 0.01,
      f'{estado["total"]} vs {aCredito["total"] - 500}')

factura = next((s for s in estado["sales"] if s["number"] == aCredito["number"]), None)
check("trae la factura con saldo", factura is not None, str([s["number"] for s in estado["sales"]]))
check("con sus dos abonos", factura and len(factura["payments"]) == 2,
      str(len(factura["payments"]) if factura else None))
check("y el detalle de cada uno",
      factura and any(p["reference"] == "REC-777" for p in factura["payments"]),
      str(factura["payments"] if factura else None))
check("las pagadas no salen",
      all(s["number"] != cash_sale["number"] for s in estado["sales"]),
      str([s["number"] for s in estado["sales"]]))
check("no dice quién recibió el abono, que es interno",
      "registeredByName" not in json.dumps(estado), "aparece registeredByName")

status, pdf = api("GET", f"/api/customers/{buyer['id']}/statement/pdf", token=owner)
check("el PDF se genera", status == 200 and isinstance(pdf, bytes) and pdf[:4] == b"%PDF",
      str(status))

status, link = api("GET", f"/api/customers/{buyer['id']}/statement/whatsapp", token=owner)
check("con saldo sí hay enlace de WhatsApp", status == 200, f"{status} {link}")
check("apunta al teléfono del cliente", link["url"].startswith(f"https://wa.me/{buyer['phone']}"),
      link["url"][:60])
check("y el mensaje lleva el enlace público", "/c/" in link["message"], link["message"][-80:])

token_publico = link["message"].split("/c/")[1].strip()

status, publico = api("GET", f"/public/statements/{token_publico}")
check("el enlace público abre sin sesión", status == 200, str(status))
check("con el mismo total", abs(publico["total"] - estado["total"]) < 0.01,
      f'{publico["total"]} vs {estado["total"]}')
check("no filtra el costo del taller", "costTotal" not in json.dumps(publico))

status, pdfPublico = api("GET", f"/public/statements/{token_publico}/pdf")
check("y el PDF también", status == 200 and pdfPublico[:4] == b"%PDF", str(status))

status, inventado = api("GET", "/public/statements/11111111-1111-1111-1111-111111111111")
check("un token inventado no abre nada", status == 404, str(status))

# ------------------------------------------------------------ la factura

print("\n[la factura]")

status, pdf = api("GET", f"/api/sales/{sale_id}/pdf", token=owner, raw=True)
check("la factura pagada se baja", status == 200 and pdf[:4] == b"%PDF", str(status))

# Una con saldo, para que el PDF imprima el bloque de abonos.
_, open_sale = api("POST", "/api/sales", {
    "branchId": matriz["id"],
    "customerId": buyer["id"],
    "paymentMethod": CASH,
    "initialPayment": 0,
    "lines": [{"lineType": 2, "description": "Todo a crédito", "quantity": 1, "unitPrice": 500}],
}, token=owner)
check("se puede facturar sin cobrar nada", open_sale["amountPaid"] == 0,
      str(open_sale.get("amountPaid")))
check("y queda debiendo el total", open_sale["balance"] == open_sale["total"])

status, pdf = api("GET", f"/api/sales/{open_sale['id']}/pdf", token=owner, raw=True)
check("la factura con saldo también", status == 200 and pdf[:4] == b"%PDF", str(status))

_, mine = api("GET", "/api/sales?pageSize=50", token=customer)
check("el cliente ve sus facturas con su saldo",
      all("balance" in s for s in mine["items"]), str(len(mine["items"])))

print("\n[cierre de caja]")

# Un abono de hoy, para buscarlo después en la caja del día.
api("POST", f"/api/sales/{open_sale['id']}/payments", {
    "amount": 120, "method": TRANSFER, "reference": "CAJA-001",
}, token=owner)

status, caja = api("GET", "/api/reports/cash-close", token=owner)
check("el Dueño consulta lo cobrado hoy", status == 200, f"{status} {caja}")
check("el total cuadra con la suma de los abonos",
      abs(caja["total"] - sum(p["amount"] for p in caja["payments"])) < 0.01,
      f"{caja['total']} vs {sum(p['amount'] for p in caja['payments'])}")
check("y el conteo también", caja["paymentCount"] == len(caja["payments"]),
      f"{caja['paymentCount']} vs {len(caja['payments'])}")
check("aparece el abono que se acaba de registrar",
      any(p["reference"] == "CAJA-001" for p in caja["payments"]),
      str([p.get("reference") for p in caja["payments"]]))
check("con su forma de pago y su factura",
      any(p["reference"] == "CAJA-001" and p["method"] == TRANSFER
          and p["saleNumber"] == open_sale["number"] for p in caja["payments"]),
      str([p for p in caja["payments"] if p.get("reference") == "CAJA-001"]))
check("dice quién recibió cada uno",
      all(p["receiverName"] for p in caja["payments"]), str(caja["payments"][:1]))
check("el reparto por forma de pago suma el total",
      abs(sum(m["total"] for m in caja["byMethod"]) - caja["total"]) < 0.01,
      str(caja["byMethod"]))
check("y el reparto por quién lo recibió también",
      abs(sum(r["total"] for r in caja["byReceiver"]) - caja["total"]) < 0.01,
      str(caja["byReceiver"]))

# La caja es de lo cobrado: los abonos de una venta anulada no entran, pero se informan.
_, anulada = api("POST", "/api/sales", {
    "branchId": matriz["id"], "paymentMethod": CASH,
    "lines": [{"lineType": 2, "description": "Anular para la caja", "quantity": 1,
               "unitPrice": 250}],
}, token=owner)
api("POST", f"/api/sales/{anulada['id']}/void", {"reason": "Prueba del cierre de caja"}, owner)

status, con_anulada = api("GET", "/api/reports/cash-close", token=owner)
check("una venta anulada no suma en la caja",
      abs(con_anulada["total"] - caja["total"]) < 0.01,
      f"{con_anulada['total']} vs {caja['total']}")
check("pero se informa lo que quedó fuera", con_anulada["voidedCount"] >= 1,
      str(con_anulada.get("voidedCount")))
check("con su monto", con_anulada["voidedAmount"] >= 250, str(con_anulada.get("voidedAmount")))

_, ayer = api("GET", "/api/reports/cash-close?date=2020-01-15T12:00:00Z", token=owner)
check("un día sin cobros da cero", ayer["total"] == 0 and ayer["paymentCount"] == 0,
      f"{ayer.get('total')} {ayer.get('paymentCount')}")

status, denied = api("GET", "/api/reports/cash-close", token=technician)
check("el técnico no ve la caja", status == 403, str(status))

status, pdf_caja = api("GET", "/api/reports/cash-close.pdf", token=owner, raw=True)
check("el cierre se baja en PDF",
      status == 200 and pdf_caja[:4] == b"%PDF", str(status))

print(f"\n{ok} comprobaciones bien, {len(failed)} mal")
if failed:
    for name in failed:
        print(f"  · {name}")
    sys.exit(1)
