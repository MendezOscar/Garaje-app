#!/usr/bin/env python3
"""Humo de la postventa: reabrir una orden, anular con plazo y la ficha de recepción.

Son las cosas que se deshacen después de cobrar, que es donde más caro sale equivocarse: una
orden reabierta con la factura viva serían dos cobros por el mismo trabajo, y una factura
anulada de un mes ya declarado no se arregla anulando.

Escribe en la base: va contra el entorno local con la demostración sembrada.

    python3 backend/tests/smoke/fase14_postventa.py
"""
import json, sys, urllib.request, urllib.error

API = "http://localhost:5199"
TOK = None

def call(method, path, body=None, raw=False):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(API + path, data=data, method=method)
    req.add_header('Content-Type', 'application/json')
    if TOK: req.add_header('Authorization', 'Bearer ' + TOK)
    try:
        with urllib.request.urlopen(req) as r:
            contenido = r.read()
            if raw: return r.status, contenido
            return r.status, (json.loads(contenido) if contenido else None)
    except urllib.error.HTTPError as e:
        cuerpo = e.read()
        try: return e.code, json.loads(cuerpo)
        except Exception: return e.code, cuerpo[:200].decode(errors='replace')

fallos = []
def revisar(nombre, condicion, detalle=''):
    print(('  ok  ' if condicion else '  FALLA ') + nombre + (f' — {detalle}' if detalle and not condicion else ''))
    if not condicion: fallos.append(nombre)

TOK = call('POST', '/api/auth/login', {'email': 'dueno@tallerdemo.hn', 'password': 'Garaj123!'})[1]['accessToken']

# ---------- 1. Reabrir ----------
print('\n1. Reabrir una orden entregada')
entregadas = call('GET', '/api/work-orders?onlyOpen=false&pageSize=100')[1]['items']
entregadas = [o for o in entregadas if o['status'] == 8]
revisar('hay órdenes entregadas en la demostración', len(entregadas) > 0)

orden = entregadas[0]
codigo, cuerpo = call('POST', f'/api/work-orders/{orden["id"]}/reopen', {'reason': 'Volvió con el mismo ruido'})
revisar('con factura viva no deja reabrir', codigo == 409, f'respondió {codigo}: {cuerpo}')
revisar('y dice qué factura anular', isinstance(cuerpo, dict) and 'anule' in json.dumps(cuerpo).lower(),
        json.dumps(cuerpo)[:160])

# La factura de esa orden: anularla y volver a intentar.
ventas = call('GET', f'/api/sales?workOrderId={orden["id"]}&pageSize=10')[1]['items']
revisar('la orden entregada tiene su factura', len(ventas) > 0)
venta = ventas[0]

codigo, cuerpo = call('POST', f'/api/sales/{venta["id"]}/void', {'reason': 'Se cobró de más'})
abonado = venta['total'] - venta['balance']
if abonado > 0:
    revisar('con abonos exige decir qué se hizo con el dinero', codigo == 400,
            f'respondió {codigo}: {cuerpo}')
    codigo, cuerpo = call('POST', f'/api/sales/{venta["id"]}/void',
                          {'reason': 'Se cobró de más', 'paymentsNote': 'Se le devolvió en efectivo'})

revisar('la factura se anula dentro del mes', codigo == 200, f'respondió {codigo}: {cuerpo}')

codigo, cuerpo = call('POST', f'/api/work-orders/{orden["id"]}/reopen', {'reason': 'Volvió con el mismo ruido'})
revisar('anulada la factura, la orden se reabre', codigo == 200, f'respondió {codigo}: {cuerpo}')
if codigo == 200:
    revisar('vuelve a «En proceso»', cuerpo['status'] == 5, f'quedó en {cuerpo["status"]}')
    revisar('el motivo queda en la línea de tiempo',
            any('Reabierta' in (e.get('note') or '') for e in cuerpo['timeline']))

codigo, cuerpo = call('POST', f'/api/work-orders/{orden["id"]}/reopen', {'reason': 'otra vez'})
revisar('una orden que no está entregada no se reabre', codigo == 409, f'respondió {codigo}')

