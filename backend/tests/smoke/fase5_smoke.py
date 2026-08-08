#!/usr/bin/env python3
"""Humo de la Fase 5: ventas y reportes.

Comprueba que el cierre de una orden factura lo trabajado, que la venta de mostrador
descuenta inventario, que anular devuelve el stock, y que los reportes separan repuestos de
mano de obra. Escribe en la base: va contra el entorno local.

    python3 backend/tests/smoke/fase5_smoke.py
"""

import json
import sys
import urllib.error
import urllib.request

BASE = "http://localhost:5080"
PASSWORD = "Garaj123!"

# Espejo de Garaj.Domain.Enums
PART, LABOR = 1, 2
CASH = 1
DELIVERED = 8
DAY, WEEK, MONTH = 1, 2, 3

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


def stock_of(token, branch_id, part_id):
    _, page = api("GET", f"/api/stock?branchId={branch_id}&partId={part_id}", token=token)
    return page["items"][0]["quantity"] if page.get("items") else None


print("Fase 5 — ventas y reportes\n")

owner = login("owner@garaj.test")
tech1 = login("tecnico1@garaj.test")
customer = login("cliente@garaj.test")

_, branches = api("GET", "/api/branches", token=owner)
matriz = next(b for b in branches if b["code"] == "MTZ")

_, parts_page = api("GET", "/api/parts?pageSize=10", token=owner)
part = parts_page["items"][0]

_, services = api("GET", "/api/labor-services", token=owner)
service = services[0]

print("[cierre de la orden]")

_, orders = api("GET", "/api/work-orders?onlyOpen=true&pageSize=50", token=tech1)
if not orders["items"]:
    sys.exit("El técnico 1 no tiene órdenes abiertas. Recree la base y siembre de nuevo.")

order = orders["items"][0]
order_id = order["id"]

# Se carga un repuesto y se le pone servicio a un paso para que haya qué cobrar.
api("POST", f"/api/work-orders/{order_id}/parts", {"partId": part["id"], "quantity": 2}, owner)

_, detail = api("GET", f"/api/work-orders/{order_id}", token=owner)
if detail["tasks"]:
    api("PUT", f"/api/work-orders/{order_id}/tasks/{detail['tasks'][0]['id']}", {
        "title": detail["tasks"][0]["title"], "laborServiceId": service["id"], "estimatedHours": 2,
    }, owner)

stock_before = stock_of(owner, matriz["id"], part["id"])

status, sale = api("POST", "/api/sales/close-work-order", {
    "workOrderId": order_id, "paymentMethod": CASH, "notes": "Prueba de humo",
}, owner)
check("el Dueño cierra la orden y genera la venta", status in (200, 201), f"{status} {sale}")

check("el número es correlativo por sucursal", sale["number"].startswith("VTA-"),
      str(sale.get("number")))
check("factura el repuesto consumido",
      any(l["lineType"] == PART and l["partId"] == part["id"] for l in sale["lines"]))
check("factura la mano de obra del paso",
      any(l["lineType"] == LABOR for l in sale["lines"]), str(sale["lines"]))

expected_subtotal = sum(l["quantity"] * l["unitPrice"] for l in sale["lines"])
check("el subtotal suma las líneas", abs(sale["subtotal"] - expected_subtotal) < 0.01,
      f"{sale['subtotal']} vs {expected_subtotal}")
check("el total incluye el ISV",
      abs(sale["total"] - (sale["subtotal"] - sale["discountTotal"] + sale["taxTotal"])) < 0.02)
check("el margen descuenta el costo",
      abs(sale["margin"] - (sale["total"] - sale["costTotal"])) < 0.01,
      f"{sale.get('margin')} vs {sale['total']} - {sale['costTotal']}")
check("la mano de obra no lleva costo",
      all(l["unitCost"] == 0 for l in sale["lines"] if l["lineType"] == LABOR))

