# Contexto de build: la carpeta backend/
# Se construye con: docker build -f backend/Dockerfile backend

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Los .csproj primero: mientras no cambien las dependencias, Docker reutiliza la capa
# del restore y el build no vuelve a bajar paquetes.
COPY Garaj.sln ./
COPY src/Garaj.Domain/Garaj.Domain.csproj src/Garaj.Domain/
COPY src/Garaj.Application/Garaj.Application.csproj src/Garaj.Application/
COPY src/Garaj.Infrastructure/Garaj.Infrastructure.csproj src/Garaj.Infrastructure/
COPY src/Garaj.Api/Garaj.Api.csproj src/Garaj.Api/
COPY tests/Garaj.Tests/Garaj.Tests.csproj tests/Garaj.Tests/
RUN dotnet restore src/Garaj.Api/Garaj.Api.csproj

COPY . .
RUN dotnet publish src/Garaj.Api/Garaj.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Usuario sin privilegios: la imagen de .NET 8 ya trae 'app' (uid 1654).
USER app

# Render enruta el tráfico al puerto que expone el contenedor; se sobrescribe con
# la variable PORT si el proveedor asigna otro.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Garaj.Api.dll"]
