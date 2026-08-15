#!/usr/bin/env python3
"""Humo de la Fase 3: inventario.

Comprueba la regla de la que depende todo lo demás —el stock se deriva de movimientos, no se
edita— y el alcance por perfil. Escribe en la base: va contra el entorno local.

    python3 backend/tests/smoke/fase3_smoke.py
"""

import json
import sys
import urllib.error
import urllib.request

BASE = "http://localhost:5080"
PASSWORD = "Garaj123!"

# Espejo de Garaj.Domain.Enums.StockMovementType
IN, OUT, ADJUSTMENT, TRANSFER_IN, TRANSFER_OUT = 1, 2, 3, 4, 5

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


def login(email):
    status, data = api("POST", "/api/auth/login", {"email": email, "password": PASSWORD})
    if status != 200:
        sys.exit(f"No se pudo entrar como {email}: {status} {data}")
    return data["accessToken"]


def stock_of(token, branch_id, part_id):
    status, page = api("GET", f"/api/stock?branchId={branch_id}&partId={part_id}", token=token)
    if status != 200 or not page["items"]:
        return None
    return page["items"][0]


print("Fase 3 — inventario\n")

owner = login("owner@garaj.test")
tech1 = login("tecnico1@garaj.test")
tech2 = login("tecnico2@garaj.test")
customer = login("cliente@garaj.test")

_, branches = api("GET", "/api/branches", token=owner)
matriz = next(b for b in branches if b["code"] == "MTZ")
norte = next(b for b in branches if b["code"] == "SPS")

print("[catálogo]")

sku = "PRU-HUMO-01"
status, part = api("POST", "/api/parts", {
    "sku": sku, "name": "Amortiguador delantero de prueba", "brand": "Monroe",
    "category": "Suspensión", "unit": "u", "costPrice": 900, "salePrice": 1650,
}, owner)

if status == 409:
    # Quedó de una corrida anterior: se reutiliza en vez de fallar.
    _, page = api(f"GET", f"/api/parts?search={sku}", token=owner)
    part = page["items"][0]
    status = 200

check("el Dueño da de alta un repuesto", status in (200, 201), f"{status} {part}")
part_id = part["id"]

status, dup = api("POST", "/api/parts", {
    "sku": sku.lower(), "name": "Otro", "unit": "u", "costPrice": 1, "salePrice": 2,
}, owner)
check("el SKU repetido se rechaza sin importar mayúsculas", status == 409, f"{status} {dup}")

status, denied = api("POST", "/api/parts", {
    "sku": "PRU-TEC", "name": "X", "unit": "u", "costPrice": 1, "salePrice": 2,
}, tech1)
check("el técnico no administra el catálogo", status == 403, str(status))

status, denied = api("GET", "/api/parts", token=customer)
check("el cliente no ve el catálogo", status == 403, str(status))

status, categories = api("GET", "/api/parts/categories", token=owner)
check("las categorías salen para los filtros", status == 200 and "Frenos" in categories,
      f"{status} {categories}")

print("\n[entradas y kardex]")

status, item = api("POST", "/api/stock/receive", {
    "branchId": matriz["id"], "partId": part_id, "quantity": 10,
    "unitCost": 900, "reference": "Factura 001-2026",
}, owner)
check("la entrada crea la existencia en la sucursal", status == 200 and item["quantity"] == 10,
      f"{status} {item}")

status, item = api("POST", "/api/stock/receive", {
    "branchId": matriz["id"], "partId": part_id, "quantity": 5, "unitCost": 950,
}, owner)
check("una segunda entrada suma al saldo", item["quantity"] == 15, str(item.get("quantity")))

status, movements = api(
    "GET", f"/api/stock/movements?branchId={matriz['id']}&partId={part_id}", token=owner)
check("cada entrada dejó su movimiento", len(movements["items"]) == 2, str(len(movements["items"])))
check("el movimiento guarda el saldo resultante",
      movements["items"][0]["resultingQuantity"] == 15,
      str(movements["items"][0].get("resultingQuantity")))
check("el movimiento dice quién lo hizo", movements["items"][0]["movedByName"] == "Óscar Méndez")

status, refreshed = api("GET", f"/api/parts/{part_id}", token=owner)
check("el costo del catálogo sigue a la última compra", refreshed["costPrice"] == 950,
      str(refreshed.get("costPrice")))

