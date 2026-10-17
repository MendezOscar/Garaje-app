#!/usr/bin/env python3
"""Humo de las citas: el enlace público, la agenda del taller y lo que cada perfil alcanza.

Lo delicado aquí no es agendar sino el enlace sin sesión: tiene que dejar pedir una cita y
nada más. Ni ver clientes, ni órdenes, ni el taller de al lado. Y una cita pedida por el
enlace no debe crear cliente ni vehículo: el padrón se llena cuando el carro llega.

Va contra el entorno local con la demostración sembrada:

    python3 backend/tests/smoke/fase16_citas.py
"""
import json, random, sys, urllib.request, urllib.error
from datetime import date, timedelta
import os

# La contraseña con la que nace el taller sembrado. Por omisión la de la base local;
# con GARAJ_CLAVE se corre contra un ambiente que tenga otra.
CLAVE = os.environ.get("GARAJ_CLAVE", "Garaj123!")

API = "http://localhost:5199"
TOK = None

def call(method, path, body=None, auth=True):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(API + path, data=data, method=method)
    req.add_header('Content-Type', 'application/json')
    if TOK and auth: req.add_header('Authorization', 'Bearer ' + TOK)
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
    codigo, cuerpo = call('POST', '/api/auth/login', {'email': correo, 'password': CLAVE}, auth=False)
    if codigo != 200:
        print(f'No se pudo entrar como {correo}: {codigo} {cuerpo}')
        sys.exit(1)
    return cuerpo

manana = (date.today() + timedelta(days=1)).isoformat()
pasado = (date.today() + timedelta(days=2)).isoformat()
ayer = (date.today() - timedelta(days=1)).isoformat()

# ---------- 0. El taller ----------
print('\n0. El enlace del taller')
dueno = entrar('dueno@tallerdemo.hn')
TOK = dueno['accessToken']

ficha = call('GET', '/api/tenant')[1]
enlace = ficha.get('bookingToken')
revisar('el taller trae su token de citas', bool(enlace), json.dumps(ficha)[:120])
revisar('nace recibiendo citas', ficha.get('bookingEnabled') is True)

sucursales = call('GET', '/api/branches')[1]
centro = sucursales[0]['id']

# ---------- 1. La hoja pública ----------
print('\n1. La hoja que abre quien no tiene cuenta')
TOK_GUARDADO, TOK = TOK, None

codigo, pagina = call('GET', f'/public/booking/{enlace}', auth=False)
revisar('la hoja abre sin sesión', codigo == 200, f'respondió {codigo}')
revisar('dice de qué taller es', pagina and pagina['tenantName'], json.dumps(pagina)[:120])
revisar('ofrece las sucursales', pagina and len(pagina['branches']) == 3,
        str(len(pagina['branches']) if pagina else 0))
revisar('no filtra nada más del taller',
        pagina and set(pagina.keys()) == {'tenantName', 'hasLogo', 'enabled', 'branches', 'lastDay'},
        json.dumps(list(pagina.keys()) if pagina else []))

codigo, _ = call('GET', '/public/booking/00000000-0000-0000-0000-000000000000', auth=False)
revisar('un enlace inventado responde 404', codigo == 404, f'respondió {codigo}')

# ---------- 2. Pedir la cita ----------
print('\n2. Pedirla por el enlace')
# Un número distinto en cada corrida: el tope por teléfono cuenta las citas abiertas que
# dejó la anterior, y con un número fijo la segunda corrida chocaba contra su propio rastro.
telefono = '5049' + ''.join(random.choice('0123456789') for _ in range(7))

cita = {
    'branchId': centro, 'contactName': 'Humo de Citas', 'contactPhone': telefono,
    'vehicleNote': 'Corolla 2014 gris', 'reason': 'Ruido al frenar',
    'day': manana, 'slot': 1,
}

