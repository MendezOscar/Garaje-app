#!/usr/bin/env python3
"""Arma el anuncio corto de un taller que va a arrancar con la app.

    python3 docs/videos/hacer-anuncio.py docs/videos/anuncio-taller.json

El logo del taller es opcional: ponga el archivo en docs/videos/ y nómbrelo en
el guion con "logo". Si no hay logo, sale el nombre del taller en letra grande.
Deja el video en docs/videos/salida/<nombre>.mp4
"""
import json, os, shutil, subprocess, sys

ANCHO, ALTO = 1080, 1920
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
MARCA = "garajapp-horizontal-blanco.svg"


def correr(cmd):
    subprocess.run(cmd, check=True, capture_output=True)


def duracion(audio):
    return float(subprocess.run(
        ["ffprobe", "-v", "error", "-show_entries", "format=duration",
         "-of", "csv=p=0", audio], check=True, capture_output=True,
        text=True).stdout.strip())


def escapar(texto):
    return texto.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def hoja(guion, aqui, carpeta):
    logo = guion.get("logo")
    if logo and os.path.exists(os.path.join(aqui, logo)):
        shutil.copy(os.path.join(aqui, logo), os.path.join(carpeta, logo))
        emblema = f'<img class="logo" src="{logo}" alt="">'
    else:
        emblema = f'<div class="nombre">{escapar(guion["taller"])}</div>'
    shutil.copy(os.path.join(aqui, "..", "..", "web", "public", "brand", MARCA),
                os.path.join(carpeta, MARCA))

    escenas = []
    for e in guion["escenas"]:
        cuerpo = f'<h1>{escapar(e["titulo"])}</h1>'
        if e.get("texto"):
            cuerpo += f'<p>{escapar(e["texto"])}</p>'
        if e.get("tipo") == "cierre":
            cuerpo += ('<div class="datos">'
                       + escapar(guion.get("telefono", "")) + "<br>"
                       + escapar(guion.get("direccion", "")) + "</div>")
        escenas.append(f"""<section class="escena {e.get('tipo', '')}">
  <div class="emblema">{emblema}</div>
  <div class="centro">{cuerpo}</div>
  <div class="pie"><span>con</span><img src="{MARCA}" alt="GarajApp"></div>
</section>""")

    html = f"""<!doctype html><meta charset="utf-8"><style>
  * {{ margin:0; padding:0; box-sizing:border-box }}
  body {{ font-family:-apple-system,"Helvetica Neue",sans-serif }}
  .escena {{ width:{ANCHO}px; height:{ALTO}px; position:relative; padding:90px 80px;
            display:flex; flex-direction:column; align-items:center; text-align:center;
            background:radial-gradient(90% 55% at 50% 0%,#1f6feb 0%,#0f2a52 45%,#07101e 100%);
            color:#fff }}
  .emblema {{ height:330px; display:flex; align-items:center; justify-content:center }}
  .logo {{ max-width:620px; max-height:330px; object-fit:contain }}
  .nombre {{ font-size:70px; font-weight:800; letter-spacing:-1px }}
  .centro {{ flex:1; display:flex; flex-direction:column; align-items:center;
            justify-content:center; gap:30px }}
  .centro h1 {{ font-size:96px; font-weight:800; line-height:1.05; letter-spacing:-2px }}
  .centro p {{ font-size:46px; font-weight:500; color:#bcd8ff; line-height:1.35 }}
  .portada .centro h1 {{ font-size:120px; color:#ffd666 }}
  .datos {{ font-size:42px; font-weight:600; color:#fff; line-height:1.5; margin-top:10px }}
  .pie {{ display:flex; align-items:center; gap:18px; opacity:.85 }}
  .pie span {{ font-size:30px; color:#bcd8ff }}
  .pie img {{ height:46px }}
</style>{''.join(escenas)}"""

    pagina = os.path.join(carpeta, "hoja.html")
    with open(pagina, "w") as f:
        f.write(html)
    png = os.path.join(carpeta, "hoja.png")
    correr([CHROME, "--headless", "--disable-gpu", "--hide-scrollbars",
            "--allow-file-access-from-files", "--force-device-scale-factor=1",
            f"--window-size={ANCHO},{ALTO * len(guion['escenas'])}",
            f"--screenshot={png}", f"file://{pagina}"])
    return png


def main(ruta_guion):
    guion = json.load(open(ruta_guion))
    aqui = os.path.dirname(os.path.abspath(ruta_guion))
    salida = os.path.join(aqui, "salida")
    taller = os.path.join(salida, f"piezas-{guion['nombre']}")
    os.makedirs(taller, exist_ok=True)

    png = hoja(guion, aqui, taller)
    clips = []
    for i, e in enumerate(guion["escenas"]):
        pedazo = os.path.join(taller, f"{i:02d}.png")
        correr(["ffmpeg", "-y", "-i", png, "-vf",
                f"crop={ANCHO}:{ALTO}:0:{i * ALTO}", pedazo])

        aiff = os.path.join(taller, f"{i:02d}.aiff")
        correr(["say", "-v", guion.get("voz", "Paulina"),
                "-r", str(guion.get("velocidad", 172)),
                "-o", aiff, e["narracion"]])
        largo = duracion(aiff) + 0.7
        cuadros = int(largo * 30)

        clip = os.path.join(taller, f"{i:02d}.mp4")
        correr(["ffmpeg", "-y", "-loop", "1", "-i", pedazo, "-i", aiff,
                "-filter_complex",
                f"[0:v]zoompan=z='min(1.07,1+0.07*on/{cuadros})':"
                f"x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':"
                f"d={cuadros}:s={ANCHO}x{ALTO}:fps=30,format=yuv420p[v];"
                f"[1:a]adelay=350|350,apad[a]",
                "-map", "[v]", "-map", "[a]", "-t", f"{largo}",
                "-c:v", "libx264", "-preset", "medium", "-crf", "20",
                "-c:a", "aac", "-b:a", "128k", "-ar", "44100", clip])
        clips.append(clip)

    lista = os.path.join(taller, "lista.txt")
    with open(lista, "w") as f:
        f.write("".join(f"file '{c}'\n" for c in clips))
    completo = os.path.join(salida, f"{guion['nombre']}.mp4")
    correr(["ffmpeg", "-y", "-f", "concat", "-safe", "0", "-i", lista,
            "-c", "copy", completo])
    print(completo)


if __name__ == "__main__":
    main(sys.argv[1])
