#!/usr/bin/env python3
"""Arma el anuncio de GarajApp con capturas de la app, al estilo reel.

    python3 docs/videos/hacer-anuncio-capturas.py docs/videos/anuncio-capturas.json

Cada escena es una captura grande sobre fondo oscuro, una etiqueta de módulo y
una frase enorme abajo; lo que va entre *asteriscos* sale en celeste.

La escena dura sus "segundos", salvo que traiga "voz" con un audio grabado: ahí
dura lo que dure el audio más medio segundo. "narracion" es solo el texto para
leer al grabar, el programa no lo usa. Si el guion trae "musica", se mezcla por
debajo de la voz.
Deja el video en docs/videos/salida/<nombre>.mp4
"""
import json, os, re, shutil, subprocess, sys

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
    texto = texto.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    return re.sub(r"\*(.+?)\*", r"<em>\1</em>", texto)


def hoja(guion, aqui, carpeta):
    shutil.copy(os.path.join(aqui, "..", "..", "web", "public", "brand", MARCA),
                os.path.join(carpeta, MARCA))

    escenas = []
    for e in guion["escenas"]:
        if e.get("tipo") == "cierre":
            cuerpo = f"""<div class="centro">
  <img class="marca" src="{MARCA}" alt="GarajApp">
  <h1>{escapar(e["titulo"])}</h1>
  <div class="datos">{escapar(e.get("texto", "")).replace(chr(10), "<br>")}</div>
</div>"""
        else:
            captura = os.path.basename(e["captura"])
            shutil.copy(os.path.join(aqui, e["captura"]),
                        os.path.join(carpeta, captura))
            cuerpo = f"""<img class="captura" src="{captura}" alt="">
<div class="velo"></div>
<div class="texto">
  <span class="chip">{escapar(e["modulo"])}</span>
  <h1>{escapar(e["titulo"])}</h1>
</div>
<div class="pie"><img src="{MARCA}" alt="GarajApp"></div>"""
        escenas.append(f'<section class="escena {e.get("tipo", "")}">{cuerpo}</section>')

    html = f"""<!doctype html><meta charset="utf-8"><style>
  * {{ margin:0; padding:0; box-sizing:border-box }}
  body {{ font-family:-apple-system,"Helvetica Neue",sans-serif }}
  .escena {{ width:{ANCHO}px; height:{ALTO}px; position:relative; overflow:hidden;
            color:#fff; background:radial-gradient(85% 45% at 50% 6%,
            #16437f 0%,#0a1c36 45%,#05080f 100%) }}
  .captura {{ position:absolute; left:50%; top:120px; transform:translateX(-50%);
             width:660px; border-radius:44px; border:3px solid rgba(255,255,255,.14);
             box-shadow:0 40px 90px rgba(0,0,0,.65) }}
  .velo {{ position:absolute; left:0; right:0; bottom:0; height:940px;
          background:linear-gradient(rgba(5,8,15,0) 0%,rgba(5,8,15,.88) 40%,#05080f 70%) }}
  .texto {{ position:absolute; left:80px; right:80px; bottom:180px }}
  .chip {{ display:inline-flex; align-items:center; margin-bottom:28px;
          padding:12px 28px; border-radius:999px; background:rgba(62,200,255,.16);
          border:2px solid rgba(62,200,255,.45); color:#cdeeff; font-size:30px;
          font-weight:700; letter-spacing:1.5px; text-transform:uppercase }}
  h1 {{ font-size:88px; font-weight:800; line-height:1.04; letter-spacing:-2.5px;
       text-shadow:0 8px 28px rgba(0,0,0,.6) }}
  em {{ font-style:normal; color:#3ec8ff }}
  .pie {{ position:absolute; left:80px; bottom:80px; opacity:.85 }}
  .pie img {{ height:44px }}
  .centro {{ position:absolute; inset:0; display:flex; flex-direction:column;
            align-items:center; justify-content:center; gap:46px; padding:0 90px;
            text-align:center }}
  .centro .marca {{ height:120px }}
  .centro h1 {{ font-size:104px }}
  .datos {{ font-size:44px; font-weight:600; color:#bcd8ff; line-height:1.5 }}
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
    con_voz = any(e.get("voz") for e in guion["escenas"])
    clips = []
    for i, e in enumerate(guion["escenas"]):
        pedazo = os.path.join(taller, f"{i:02d}.png")
        correr(["ffmpeg", "-y", "-i", png, "-vf",
                f"crop={ANCHO}:{ALTO}:0:{i * ALTO}", pedazo])

        voz = os.path.join(aqui, e["voz"]) if e.get("voz") else None
        if voz and os.path.exists(voz):
            largo = duracion(voz) + 0.8
        else:
            voz = None
            largo = float(e.get("segundos", guion.get("segundos", 3.4)))
        cuadros = int(largo * 30)
        movimiento = (f"[0:v]zoompan=z='min(1.07,1+0.07*on/{cuadros})':"
                      f"x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':"
                      f"d={cuadros}:s={ANCHO}x{ALTO}:fps=30,format=yuv420p[v]")

        clip = os.path.join(taller, f"{i:02d}.mp4")
        orden = ["ffmpeg", "-y", "-loop", "1", "-i", pedazo]
        if not con_voz:
            orden += ["-filter_complex", movimiento, "-map", "[v]", "-an"]
        else:
            # Todas las escenas llevan pista de audio aunque no tengan voz; si a
            # una le faltara, el concat de después se queda sin sonido.
            if voz:
                orden += ["-i", voz, "-filter_complex",
                          movimiento + ";[1:a]adelay=300|300,apad[a]"]
            else:
                orden += ["-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo",
                          "-filter_complex", movimiento + ";[1:a]anull[a]"]
            orden += ["-map", "[v]", "-map", "[a]",
                      "-c:a", "aac", "-b:a", "160k", "-ar", "44100"]
        correr(orden + ["-t", f"{largo}", "-c:v", "libx264",
                        "-preset", "medium", "-crf", "20", clip])
        clips.append(clip)

    lista = os.path.join(taller, "lista.txt")
    with open(lista, "w") as f:
        f.write("".join(f"file '{c}'\n" for c in clips))
    mudo = os.path.join(taller, "mudo.mp4")
    correr(["ffmpeg", "-y", "-f", "concat", "-safe", "0", "-i", lista,
            "-c", "copy", mudo])

    completo = os.path.join(salida, f"{guion['nombre']}.mp4")
    musica = os.path.join(aqui, guion["musica"]) if guion.get("musica") else None
    if musica and os.path.exists(musica):
        # La música baja de volumen si hay voz encima, y se desvanece al final.
        largo = duracion(mudo)
        volumen = 0.18 if con_voz else 0.5
        mezcla = (f"[1:a]volume={volumen},afade=t=out:st={largo - 1.5:.1f}:d=1.5[m]")
        if con_voz:
            mezcla += ";[0:a][m]amix=inputs=2:duration=first:dropout_transition=0[a]"
        else:
            mezcla += ";[m]anull[a]"
        correr(["ffmpeg", "-y", "-i", mudo, "-i", musica,
                "-filter_complex", mezcla, "-map", "0:v", "-map", "[a]",
                "-shortest", "-c:v", "copy", "-c:a", "aac", "-b:a", "160k",
                completo])
    else:
        shutil.copy(mudo, completo)
    print(completo)


if __name__ == "__main__":
    main(sys.argv[1])