codigo, pedida = call('POST', f'/public/booking/{enlace}', cita, auth=False)
revisar('se puede pedir sin cuenta', codigo == 200, f'respondió {codigo} {pedida}')
revisar('queda por confirmar', pedida and pedida['status'] == 1, json.dumps(pedida)[:160])
token_cliente = pedida['token'] if pedida else None

codigo, _ = call('POST', f'/public/booking/{enlace}', {**cita, 'day': ayer}, auth=False)
revisar('un día que ya pasó se rechaza', codigo == 400, f'respondió {codigo}')

codigo, _ = call('POST', f'/public/booking/{enlace}', {**cita, 'contactPhone': '123'}, auth=False)
revisar('sin teléfono de verdad se rechaza', codigo == 400, f'respondió {codigo}')

codigo, _ = call('POST', f'/public/booking/{enlace}', {**cita, 'reason': '  '}, auth=False)
revisar('sin decir qué necesita se rechaza', codigo == 400, f'respondió {codigo}')

# El tope por teléfono, con un número suyo: la primera, la segunda y la tercera pasan; la
# cuarta no.
otro = {**cita, 'contactPhone': '5049' + ''.join(random.choice('0123456789') for _ in range(7))}
for _ in range(3):
    call('POST', f'/public/booking/{enlace}', {**otro, 'day': pasado}, auth=False)

codigo, _ = call('POST', f'/public/booking/{enlace}', {**otro, 'day': pasado}, auth=False)
revisar('un mismo número no llena la agenda', codigo == 409, f'respondió {codigo}')

# ---------- 3. La página del cliente ----------
print('\n3. La página de su cita')
codigo, suya = call('GET', f'/public/booking/appointments/{token_cliente}', auth=False)
revisar('la abre con su token', codigo == 200, f'respondió {codigo}')
revisar('no expone ids del taller',
        suya and 'branchId' not in suya and 'customerId' not in suya,
        json.dumps(list(suya.keys()) if suya else []))

# ---------- 4. La agenda del taller ----------
print('\n4. La agenda')
TOK = TOK_GUARDADO

agenda = call('GET', '/api/appointments?pageSize=100')[1]
mia = next((c for c in agenda['items'] if c['contactName'] == 'Humo de Citas'), None)
revisar('la cita del enlace aparece en la agenda', mia is not None)
revisar('sin cliente ni vehículo del padrón',
        mia and mia['customerId'] is None and mia['vehicleId'] is None,
        json.dumps(mia)[:160] if mia else '')
revisar('se ve lo que la persona dijo del vehículo',
        mia and mia['vehicleLabel'] == 'Corolla 2014 gris',
        mia['vehicleLabel'] if mia else '')

clientes = call('GET', '/api/customers?search=Humo')[1]
revisar('pedir cita no creó un cliente', clientes['total'] == 0, str(clientes['total']))

codigo, confirmada = call('POST', f'/api/appointments/{mia["id"]}/confirm',
                          {'day': pasado, 'slot': 2, 'note': 'Lo esperamos a la 1'})
revisar('el taller la confirma y le mueve el día', codigo == 200 and confirmada['status'] == 2,
        f'respondió {codigo}')
revisar('el día quedó movido', confirmada and confirmada['day'] == pasado,
        confirmada['day'] if confirmada else '')

codigo, vista = call('GET', f'/public/booking/appointments/{token_cliente}', auth=False)
revisar('el cliente ve que se la confirmaron', vista and vista['status'] == 2)
revisar('y lee la nota del taller', vista and vista['responseNote'] == 'Lo esperamos a la 1')

codigo, atendida = call('POST', f'/api/appointments/{mia["id"]}/attended', {})
revisar('se cierra cuando llega el vehículo', codigo == 200 and atendida['status'] == 3,
        f'respondió {codigo}')

# ---------- 5. Cada quien lo suyo ----------
print('\n5. Lo que alcanza cada perfil')
encargado = entrar('encargado@tallerdemo.hn')
TOK = encargado['accessToken']
suya_sucursal = encargado['user']['branches'][0]['id']

