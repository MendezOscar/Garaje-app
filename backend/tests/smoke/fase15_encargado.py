#!/usr/bin/env python3
"""Humo del Encargado de sucursal: hasta dónde llega y dónde se le cierra la puerta.

Es un perfil de permisos, así que lo que hay que probar no es que funcione sino que **no**
funcione lo que no le toca: las órdenes de otra sucursal, los catálogos del taller, los gastos
y el personal. Y que las sucursales ajenas contesten 404 y no 403, porque un 403 ya confirma
que esa sucursal existe.

Va contra el entorno local con la demostración sembrada, donde el encargado es de la sucursal
del Puerto:

    python3 backend/tests/smoke/fase15_encargado.py
"""
import json, sys, urllib.request, urllib.error

API = "http://localhost:5199"
TOK = None

def call(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(API + path, data=data, method=method)
    req.add_header('Content-Type', 'application/json')
    if TOK: req.add_header('Authorization', 'Bearer ' + TOK)
    try:
        with urllib.request.urlopen(req) as r:
            contenido = r.read()
            return r.status, (json.loads(contenido) if contenido else None)
    except urllib.error.HTTPError as e:
        cuerpo = e.read()
        try: return e.code, json.loads(cuerpo)
        except Exception: return e.code, cuerpo[:200].decode(errors='replace')

fallos = []
def revisar(nombre, condicion, detalle=''):
    print(('  ok  ' if condicion else '  FALLA ') + nombre + (f' — {detalle}' if detalle and not condicion else ''))
    if not condicion: fallos.append(nombre)

def entrar(correo):
    codigo, cuerpo = call('POST', '/api/auth/login', {'email': correo, 'password': 'Garaj123!'})
    if codigo != 200:
        print(f'No se pudo entrar como {correo}: {codigo} {cuerpo}')
        sys.exit(1)
    return cuerpo

# ---------- 0. Quién es quién ----------
print('\n0. El taller, visto por el Dueño')
dueno = entrar('dueno@tallerdemo.hn')
TOK = dueno['accessToken']

sucursales = call('GET', '/api/branches')[1]
revisar('la demostración tiene tres sucursales', len(sucursales) == 3, str(len(sucursales)))

todas = call('GET', '/api/work-orders?onlyOpen=false&pageSize=200')[1]
ordenes_del_taller = todas['total']
revisar('el Dueño ve las órdenes de todo el taller', ordenes_del_taller > 0)

# ---------- 1. Entra el Encargado ----------
print('\n1. El Encargado del Puerto')
encargado = entrar('encargado@tallerdemo.hn')
TOK = encargado['accessToken']

revisar('su perfil es Encargado', encargado['user']['role'] == 'Manager', encargado['user']['role'])
revisar('solo ve su sucursal', len(encargado['user']['branches']) == 1,
        json.dumps(encargado['user']['branches']))

suya = encargado['user']['branches'][0]['id']
ajena = next(b['id'] for b in sucursales if b['id'] != suya)

# ---------- 2. Lo suyo ----------
print('\n2. Lo que sí le toca')
codigo, mias = call('GET', '/api/work-orders?onlyOpen=false&pageSize=200')
revisar('abre la bandeja de órdenes', codigo == 200, str(codigo))
revisar('y solo trae las de su sucursal',
        mias['total'] < ordenes_del_taller and all(o['branchId'] == suya for o in mias['items']),
        f'{mias["total"]} de {ordenes_del_taller}')

codigo, tablero = call('GET', '/api/reports/dashboard')
revisar('ve el resumen del día', codigo == 200, str(codigo))

codigo, caja = call('GET', '/api/reports/cash-close')
revisar('cuadra su caja', codigo == 200, str(codigo))

codigo, ventas = call('GET', '/api/sales?pageSize=50')
revisar('ve las ventas de su sucursal', codigo == 200, str(codigo))
revisar('y ninguna de otra', all(v['branchId'] == suya for v in ventas['items']) if codigo == 200 else False)

codigo, reclamos = call('GET', '/api/claims?onlyOpen=false&pageSize=50')
revisar('ve los reclamos de su sucursal', codigo == 200, str(codigo))

codigo, tecnicos = call('GET', '/api/users?role=Technician')
revisar('ve a los técnicos para asignarles trabajo', codigo == 200, str(codigo))
revisar('y no al Dueño ni a los de otra sucursal',
        all(u['role'] == 'Technician' and suya in u['branchIds'] for u in tecnicos) if codigo == 200 else False,
        json.dumps(tecnicos)[:200] if codigo == 200 else '')
revisar('sin lo que se les paga', all(u['payAmount'] == 0 for u in tecnicos) if codigo == 200 else False)

codigo, _ = call('GET', '/api/stock?pageSize=20')
revisar('ve la bodega', codigo == 200, str(codigo))

# ---------- 3. Lo ajeno: 404, no 403 ----------
print('\n3. La sucursal de al lado no existe para él')
codigo, cuerpo = call('GET', f'/api/reports/dashboard?branchId={ajena}')
revisar('el resumen de otra sucursal responde 404', codigo == 404, f'respondió {codigo}: {cuerpo}')

de_otra = [o for o in todas['items'] if o['branchId'] != suya]
if de_otra:
    codigo, cuerpo = call('GET', f'/api/work-orders/{de_otra[0]["id"]}')
    revisar('una orden de otra sucursal responde 404', codigo == 404, f'respondió {codigo}')

codigo, cuerpo = call('POST', '/api/stock/receive',
                      {'branchId': ajena, 'partId': '00000000-0000-0000-0000-000000000000',
                       'quantity': 1})
revisar('no recibe repuestos en la bodega ajena', codigo in (404, 400), f'respondió {codigo}')

# ---------- 4. Lo del taller entero ----------
print('\n4. Lo que es del Dueño')
for nombre, metodo, ruta, cuerpo in [
    ('los ajustes del taller', 'GET', '/api/tenant', None),
    ('el personal, para darlo de alta', 'POST', '/api/users',
     {'email': 'nadie@tallerdemo.hn', 'fullName': 'Nadie', 'role': 'Technician',
      'password': 'Garaj123!', 'branchIds': []}),
    ('el catálogo de mano de obra', 'POST', '/api/labor-services',
     {'code': 'X', 'name': 'X', 'isFixedPrice': True, 'fixedPrice': 1, 'isActive': True}),
    ('los trabajos frecuentes', 'POST', '/api/job-templates', {'name': 'X'}),
    ('el libro de ventas', 'GET', '/api/reports/sales-book.csv?year=2026&month=1', None),
    ('las sucursales', 'POST', '/api/branches', {'name': 'X', 'code': 'XXX'}),
]:
    codigo, _ = call(metodo, ruta, cuerpo)
    revisar(f'no alcanza {nombre}', codigo == 403, f'respondió {codigo}')

print()
if fallos:
    print(f'{len(fallos)} fallas: ' + ', '.join(fallos))
    sys.exit(1)

print('TODO BIEN')
