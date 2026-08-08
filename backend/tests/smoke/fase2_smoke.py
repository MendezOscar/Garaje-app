#!/usr/bin/env python3
"""Humo de la Fase 2: evidencia fotográfica.

Recorre el flujo real —pedir URL prefirmada, subir el binario al bucket, confirmar,
listar, borrar— y comprueba el alcance por perfil. Sube archivos de verdad a MinIO
y escribe en la base, así que va contra el entorno local, nunca contra producción.

    python3 backend/tests/smoke/fase2_smoke.py
"""


import json
import struct
import sys
import urllib.error
import urllib.request
import zlib

BASE = "http://localhost:5080"

# La API serializa los enums como enteros, igual que en el resto de fases.
SERVICE_REQUEST, WORK_ORDER, WORK_ORDER_TASK = 1, 2, 3
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


def request(method, url, body=None, token=None, raw=None, content_type=None):
    """Devuelve (status, payload). payload es dict/list si la respuesta es JSON."""
    data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
    req = urllib.request.Request(url, data=data, method=method)

    if content_type:
        req.add_header("Content-Type", content_type)
    elif data is not None and raw is None:
        req.add_header("Content-Type", "application/json")

    if token:
        req.add_header("Authorization", f"Bearer {token}")

    try:
        with urllib.request.urlopen(req) as response:
            payload = response.read()
            try:
                return response.status, json.loads(payload)
            except (ValueError, UnicodeDecodeError):
                return response.status, payload
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
        print(f"No se pudo entrar como {email}: {status} {data}")
        sys.exit(1)
    return data["accessToken"], data["user"]


def png(width, height, color):
    """PNG mínimo generado a mano: evita depender de Pillow para tener un archivo real."""

    def chunk(tag, payload):
        body = tag + payload
        return struct.pack(">I", len(payload)) + body + struct.pack(">I", zlib.crc32(body))

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    rows = b"".join(b"\x00" + bytes(color) * width for _ in range(height))

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(rows))
            + chunk(b"IEND", b""))


def upload(token, owner_type, owner_id, image, caption=None, visible=True, content_type="image/png"):
    """Flujo completo de subida. Devuelve (status_de_la_url, dto_confirmado_o_error)."""
    status, presigned = api("POST", "/api/media/upload-url", {
        "ownerType": owner_type,
        "ownerId": owner_id,
        "contentType": content_type,
        "sizeBytes": len(image),
        "fileName": "evidencia.png",
        "caption": caption,
        "isVisibleToCustomer": visible,
    }, token)

    if status != 200:
        return status, presigned

    put_status, _ = request(
        "PUT", presigned["uploadUrl"], raw=image,
        content_type=presigned["headers"]["Content-Type"])

    if put_status not in (200, 204):
        return put_status, {"detail": "el PUT al bucket falló"}

    return api("POST", f"/api/media/{presigned['attachmentId']}/confirm", token=token)


print("Fase 2 — evidencia fotográfica\n")

owner_token, owner = login("owner@garaj.test")
tech1_token, tech1 = login("tecnico1@garaj.test")
tech2_token, _ = login("tecnico2@garaj.test")
customer_token, _ = login("cliente@garaj.test")

# Una orden de la sucursal Matriz, que es la del técnico 1.
_, orders = api("GET", "/api/work-orders?onlyOpen=true&pageSize=200", token=tech1_token)
if not orders["items"]:
    print("El técnico 1 no tiene órdenes abiertas. Recree la base y siembre de nuevo.")
    sys.exit(1)

order = orders["items"][0]
order_id = order["id"]

_, detail = api("GET", f"/api/work-orders/{order_id}", token=tech1_token)
task_id = detail["tasks"][0]["id"] if detail["tasks"] else None

photo = png(64, 48, (200, 30, 30))
big_photo = png(1200, 900, (20, 120, 200))

print("\n[subida]")

status, attachment = upload(tech1_token, WORK_ORDER, order_id, photo, caption="Golpe en el guardafango")
check("el técnico sube una foto a su orden", status == 200, f"{status} {attachment}")

if status != 200:
    print("\nSin una subida válida no tiene sentido seguir.")
    sys.exit(1)

check("la foto queda con su leyenda", attachment.get("caption") == "Golpe en el guardafango")
check("la URL de lectura es prefirmada", "X-Amz-Signature" in attachment.get("url", ""))
check("la miniatura se generó aparte del original",
      attachment.get("thumbnailUrl") and attachment["thumbnailUrl"] != attachment["url"])
check("registra quién la tomó", attachment.get("uploadedByName") == "Luis Cabrera")

status, big = upload(tech1_token, WORK_ORDER, order_id, big_photo, caption="Motor abierto", visible=False)
check("una foto grande también se procesa", status == 200, f"{status} {big}")

if task_id:
    status, task_photo = upload(tech1_token, WORK_ORDER_TASK, task_id, photo, caption="Paso 1 listo")
    check("el técnico documenta un paso concreto", status == 200, f"{status} {task_photo}")
    check("la foto del paso dice a qué paso pertenece", task_photo.get("taskTitle"))