status, error = api("POST", "/api/stock/receive", {
    "branchId": matriz["id"], "partId": part_id, "quantity": 0,
}, owner)
check("una entrada de cero se rechaza", status == 400, f"{status} {error}")

status, denied = api("POST", "/api/stock/receive", {
    "branchId": matriz["id"], "partId": part_id, "quantity": 1,
}, tech1)
check("el técnico no registra entradas", status == 403, str(status))

print("\n[ajuste por conteo]")

status, error = api("POST", "/api/stock/adjust", {
    "branchId": matriz["id"], "partId": part_id, "countedQuantity": 13, "reason": "",
}, owner)
check("un ajuste sin motivo se rechaza", status == 400, f"{status} {error}")

status, adjusted = api("POST", "/api/stock/adjust", {
    "branchId": matriz["id"], "partId": part_id, "countedQuantity": 13,
    "reason": "Conteo físico de agosto",
}, owner)
check("el ajuste deja el saldo en lo contado", adjusted["quantity"] == 13,
      str(adjusted.get("quantity")))

status, movements = api(
    "GET", f"/api/stock/movements?branchId={matriz['id']}&partId={part_id}&type={ADJUSTMENT}",
    token=owner)
faltante = movements["items"][0]
check("el faltante queda con signo negativo", faltante["signedQuantity"] == -2,
      str(faltante.get("signedQuantity")))
check("y con el motivo que se dio", faltante["notes"] == "Conteo físico de agosto")

print("\n[transferencia entre sucursales]")

status, error = api("POST", "/api/stock/transfer", {
    "fromBranchId": matriz["id"], "toBranchId": matriz["id"], "partId": part_id, "quantity": 1,
}, owner)
check("no se puede transferir a la misma sucursal", status == 400, f"{status} {error}")

status, error = api("POST", "/api/stock/transfer", {
    "fromBranchId": matriz["id"], "toBranchId": norte["id"], "partId": part_id, "quantity": 999,
}, owner)
check("transferir más de lo que hay se rechaza", status == 409, f"{status} {error}")
check("y el error dice cuánto queda", "13" in str(error.get("detail", "")), str(error))

status, both = api("POST", "/api/stock/transfer", {
    "fromBranchId": matriz["id"], "toBranchId": norte["id"], "partId": part_id,
    "quantity": 4, "notes": "Reposición de la Norte",
}, owner)
check("la transferencia responde con ambos saldos", status == 200 and len(both) == 2, f"{status} {both}")

origin = stock_of(owner, matriz["id"], part_id)
destination = stock_of(owner, norte["id"], part_id)
check("el origen bajó", origin["quantity"] == 9, str(origin.get("quantity")))
check("el destino subió", destination["quantity"] == 4, str(destination.get("quantity")))

status, movements = api(
    "GET", f"/api/stock/movements?branchId={norte['id']}&partId={part_id}&type={TRANSFER_IN}",
    token=owner)
check("el destino sabe de qué sucursal vino",
      movements["items"][0]["counterpartBranchName"] == "Matriz",
      str(movements["items"][0].get("counterpartBranchName")))

print("\n[alcance por sucursal]")

status, page = api("GET", "/api/stock", token=tech1)
check("el técnico ve existencias", status == 200 and page["total"] > 0, str(status))
check("pero solo las de su sucursal",
      all(i["branchName"] == "Matriz" for i in page["items"]),
      str({i["branchName"] for i in page["items"]}))

status, denied = api("GET", f"/api/stock?branchId={norte['id']}", token=tech1)
check("pedir otra sucursal devuelve 404", status == 404, str(status))

status, denied = api("GET", "/api/stock", token=customer)
check("el cliente no ve el inventario", status == 403, str(status))

status, alerts = api("GET", "/api/stock/alerts", token=owner)
check("la alerta de reposición responde", status == 200, str(status))
check("y trae lo que está en el mínimo",
      any(a["sku"].startswith("BAT") for a in alerts), str([a["sku"] for a in alerts]))
check("todo lo alertado está marcado", all(a["isBelowMinimum"] for a in alerts))

print("\n[consumo desde la orden de trabajo]")