stock_after = stock_of(owner, matriz["id"], part["id"])
check("cerrar no vuelve a descontar el repuesto", stock_after == stock_before,
      f"{stock_before} → {stock_after}")

_, detail = api("GET", f"/api/work-orders/{order_id}", token=owner)
check("la orden queda entregada", detail["status"] == DELIVERED, str(detail.get("status")))
check("y lo dice en su línea de tiempo",
      any("facturado" in (t.get("note") or "") for t in detail["timeline"]))

status, again = api("POST", "/api/sales/close-work-order", {
    "workOrderId": order_id, "paymentMethod": CASH,
}, owner)
check("no se puede facturar dos veces la misma orden", status == 409, f"{status} {again}")
check("y el mensaje dice qué hacer", "anúlela" in str(again.get("detail", "")).lower(), str(again))

print("\n[venta de mostrador]")

before = stock_of(owner, matriz["id"], part["id"])

status, counter = api("POST", "/api/sales", {
    "branchId": matriz["id"], "paymentMethod": CASH,
    "lines": [
        {"lineType": PART, "partId": part["id"], "quantity": 3},
        {"lineType": LABOR, "description": "Montaje rápido", "quantity": 1, "unitPrice": 300},
    ],
}, owner)
check("el Dueño registra una venta de mostrador", status in (200, 201), f"{status} {counter}")

after = stock_of(owner, matriz["id"], part["id"])
check("descuenta el repuesto de la bodega", after == before - 3, f"{before} → {after}")
check("acepta mano de obra sin catálogo",
      any(l["lineType"] == LABOR and l["total"] == 300 for l in counter["lines"]))

status, error = api("POST", "/api/sales", {
    "branchId": matriz["id"], "paymentMethod": CASH, "lines": [],
}, owner)
check("una venta sin líneas se rechaza", status == 400, f"{status} {error}")

status, error = api("POST", "/api/sales", {
    "branchId": matriz["id"], "paymentMethod": CASH,
    "lines": [{"lineType": PART, "partId": part["id"], "quantity": 99999}],
}, owner)
check("vender más de lo que hay se rechaza", status == 409, f"{status} {error}")

print("\n[anulación]")

status, error = api("POST", f"/api/sales/{counter['id']}/void", {"reason": ""}, owner)
check("anular sin motivo se rechaza", status == 400, f"{status} {error}")

status, voided = api("POST", f"/api/sales/{counter['id']}/void", {
    "reason": "Prueba de humo: se anula",
}, owner)
check("el Dueño anula la venta", status == 200 and voided["isVoided"], f"{status} {voided}")

returned = stock_of(owner, matriz["id"], part["id"])
check("el repuesto vuelve a la bodega", returned == before, f"{after} → {returned}")

status, twice = api("POST", f"/api/sales/{counter['id']}/void", {"reason": "otra vez"}, owner)
check("anular dos veces se rechaza", status == 409, f"{status} {twice}")

status, listed = api("GET", "/api/sales", token=owner)
check("la anulada no sale en el listado por defecto",
      all(s["id"] != counter["id"] for s in listed["items"]))

status, with_voided = api("GET", "/api/sales?includeVoided=true", token=owner)
check("pero sigue estando, no se borró",
      any(s["id"] == counter["id"] for s in with_voided["items"]))

print("\n[alcance por perfil]")

status, denied = api("GET", "/api/sales", token=tech1)
check("el técnico no ve las ventas", status == 403, str(status))

status, mine = api("GET", "/api/sales", token=customer)
check("el cliente ve sus facturas", status == 200, str(status))

if mine.get("items"):
    status, invoice = api("GET", f"/api/sales/{mine['items'][0]['id']}", token=customer)
    check("pero no el costo del taller", invoice["costTotal"] == 0 and invoice["margin"] == 0,
          f"costo {invoice.get('costTotal')} margen {invoice.get('margin')}")
else:
    check("pero no el costo del taller", False, "el cliente no tiene facturas")

