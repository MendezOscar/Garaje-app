#!/usr/bin/env bash
# Deja la terminal lista para hablarle a la base, sin escribir la contraseña en ninguna parte.
#
#   source 0-conectar.sh              # la base local (docker compose)
#   source 0-conectar.sh produccion   # la de los talleres de verdad, de los user secrets
#
# Se usa con `source`, no ejecutándolo: tiene que dejar las variables puestas en SU terminal.
#
# psql no entiende la cadena de .NET —ella dice Host/Database/Username y psql dice
# host/dbname/user—, así que en vez de convertirla se reparte en las variables PG*, que es lo
# que psql lee solo. La contraseña nunca se imprime.

if [ "$1" = "produccion" ]; then
  CADENA=$(dotnet user-secrets list --project ../../backend/src/Garaj.Api 2>/dev/null \
           | sed -n 's/^ConnectionStrings:Default = //p')

  if [ -z "$CADENA" ]; then
    echo "No encontré ConnectionStrings:Default en los user secrets."
    echo "Sáquela de las variables de entorno de Render y expórtela a mano:"
    echo "  export PGHOST=... PGPORT=... PGDATABASE=... PGUSER=... PGPASSWORD=..."
    return 1 2>/dev/null || exit 1
  fi

  # Supabase y Render exigen TLS.
  export PGSSLMODE=require
else
  CADENA=$(grep -rho 'Host=localhost[^"]*' ../../backend/src/Garaj.Api/appsettings*.json | head -1)
  unset PGSSLMODE
fi

export PGHOST=$(sed -n 's/.*Host=\([^;]*\).*/\1/p' <<<"$CADENA")
export PGPORT=$(sed -n 's/.*Port=\([^;]*\).*/\1/p' <<<"$CADENA")
export PGDATABASE=$(sed -n 's/.*Database=\([^;]*\).*/\1/p' <<<"$CADENA")
export PGUSER=$(sed -n 's/.*Username=\([^;]*\).*/\1/p' <<<"$CADENA")
export PGPASSWORD=$(sed -n 's/.*Password=\([^;]*\).*/\1/p' <<<"$CADENA")
unset CADENA

echo "Conectado a ${PGDATABASE} en ${PGHOST}:${PGPORT} como ${PGUSER}."