_, orders = api("GET", "/api/work-orders?onlyOpen=true&pageSize=50", token=tech1)
if not orders["items"]:
    sys.exit("El técnico 1 no tiene órdenes abiertas. Recree la base y siembre de nuevo.")

order_id = orders["items"][0]["id"]
before = stock_of(owner, matriz["id"], part_id)["quantity"]

status, line = api("POST", f"/api/work-orders/{order_id}/parts", {
    "partId": part_id, "quantity": 2,
}, tech1)
check("el técnico carga el repuesto a su orden", status == 200, f"{status} {line}")
check("congela el precio de venta del catálogo", line["unitPrice"] == 1650,
      str(line.get("unitPrice")))
check("y el costo, para el margen", line["unitCost"] == 950, str(line.get("unitCost")))
check("con su total", line["total"] == 3300, str(line.get("total")))

after = stock_of(owner, matriz["id"], part_id)["quantity"]
check("la bodega bajó exactamente lo consumido", after == before - 2, f"{before} → {after}")

status, movements = api(
    "GET", f"/api/stock/movements?branchId={matriz['id']}&partId={part_id}&type={OUT}", token=owner)
check("la salida quedó ligada a la orden",
      movements["items"][0]["workOrderNumber"] == orders["items"][0]["number"],
      str(movements["items"][0].get("workOrderNumber")))

status, detail = api("GET", f"/api/work-orders/{order_id}", token=tech1)
check("el repuesto aparece en el detalle de la orden",
      any(p["partId"] == part_id for p in detail["parts"]))
check("y el total de repuestos está sumado", detail["partsTotal"] >= 3300,
      str(detail.get("partsTotal")))

status, error = api("POST", f"/api/work-orders/{order_id}/parts", {
    "partId": part_id, "quantity": 9999,
}, tech1)
check("consumir más de lo que hay se rechaza", status == 409, f"{status} {error}")
check("y el mensaje dice qué hacer", "ajuste" in str(error.get("detail", "")).lower(), str(error))

status, denied = api("POST", f"/api/work-orders/{order_id}/parts", {
    "partId": part_id, "quantity": 1,
}, tech2)
check("otro técnico no carga repuestos a una orden ajena", status == 404, str(status))

status, denied = api("POST", f"/api/work-orders/{order_id}/parts", {
    "partId": part_id, "quantity": 1,
}, customer)
check("el cliente no carga repuestos", status == 403, str(status))

status, customer_detail = api("GET", f"/api/work-orders/{order_id}", token=customer)
if status == 200:
    check("el cliente ve qué repuestos le pusieron", len(customer_detail["parts"]) > 0)
    check("pero no el costo del taller",
          all(p["unitCost"] == 0 for p in customer_detail["parts"]),
          str([p["unitCost"] for p in customer_detail["parts"]]))
else:
    check("el cliente ve qué repuestos le pusieron", False, str(status))

print("\n[repuesto cargado a mano]")

# Lo que se compró de encargo y nunca pasó por bodega: no hay existencia que descontar.
antes_manual = stock_of(owner, matriz["id"], part_id)["quantity"]

status, manual = api("POST", f"/api/work-orders/{order_id}/parts", {
    "description": "Faro delantero izquierdo, traído de encargo",
    "quantity": 1, "unitPrice": 2450, "unitCost": 1800,
}, tech1)
check("el técnico carga un repuesto a mano", status == 200, f"{status} {manual}")
check("sin repuesto del catálogo detrás", manual["partId"] is None, str(manual.get("partId")))
check("con el concepto que escribió",
      manual["partName"] == "Faro delantero izquierdo, traído de encargo",
      str(manual.get("partName")))
check("sin código de catálogo", manual["sku"] == "", repr(manual.get("sku")))
check("con el precio que le pusieron", manual["unitPrice"] == 2450, str(manual.get("unitPrice")))
check("y su costo, para el margen", manual["unitCost"] == 1800, str(manual.get("unitCost")))

check("no movió la bodega",
      stock_of(owner, matriz["id"], part_id)["quantity"] == antes_manual,
      str(antes_manual))

status, sin_precio = api("POST", f"/api/work-orders/{order_id}/parts", {
    "description": "Sin precio", "quantity": 1,
}, tech1)
check("uno a mano sin precio se rechaza", status == 400, f"{status} {sin_precio}")