status, denied = api("GET", "/api/reports/revenue", token=tech1)
check("el técnico no ve reportes", status == 403, str(status))

status, denied = api("GET", "/api/reports/dashboard", token=customer)
check("el cliente tampoco", status == 403, str(status))

print("\n[reportes]")

status, report = api("GET", f"/api/reports/revenue?groupBy={DAY}", token=owner)
check("el reporte de ingresos responde", status == 200, f"{status} {report}")
check("separa repuestos de mano de obra",
      report["partsRevenue"] > 0 and report["laborRevenue"] > 0,
      f"repuestos {report.get('partsRevenue')} mano de obra {report.get('laborRevenue')}")
check("el total es la suma de ambos",
      abs(report["total"] - (report["partsRevenue"] + report["laborRevenue"])) < 0.01)
check("el margen descuenta el costo",
      abs(report["margin"] - (report["total"] - report["cost"])) < 0.01)
check("y da el porcentaje", report["marginPercent"] > 0, str(report.get("marginPercent")))
check("trae puntos por periodo", len(report["points"]) > 0)
check("los puntos suman el total",
      abs(sum(p["total"] for p in report["points"]) - report["total"]) < 0.05,
      f"{sum(p['total'] for p in report['points'])} vs {report['total']}")
check("desglosa por sucursal", len(report["branches"]) > 0)
check("y trae los repuestos más vendidos", len(report["topParts"]) > 0)

check("la venta anulada no cuenta en el reporte",
      report["saleCount"] == 1, f"contó {report.get('saleCount')} ventas")

status, weekly = api("GET", f"/api/reports/revenue?groupBy={WEEK}", token=owner)
check("agrupa por semana", status == 200 and weekly["points"][0]["periodLabel"].startswith("sem."),
      str(weekly["points"][0].get("periodLabel") if weekly.get("points") else weekly))

status, monthly = api("GET", f"/api/reports/revenue?groupBy={MONTH}", token=owner)
check("y por mes", status == 200 and len(monthly["points"]) == 1,
      str(len(monthly.get("points", []))))
check("el total no cambia al reagrupar",
      abs(monthly["total"] - report["total"]) < 0.01,
      f"{monthly['total']} vs {report['total']}")

status, error = api("GET", "/api/reports/revenue?from=2030-01-01&to=2020-01-01", token=owner)
check("un rango invertido se rechaza", status == 400, f"{status} {error}")

status, csv = api("GET", f"/api/reports/revenue.csv?groupBy={DAY}", token=owner)
check("el CSV se descarga", status == 200 and isinstance(csv, bytes), str(status))
check("con encabezado y total",
      b"Mano de obra" in csv and b"TOTAL" in csv, str(csv[:80] if isinstance(csv, bytes) else csv))

print("\n[tablero]")

status, dashboard = api("GET", "/api/reports/dashboard", token=owner)
check("el tablero responde", status == 200, f"{status} {dashboard}")
check("muestra el ingreso del mes", dashboard["revenueMonth"] > 0, str(dashboard.get("revenueMonth")))
check("el del día no supera al del mes", dashboard["revenueToday"] <= dashboard["revenueMonth"])
check("cuenta las órdenes abiertas", dashboard["openWorkOrders"] >= 0)
check("suma por estado lo mismo que el total",
      sum(s["count"] for s in dashboard["workOrdersByStatus"]) == dashboard["openWorkOrders"],
      f"{sum(s['count'] for s in dashboard['workOrdersByStatus'])} vs {dashboard['openWorkOrders']}")
check("alerta de repuestos bajo mínimo", dashboard["partsBelowMinimum"] >= 1,
      str(dashboard.get("partsBelowMinimum")))
check("trae los últimos 14 días", len(dashboard["lastDays"]) == 14,
      str(len(dashboard.get("lastDays", []))))

print(f"\n{ok} comprobaciones correctas, {len(failed)} fallidas")
if failed:
    for name in failed:
        print(f"  - {name}")
    sys.exit(1)
