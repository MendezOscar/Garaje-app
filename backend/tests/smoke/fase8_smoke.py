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

print(f"\n{ok} comprobaciones bien, {len(failed)} mal")
if failed:
    for name in failed:
        print(f"  · {name}")
    sys.exit(1)