status, sin_concepto = api("POST", f"/api/work-orders/{order_id}/parts", {
    "quantity": 1, "unitPrice": 100,
}, tech1)
check("y sin decir qué es, también", status == 400, f"{status} {sin_concepto}")

status, _ = api("DELETE", f"/api/work-orders/{order_id}/parts/{manual['id']}", token=tech1)
check("quitarlo responde 204", status == 204, str(status))
check("y no devolvió nada a la bodega, porque nunca salió de ella",
      stock_of(owner, matriz["id"], part_id)["quantity"] == antes_manual,
      str(antes_manual))

print("\n[devolución]")

status, _ = api("DELETE", f"/api/work-orders/{order_id}/parts/{line['id']}", token=tech1)
check("quitar el repuesto de la orden responde 204", status == 204, str(status))

returned = stock_of(owner, matriz["id"], part_id)["quantity"]
check("la bodega recupera lo devuelto", returned == before, f"{after} → {returned}")

status, movements = api(
    "GET", f"/api/stock/movements?branchId={matriz['id']}&partId={part_id}&type={IN}", token=owner)
check("la devolución entra como movimiento, no borra el histórico",
      "Devolución" in str(movements["items"][0].get("reference", "")),
      str(movements["items"][0].get("reference")))

status, detail = api("GET", f"/api/work-orders/{order_id}", token=tech1)
check("y desaparece del detalle", not any(p["id"] == line["id"] for p in detail["parts"]))

print("\n[trabajos frecuentes]")

# Se le pone un paso a la orden para que haya algo que guardar como plantilla.
_, services = api("GET", "/api/labor-services", token=owner)
service_id = services[0]["id"]

api("POST", f"/api/work-orders/{order_id}/tasks",
    {"title": "Revisión de humo", "laborServiceId": service_id}, owner)
_, semilla = api("POST", f"/api/work-orders/{order_id}/parts",
                 {"partId": part_id, "quantity": 2}, owner)

nombre = "Trabajo de humo"
status, plantilla = api("POST", "/api/job-templates/from-work-order",
                        {"workOrderId": order_id, "name": nombre}, owner)

if status == 409:
    # Quedó de una corrida anterior: se reutiliza en vez de fallar.
    _, existentes = api("GET", "/api/job-templates?includeInactive=true", token=owner)
    plantilla = next(t for t in existentes if t["name"] == nombre)
    status = 201

check("una orden ya armada se guarda como trabajo frecuente", status in (200, 201),
      f"{status} {plantilla}")
check("y se lleva los pasos y los repuestos que tenía",
      len(plantilla["tasks"]) >= 1 and len(plantilla["parts"]) >= 1,
      f"{len(plantilla['tasks'])} pasos, {len(plantilla['parts'])} repuestos")

template_id = plantilla["id"]

# El repuesto ya cumplió su papel —quedar dentro de la plantilla—: se devuelve a la bodega para
# medir sobre limpio que aplicarla no mueve el inventario.
if isinstance(semilla, dict) and "id" in semilla:
    api("DELETE", f"/api/work-orders/{order_id}/parts/{semilla['id']}", token=owner)

status, dup = api("POST", "/api/job-templates",
                  {"name": nombre, "isActive": True,
                   "tasks": [{"title": "X"}], "parts": []}, owner)
check("el nombre repetido se rechaza", status == 409, f"{status} {dup}")

# Editarla reemplaza pasos y repuestos enteros, no los duplica ni deja huérfanos.
status, editada = api("PUT", f"/api/job-templates/{template_id}",
                      {"name": nombre, "description": "Editado por el humo", "isActive": True,
                       "tasks": [{"title": "Único paso", "laborServiceId": service_id}],
                       "parts": [{"partId": part_id, "quantity": 3}]}, owner)
check("editarla responde 200", status == 200, f"{status} {editada}")
check("y reemplaza los pasos enteros, no los suma",
      len(editada["tasks"]) == 1 and len(editada["parts"]) == 1,
      f"{len(editada['tasks'])} pasos, {len(editada['parts'])} repuestos")