suyas = call('GET', '/api/appointments?pageSize=100&includeClosed=true')[1]
revisar('el Encargado solo ve las de su sucursal',
        all(c['branchId'] == suya_sucursal for c in suyas['items']),
        json.dumps([c['branchName'] for c in suyas['items']]))

codigo, _ = call('POST', '/api/appointments',
                 {'branchId': centro if centro != suya_sucursal else sucursales[1]['id'],
                  'contactName': 'Nadie', 'contactPhone': '50499000222',
                  'reason': 'X', 'day': manana, 'slot': 1})
revisar('no anota citas en una sucursal ajena', codigo == 404, f'respondió {codigo}')

tecnico = entrar('tecnico1@tallerdemo.hn')
TOK = tecnico['accessToken']
codigo, delTecnico = call('GET', '/api/appointments')
revisar('el Técnico no ve la agenda', codigo == 200 and delTecnico['total'] == 0,
        f'respondió {codigo}')

cliente = entrar('cliente1@tallerdemo.hn')
TOK = cliente['accessToken']
codigo, delCliente = call('GET', '/api/appointments')
revisar('el Cliente solo ve las suyas', codigo == 200, f'respondió {codigo}')
revisar('y ninguna del enlace público',
        all(c['customerId'] is not None for c in (delCliente['items'] if delCliente else [])))

vehiculos = call('GET', '/api/vehicles?pageSize=1')[1]['items']
if vehiculos:
    codigo, pedida = call('POST', '/api/appointments/request',
                          {'branchId': centro, 'vehicleId': vehiculos[0]['id'],
                           'reason': 'Servicio', 'day': manana, 'slot': 1})
    revisar('el Cliente pide cita sobre su vehículo', codigo == 201, f'respondió {codigo}')
    revisar('nace por confirmar y con su vehículo',
            pedida and pedida['status'] == 1 and pedida['vehicleId'] == vehiculos[0]['id'])

codigo, _ = call('POST', f'/api/appointments/{mia["id"]}/confirm', {})
revisar('el Cliente no confirma citas', codigo == 403, f'respondió {codigo}')

# ---------- 6. El aviso ----------
print('\n6. A quién le suena')
# Una cita en la sucursal del Encargado: tiene que sonarle a él y al Dueño, y a nadie más.
suya_cita = {**cita, 'branchId': suya_sucursal,
             'contactPhone': '5049' + ''.join(random.choice('0123456789') for _ in range(7)),
             'contactName': 'Humo del Aviso'}

codigo, pedida = call('POST', f'/public/booking/{enlace}', suya_cita, auth=False)
revisar('se pide una cita en la sucursal del Encargado', codigo == 200, f'respondió {codigo}')

def avisos_de(correo):
    global TOK
    TOK = entrar(correo)['accessToken']
    return call('GET', '/api/notifications?pageSize=50')[1]['items']

def aviso_de_la_cita(avisos):
    # Tipo 6 es AppointmentRequested. Se busca por el nombre para no confundirlo con las
    # citas que dejó el sembrador.
    return next((a for a in avisos if a['type'] == 6 and 'Humo del Aviso' in a['body']), None)

del_dueno = aviso_de_la_cita(avisos_de('dueno@tallerdemo.hn'))
revisar('le suena al Dueño', del_dueno is not None)
revisar('y el aviso lleva la cita, para abrir la agenda de un toque',
        del_dueno and del_dueno['appointmentId'], json.dumps(del_dueno))

revisar('le suena al Encargado de esa sucursal',
        aviso_de_la_cita(avisos_de('encargado@tallerdemo.hn')) is not None)

TOK = entrar('tecnico1@tallerdemo.hn')['accessToken']
revisar('al Técnico no le suena',
        aviso_de_la_cita(call('GET', '/api/notifications?pageSize=50')[1]['items']) is None)

print()
if fallos:
    print(f'{len(fallos)} fallas: ' + ', '.join(fallos))
    sys.exit(1)

print('TODO BIEN')