codigo, _ = call('POST', f'/api/work-orders/{orden["id"]}/reopen', {'reason': '   '})
revisar('sin motivo no se reabre', codigo == 400, f'respondió {codigo}')

# ---------- 2. Volver a facturar ----------
print('\n2. Volver a facturar la orden reabierta')
codigo, cuerpo = call('POST', '/api/sales/close-work-order', {
    'workOrderId': orden['id'], 'paymentMethod': 1, 'notes': None, 'taxRate': None})
revisar('se puede emitir la factura nueva', codigo in (200, 201), f'respondió {codigo}: {cuerpo}')

# ---------- 3. La ficha de recepción ----------
print('\n3. La ficha de recepción en PDF')

# Para el 404 hace falta una orden sin hoja llenada: la de arriba puede traerla de una
# corrida anterior, y entonces la prueba se estaría mintiendo sola.
sinHoja = next(
    (o for o in entregadas
     if call('GET', f'/api/work-orders/{o["id"]}/reception')[0] == 204),
    None)

if sinHoja:
    codigo, _ = call('GET', f'/api/work-orders/{sinHoja["id"]}/reception/pdf', raw=True)
    revisar('sin recepción llenada responde 404', codigo == 404, f'respondió {codigo}')
else:
    print('      (todas las órdenes de la demostración ya tienen hoja)')

codigo, _ = call('PUT', f'/api/work-orders/{orden["id"]}/reception', {
    'fuelLevel': 3, 'damages': 'Rayón en el tanque, lado derecho',
    'belongings': 'Casco y documentos', 'notes': None,
    'deliveredByName': 'El hijo del cliente', 'mileageIn': 31800, 'signature': None})
revisar('la hoja se guarda', codigo == 200, f'respondió {codigo}')

codigo, bytes_pdf = call('GET', f'/api/work-orders/{orden["id"]}/reception/pdf', raw=True)
revisar('la ficha sale en PDF', codigo == 200 and bytes_pdf[:4] == b'%PDF', f'respondió {codigo}')
revisar('y pesa algo razonable', codigo == 200 and len(bytes_pdf) > 2000, f'{len(bytes_pdf) if codigo==200 else 0} bytes')

# ---------- 4. La cotización con la ficha ----------
print('\n4. La cotización con la ficha cosida delante')
cotizaciones = call('GET', '/api/quotes?pageSize=50')[1]['items']
conOrden = [q for q in cotizaciones if q.get('workOrderId')]
if conOrden:
    cot = conOrden[0]
    _, sola = call('GET', f'/api/quotes/{cot["id"]}/pdf', raw=True)
    _, conFicha = call('GET', f'/api/quotes/{cot["id"]}/pdf?includeReception=true', raw=True)
    revisar('el presupuesto solo es un PDF', sola[:4] == b'%PDF')
    revisar('con la ficha también', conFicha[:4] == b'%PDF')
    # La orden de esa cotización puede no tener ficha: entonces los dos pesan igual, y está bien.
    print(f'      presupuesto {len(sola)} bytes · con ficha {len(conFicha)} bytes')
else:
    print('      (sin cotizaciones ligadas a una orden en la demostración)')

# ---------- 5. Estado de resultados de meses anteriores ----------
print('\n5. El estado de resultados de meses anteriores')
codigo, mesPasado = call('GET', '/api/expenses/income-statement'
                         '?from=2026-09-01T00:00:00Z&to=2026-09-30T23:59:59Z')
revisar('el mes pasado se puede consultar', codigo == 200, f'respondió {codigo}')
if codigo == 200:
    revisar('viene con el periodo que se pidió', mesPasado['from'].startswith('2026-09'),
            mesPasado['from'])
    revisar('y con el periodo anterior para comparar', mesPasado.get('previous') is not None)

print('\n' + ('TODO BIEN' if not fallos else f'FALLARON {len(fallos)}: ' + ', '.join(fallos)))
sys.exit(1 if fallos else 0)
