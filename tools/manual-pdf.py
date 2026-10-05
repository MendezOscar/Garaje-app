#!/usr/bin/env python3
"""Arma el manual en un solo PDF, para imprimirlo o mandarlo por correo.

    tools/manual-pdf.py [salida.pdf]

Convierte los capítulos de docs/manual a una sola página y la imprime con
Chrome, que es lo único que hace falta tener instalado: nada de pandoc ni de
LaTeX para un documento que son títulos, tablas y capturas.
"""
import html
import re
import subprocess
import sys
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
MANUAL = RAIZ / 'docs' / 'manual'
CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'


def en_linea(t: str) -> str:
    """Negritas, cursivas, código, enlaces e imágenes dentro de un párrafo."""
    trozos = re.split(r'(`[^`]+`)', t)
    salida = []
    for i, trozo in enumerate(trozos):
        if i % 2:
            salida.append('<code>' + html.escape(trozo[1:-1]) + '</code>')
            continue
        x = html.escape(trozo)
        x = re.sub(r'!\[([^\]]*)\]\(([^)]+)\)',
                   lambda m: f'<img src="{(MANUAL / m.group(2)).as_uri()}" alt="{m.group(1)}">', x)
        # Un enlace a otro capítulo no sirve en papel: queda el texto solo.
        x = re.sub(r'\[([^\]]+)\]\((?:https?://[^)]+)\)', r'<a>\1</a>', x)
        x = re.sub(r'\[([^\]]+)\]\([^)]+\)', r'\1', x)
        x = re.sub(r'\*\*([^*]+)\*\*', r'<strong>\1</strong>', x)
        x = re.sub(r'(?<![\w*])\*([^*]+)\*(?![\w*])', r'<em>\1</em>', x)
        salida.append(x)
    return ''.join(salida)


def convertir(md: str) -> str:
    fuera, lineas, i = [], md.split('\n'), 0
    while i < len(lineas):
        linea = lineas[i]

        if linea.startswith('```'):
            cuerpo = []
            i += 1
            while i < len(lineas) and not lineas[i].startswith('```'):
                cuerpo.append(html.escape(lineas[i]))
                i += 1
            fuera.append('<pre>' + '\n'.join(cuerpo) + '</pre>')

        elif linea.startswith('|'):
            tabla = []
            while i < len(lineas) and lineas[i].startswith('|'):
                tabla.append(lineas[i])
                i += 1
            i -= 1
            celdas = [[c.strip() for c in f.strip().strip('|').split('|')] for f in tabla]
            cuerpo = [f for f in celdas if not all(set(c) <= set('-: ') for c in f)]
            if cuerpo:
                encabezado = ''.join(f'<th>{en_linea(c)}</th>' for c in cuerpo[0])
                filas = ''.join(
                    '<tr>' + ''.join(f'<td>{en_linea(c)}</td>' for c in f) + '</tr>'
                    for f in cuerpo[1:])
                fuera.append(f'<table><thead><tr>{encabezado}</tr></thead><tbody>{filas}</tbody></table>')

        elif linea.startswith('> '):
            cita = []
            while i < len(lineas) and lineas[i].startswith('>'):
                cita.append(lineas[i].lstrip('>').strip())
                i += 1
            i -= 1
            fuera.append('<blockquote>' + en_linea(' '.join(cita)) + '</blockquote>')

        elif re.match(r'^(\d+\.|-) ', linea):
            ordenada = bool(re.match(r'^\d+\.', linea))
            puntos = []
            while i < len(lineas) and (re.match(r'^(\d+\.|-) ', lineas[i]) or
                                       (lineas[i].startswith('  ') and lineas[i].strip() and puntos)):
                if re.match(r'^(\d+\.|-) ', lineas[i]):
                    puntos.append(re.sub(r'^(\d+\.|-) ', '', lineas[i]))
                else:
                    puntos[-1] += ' ' + lineas[i].strip()
                i += 1
            i -= 1
            if ordenada:
                # El índice numera del 1 al 23 de corrido aunque vaya en tres bloques.
                desde = int(re.match(r'^(\d+)\.', linea).group(1))
                apertura = f'<ol start="{desde}">'
            else:
                apertura = '<ul>'
            etiqueta = 'ol' if ordenada else 'ul'
            fuera.append(apertura + ''.join(f'<li>{en_linea(p)}</li>' for p in puntos) + f'</{etiqueta}>')

        elif linea.startswith('#'):
            nivel = len(linea) - len(linea.lstrip('#'))
            fuera.append(f'<h{nivel}>{en_linea(linea[nivel:].strip())}</h{nivel}>')

        elif linea.strip():
            parrafo = []
            while i < len(lineas) and lineas[i].strip() and not re.match(r'^(#|>|\||```|- |\d+\. )', lineas[i]):
                parrafo.append(lineas[i].strip())
                i += 1
            i -= 1
            texto = en_linea(' '.join(parrafo))
            fuera.append(texto if texto.startswith('<img') else f'<p>{texto}</p>')

        i += 1
    return '\n'.join(fuera)


ESTILO = """
@page { size: letter; margin: 18mm 16mm; }
body { font: 11pt/1.5 -apple-system, "Helvetica Neue", Arial, sans-serif; color: #15181d; }
h1 { font-size: 20pt; margin: 0 0 .6em; }
h2 { font-size: 14pt; margin: 1.4em 0 .4em; }
h3 { font-size: 12pt; margin: 1.2em 0 .3em; }
p, li { orphans: 3; widows: 3; }
img { max-width: 62mm; border: 1px solid #d7dbe2; border-radius: 6px; display: block; margin: .8em 0; }
table { border-collapse: collapse; width: 100%; margin: .8em 0; font-size: 10pt; }
th, td { border: 1px solid #d7dbe2; padding: 5px 8px; text-align: left; vertical-align: top; }
th { background: #f2f4f7; }
blockquote { margin: .8em 0; padding: .5em .9em; border-left: 3px solid #f2a31a; background: #fffaf0; }
pre { background: #f2f4f7; padding: .7em .9em; border-radius: 6px; font-size: 9.5pt; overflow-wrap: break-word; }
code { background: #f2f4f7; padding: 1px 4px; border-radius: 3px; font-size: 9.5pt; }
.capitulo { break-before: page; }
a { color: inherit; text-decoration: none; }
"""


def main() -> int:
    if not Path(CHROME).exists():
        print('Hace falta Google Chrome para imprimir el PDF.', file=sys.stderr)
        return 1

    archivos = [MANUAL / 'README.md'] + sorted(MANUAL.glob('[0-9][0-9]-*.md'))
    partes = []
    for n, archivo in enumerate(archivos):
        clase = ' class="capitulo"' if n else ''
        partes.append(f'<section{clase}>' + convertir(archivo.read_text()) + '</section>')

    pagina = (f'<!doctype html><html lang="es"><head><meta charset="utf-8">'
              f'<title>Manual de GarajApp</title><style>{ESTILO}</style></head>'
              f'<body>{"".join(partes)}</body></html>')

    fuente = MANUAL / '.manual.html'
    fuente.write_text(pagina)
    salida = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else MANUAL / 'GarajApp-manual.pdf'
    try:
        subprocess.run([CHROME, '--headless', '--disable-gpu', '--no-pdf-header-footer',
                        f'--print-to-pdf={salida}', fuente.as_uri()],
                       check=True, capture_output=True, timeout=180)
    finally:
        fuente.unlink(missing_ok=True)
    print(f'{salida} ({salida.stat().st_size // 1024} KB)')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
