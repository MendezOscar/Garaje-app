#!/usr/bin/env python3
"""Arma un video narrado a partir de un guion y unas capturas de pantalla.

    python3 docs/videos/hacer-video.py docs/videos/guion-hoy.json

Deja el video en docs/videos/salida/<nombre>.mp4
"""
import json, os, shutil, subprocess, sys

ANCHO, ALTO = 1080, 1920
PIE = 330          # alto de la franja de texto
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"


def correr(cmd):
    subprocess.run(cmd, check=True, capture_output=True)


def medir(imagen):
    salida = subprocess.run(
        ["ffprobe", "-v", "error", "-select_streams", "v:0",
         "-show_entries", "stream=width,height", "-of", "csv=p=0:s=x", imagen],
        check=True, capture_output=True, text=True).stdout.strip()
    ancho, alto = salida.split("x")
    return int(ancho), int(alto)


def duracion(audio):
    return float(subprocess.run(
        ["ffprobe", "-v", "error", "-show_entries", "format=duration",
         "-of", "csv=p=0", audio], check=True, capture_output=True,
        text=True).stdout.strip())


def escapar(texto):
    return (texto.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;"))


def hoja(guion, aqui, carpeta):
    """Una sola pasada de Chrome: todas las escenas apiladas en un PNG."""
    alto_foto = ALTO - PIE - 60
    escenas, copiadas = [], set()

    for e in guion["escenas"]:
        nombre = e.get("imagen", guion["imagen"])
        ruta = os.path.join(aqui, nombre)
        if nombre not in copiadas:
            shutil.copy(ruta, os.path.join(carpeta, nombre))
            copiadas.add(nombre)
        ancho_base, alto_base = medir(ruta)
        escala = alto_foto / alto_base
        ancho_foto = ancho_base * escala

        if e["foco"]:
            x, y, w, h = [v * escala for v in e["foco"]]
            marco = (f'<div class="marco" style="left:{x:.1f}px;top:{y:.1f}px;'
                     f'width:{w:.1f}px;height:{h:.1f}px"></div>')
        else:
            marco = '<div class="velo"></div>'
        escenas.append(f"""<section class="escena">
  <div class="foto" style="width:{ancho_foto:.1f}px;height:{alto_foto:.1f}px">
    <img src="{nombre}">{marco}
  </div>
  <div class="pie"><h2>{escapar(e['titulo'])}</h2><p>{escapar(e['texto'])}</p></div>
</section>""")

    html = f"""<!doctype html><meta charset="utf-8"><style>
  * {{ margin:0; padding:0; box-sizing:border-box }}
  body {{ background:#0b0f14; font-family:-apple-system,"Helvetica Neue",sans-serif }}
  .escena {{ width:{ANCHO}px; height:{ALTO}px; position:relative;
            background:radial-gradient(120% 70% at 50% 0%,#16202c 0%,#0b0f14 70%);
            display:flex; flex-direction:column; align-items:center }}
  .foto {{ position:relative; margin-top:40px; border-radius:26px; overflow:hidden;
          box-shadow:0 24px 60px rgba(0,0,0,.6) }}
  .foto img {{ width:100%; height:100%; display:block }}
  .velo {{ position:absolute; inset:0; background:rgba(0,0,0,.25) }}
  .marco {{ position:absolute; border:5px solid #4da3ff; border-radius:16px;
           box-shadow:0 0 0 9999px rgba(3,7,12,.68), 0 0 26px rgba(77,163,255,.9) }}
  .pie {{ width:100%; height:{PIE}px; padding:28px 70px 0; text-align:center }}
  .pie h2 {{ color:#fff; font-size:62px; font-weight:700; letter-spacing:-1px }}
  .pie p {{ color:#8fb6d9; font-size:40px; font-weight:500; margin-top:14px }}
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
    taller = os.path.join(salida, "piezas")
    os.makedirs(taller, exist_ok=True)

    png = hoja(guion, aqui, taller)
    clips = []
    for i, e in enumerate(guion["escenas"]):
        pedazo = os.path.join(taller, f"{i:02d}.png")
        correr(["ffmpeg", "-y", "-i", png, "-vf",
                f"crop={ANCHO}:{ALTO}:0:{i * ALTO}", pedazo])

        aiff = os.path.join(taller, f"{i:02d}.aiff")
        correr(["say", "-v", guion.get("voz", "Paulina"),
                "-r", str(guion.get("velocidad", 165)),
                "-o", aiff, e["narracion"]])
        largo = duracion(aiff) + 0.6
        cuadros = int(largo * 30)

        clip = os.path.join(taller, f"{i:02d}.mp4")
        correr(["ffmpeg", "-y", "-loop", "1", "-i", pedazo, "-i", aiff,
                "-filter_complex",
                f"[0:v]zoompan=z='min(1.06,1+0.06*on/{cuadros})':"
                f"x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':"
                f"d={cuadros}:s={ANCHO}x{ALTO}:fps=30,format=yuv420p[v];"
                f"[1:a]adelay=400|400,apad[a]",
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
