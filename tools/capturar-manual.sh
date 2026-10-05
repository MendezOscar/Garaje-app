#!/bin/bash
# Saca las capturas del manual. Corre la prueba de integración, que deja cada pantalla quieta
# y anuncia «CAPTURA:<nombre>»; aquí se fotografía el simulador en ese momento.
#
#   tools/capturar-manual.sh [udid-del-simulador]
#
# Antes hace falta: docker compose --profile local-db up -d postgres, la API local en el 5199
# con la demostración sembrada, y el simulador arrancado.
set -u
UDID="${1:-BEE5498C-B907-4F87-A554-5230934A36F3}"
RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
DESTINO="$RAIZ/docs/manual/img"
LOG="${TMPDIR:-/tmp}/capturas-manual.log"
mkdir -p "$DESTINO"

( cd "$RAIZ/mobile" && flutter test integration_test/capturas_manual_test.dart \
    -d "$UDID" --dart-define=API_URL=http://localhost:5199 ) > "$LOG" 2>&1 &
PRUEBA=$!

tail -n +1 -f "$LOG" | while IFS= read -r linea; do
  case "$linea" in
    *CAPTURA:*)
      nombre="${linea##*CAPTURA:}"
      nombre="$(echo "$nombre" | tr -d '\r' | awk '{print $1}')"
      sleep 1
      if xcrun simctl io "$UDID" screenshot --type=png "$DESTINO/$nombre.png" >/dev/null 2>&1; then
        # A la mitad: se lee igual en el manual y el repositorio no carga 8 MB de PNG.
        sips -Z 1278 "$DESTINO/$nombre.png" >/dev/null 2>&1
        echo "  ✓ $nombre"
      else
        echo "  ✗ $nombre"
      fi
      ;;
  esac
  kill -0 $PRUEBA 2>/dev/null || break
done

wait $PRUEBA
echo "Las capturas quedaron en $DESTINO"