print("\n[validación de entrada]")

status, error = api("POST", "/api/media/upload-url", {
    "ownerType": WORK_ORDER, "ownerId": order_id,
    "contentType": "application/pdf", "sizeBytes": 100,
}, tech1_token)
check("rechaza un tipo de archivo no admitido", status == 400, f"{status} {error}")

status, error = api("POST", "/api/media/upload-url", {
    "ownerType": WORK_ORDER, "ownerId": order_id,
    "contentType": "image/jpeg", "sizeBytes": 40 * 1024 * 1024,
}, tech1_token)
check("rechaza un archivo por encima del tope", status == 400, f"{status} {error}")

status, presigned = api("POST", "/api/media/upload-url", {
    "ownerType": WORK_ORDER, "ownerId": order_id,
    "contentType": "image/png", "sizeBytes": len(photo),
}, tech1_token)
status, error = api("POST", f"/api/media/{presigned['attachmentId']}/confirm", token=tech1_token)
check("confirmar sin haber subido el archivo devuelve 409", status == 409, f"{status} {error}")

print("\n[galería]")

status, gallery = api("GET", f"/api/media/work-order/{order_id}", token=tech1_token)
check("la galería de la orden responde", status == 200, f"{status} {gallery}")
check("trae las fotos de la orden y las de sus pasos", len(gallery) >= 3, f"trajo {len(gallery)}")
check("vienen ordenadas por fecha de toma",
      [m["takenAt"] for m in gallery] == sorted(m["takenAt"] for m in gallery))
check("ninguna sin confirmar se cuela", all(m["url"] for m in gallery))

status, only_order = api("GET", f"/api/media?ownerType={WORK_ORDER}&ownerId={order_id}", token=tech1_token)
check("el listado por dueño filtra bien", all(m["ownerType"] == WORK_ORDER for m in only_order))

print("\n[alcance por perfil]")

status, _ = api("GET", f"/api/media/work-order/{order_id}", token=owner_token)
check("el Dueño ve la galería de cualquier orden", status == 200, str(status))

status, foreign = api("GET", f"/api/media/work-order/{order_id}", token=tech2_token)
check("otro técnico no ve la galería de una orden ajena", status == 404, f"{status} {foreign}")

status, denied = upload(tech2_token, WORK_ORDER, order_id, photo)
check("otro técnico no puede subir a una orden ajena", status == 404, f"{status} {denied}")

status, customer_gallery = api("GET", f"/api/media/work-order/{order_id}", token=customer_token)
if status == 200:
    check("el cliente ve la galería de su vehículo", True)
    check("no ve las fotos marcadas como internas",
          all(m["isVisibleToCustomer"] for m in customer_gallery))
    check("ve menos fotos que el taller", len(customer_gallery) < len(gallery),
          f"cliente {len(customer_gallery)} vs taller {len(gallery)}")
else:
    check("el cliente ve la galería de su vehículo", False, f"{status} {customer_gallery}")

status, denied = upload(customer_token, WORK_ORDER, order_id, photo)
check("el cliente no adjunta fotos a la orden de trabajo", status == 403, f"{status} {denied}")

print("\n[requerimientos]")

_, requests_page = api("GET", "/api/service-requests?pageSize=200", token=customer_token)
if requests_page["items"]:
    request_id = requests_page["items"][0]["id"]

    status, own = upload(customer_token, SERVICE_REQUEST, request_id, photo, caption="Así suena el ruido")
    check("el cliente sí adjunta fotos a su requerimiento", status == 200, f"{status} {own}")

    status, seen = api("GET", f"/api/media?ownerType={SERVICE_REQUEST}&ownerId={request_id}", token=owner_token)
    check("el Dueño ve lo que adjuntó el cliente", status == 200 and len(seen) >= 1, f"{status} {seen}")
else:
    check("el cliente tiene un requerimiento con el que probar", False)

print("\n[borrado]")

target = attachment["id"]

status, _ = api("DELETE", f"/api/media/{target}", token=tech2_token)
check("un técnico ajeno no puede borrar la foto", status == 404, str(status))

status, _ = api("DELETE", f"/api/media/{target}", token=customer_token)
check("el cliente no puede borrar evidencia del taller", status in (403, 404), str(status))

status, _ = api("DELETE", f"/api/media/{target}", token=tech1_token)
check("el técnico borra su propia foto", status == 204, str(status))

status, after = api("GET", f"/api/media/work-order/{order_id}", token=tech1_token)
check("la foto borrada desaparece de la galería",
      all(m["id"] != target for m in after))

status, _ = api("DELETE", f"/api/media/{target}", token=tech1_token)
check("borrarla otra vez devuelve 404", status == 404, str(status))

print(f"\n{ok} comprobaciones correctas, {len(failed)} fallidas")
if failed:
    for name in failed:
        print(f"  - {name}")
    sys.exit(1)