status, releida = api("GET", f"/api/job-templates/{template_id}", token=owner)
check("y al releerla siguen siendo esos, no los viejos",
      len(releida["tasks"]) == 1 and releida["parts"][0]["quantity"] == 3,
      f"{len(releida['tasks'])} pasos, {releida['parts'][0]['quantity']}")

plantilla = releida

status, denied = api("POST", "/api/job-templates",
                     {"name": "Del técnico", "isActive": True,
                      "tasks": [{"title": "X"}], "parts": []}, tech1)
check("el técnico no administra los trabajos frecuentes", status == 403, str(status))

status, listado = api("GET", "/api/job-templates", token=tech1)
check("pero sí los lista, que es lo que aplica en el patio", status == 200, str(status))

status, denied = api("GET", "/api/job-templates", token=customer)
check("el cliente no los ve", status == 403, str(status))

# La prueba que importa: aplicar no puede mover el inventario.
antes = stock_of(owner, matriz["id"], part_id)["quantity"]
_, previo = api("GET", f"/api/work-orders/{order_id}", token=owner)
pasos_previos = len(previo["tasks"])

status, aplicado = api("POST", f"/api/work-orders/{order_id}/apply-template",
                       {"templateId": template_id}, tech1)
check("el técnico aplica el trabajo frecuente", status == 200, f"{status} {aplicado}")

_, despues = api("GET", f"/api/work-orders/{order_id}", token=owner)
check("los pasos se anexan a los que ya había, no los reemplazan",
      len(despues["tasks"]) == pasos_previos + len(plantilla["tasks"]),
      f"{pasos_previos} → {len(despues['tasks'])}")

check("aplicarlo NO mueve el inventario: los repuestos se proponen, no se cargan",
      stock_of(owner, matriz["id"], part_id)["quantity"] == antes,
      f"{antes} → {stock_of(owner, matriz['id'], part_id)['quantity']}")
check("y vienen con la existencia de esa bodega, para ver que no hay antes de intentarlo",
      any(p["partId"] == part_id and p["available"] == antes
          for p in aplicado["suggestedParts"]),
      str(aplicado["suggestedParts"]))

# Cargar uno propuesto sí descuenta: es el camino normal, el de siempre.
sugerido = next(p for p in aplicado["suggestedParts"] if p["partId"] == part_id)
status, cargado = api("POST", f"/api/work-orders/{order_id}/parts",
                      {"partId": sugerido["partId"], "quantity": sugerido["quantity"]}, tech1)
check("cargar un repuesto propuesto sí descuenta la bodega",
      status == 200 and stock_of(owner, matriz["id"], part_id)["quantity"]
      == antes - sugerido["quantity"],
      f"{antes} → {stock_of(owner, matriz['id'], part_id)['quantity']}")
api("DELETE", f"/api/work-orders/{order_id}/parts/{cargado['id']}", token=tech1)

# El precio no vive en la plantilla: sale del catálogo cada vez que se pregunta.
_, refrescada = api("GET", f"/api/job-templates/{template_id}", token=owner)
esperado = sum(p["quantity"] for p in refrescada["parts"] if p["partId"] == part_id) * 1650
check("el precio del trabajo sale del catálogo, no de lo que se guardó",
      any(abs(p["unitPrice"] - 1650) < 0.01 for p in refrescada["parts"] if p["partId"] == part_id)
      and refrescada["partsTotal"] >= esperado,
      f"{refrescada['partsTotal']} vs {esperado}")
check("y se cuenta cuántas veces se ha usado, para poner arriba lo que más se repite",
      refrescada["usageCount"] >= 1, str(refrescada["usageCount"]))

status, _ = api("DELETE", f"/api/job-templates/{template_id}", token=owner)
check("darlo de baja responde 204", status == 204, str(status))

status, listado = api("GET", "/api/job-templates", token=owner)
check("y deja de aparecer al armar una orden",
      not any(t["id"] == template_id for t in listado), str(len(listado)))

status, baja = api("POST", f"/api/work-orders/{order_id}/apply-template",
                   {"templateId": template_id}, owner)
check("aplicar uno dado de baja se rechaza", status == 409, f"{status} {baja}")

print(f"\n{ok} comprobaciones correctas, {len(failed)} fallidas")
if failed:
    for name in failed:
        print(f"  - {name}")
    sys.exit(1)
