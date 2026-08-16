# Comienzo

[![Release](https://github.com/victor141516/comienzo/actions/workflows/release.yml/badge.svg)](https://github.com/victor141516/comienzo/actions/workflows/release.yml)
[![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows)](https://github.com/victor141516/comienzo/releases)

Comienzo es un menú Inicio alternativo, ligero y nativo para Windows 10 y Windows 11. Sustituye la apertura normal de Inicio, mantiene los atajos de Windows y permite buscar aplicaciones, ajustes del sistema y operaciones matemáticas desde el teclado.

## Características

- Apertura mediante la tecla Windows o un clic en el botón Inicio.
- Acceso al menú nativo manteniendo Shift.
- Compatibilidad genérica con atajos como `Win+R`, `Win+E` y `Win+L`.
- Catálogo combinado de accesos directos, aplicaciones Win32, MSIX/Store y App Paths.
- Compatibilidad con `.lnk`, `.url`, `.appref-ms` y protocolos de launchers como `steam://`.
- Búsqueda agrupada de aplicaciones y ajustes de Windows.
- Navegación con ↑, ↓ y Enter.
- Calculadora con paréntesis, precedencia y potencias.
- Ranking local de elementos más usados.
- Ventana e iconos precargados para aperturas y desplazamiento inmediatos.
- Paquetes autocontenidos para Windows x64 y Windows ARM64.

## Descargar y ejecutar

1. Abre la sección [Releases](https://github.com/victor141516/comienzo/releases).
2. Descarga el ZIP correspondiente:
   - `win-x64` para la mayoría de equipos Intel y AMD.
   - `win-arm64` para equipos Windows con procesador ARM64.
3. Extrae el ZIP y ejecuta `Comienzo.exe`.

No requiere instalación, permisos de administrador ni un runtime de .NET instalado. Windows puede mostrar una advertencia de reputación para binarios nuevos que todavía no estén firmados digitalmente.

## Uso

1. Pulsa la tecla Windows o haz clic en el botón Inicio.
2. Escribe para buscar una aplicación, un ajuste o una expresión como `(12+3)*2^3`.
3. Usa ↑/↓ y Enter, o abre un resultado con un clic.
4. Pulsa Escape o haz clic fuera para cerrar el menú.
5. Mantén Shift al pulsar Inicio para abrir el menú nativo de Windows.

El icono de la bandeja permite abrir o cerrar Comienzo y activar **Iniciar con Windows**. Si ya existe una instancia, volver a ejecutar el programa muestra esa misma ventana.

## Privacidad y datos locales

Comienzo no envía telemetría ni el historial de uso a servicios externos. El contador utilizado para la sección **Más usados** se guarda localmente en:

```text
%LOCALAPPDATA%\Comienzo\usage.json
```

## Compilar desde el código fuente

Requisitos:

- Windows 10 u 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

Compilar y ejecutar las comprobaciones internas:

```powershell
dotnet build Comienzo.slnx -c Release
dotnet run --project src/Comienzo/Comienzo.csproj -c Release -- --self-test
```

Publicar ejecutables autocontenidos:

```powershell
dotnet publish src/Comienzo/Comienzo.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
dotnet publish src/Comienzo/Comienzo.csproj -c Release -r win-arm64 --self-contained true -o artifacts/win-arm64
```

## Estructura

```text
src/Comienzo/
├── Models/       Modelos del catálogo y resultados
├── Services/     Descubrimiento, búsqueda, iconos, hooks y persistencia
├── App.xaml      Ciclo de vida, bandeja e instancia única
└── MainWindow.*  Interfaz WPF y comportamiento del menú
```

## Releases automáticas

Cada tag enviado a GitHub activa [`.github/workflows/release.yml`](.github/workflows/release.yml). El workflow compila paquetes autocontenidos para `win-x64` y `win-arm64`, genera sus sumas SHA-256 y crea una GitHub Release con notas automáticas.

Convención recomendada:

```powershell
git tag v0.2.4
git push origin v0.2.4
```

Antes de crear el tag, actualiza `<Version>` en [`src/Comienzo/Comienzo.csproj`](src/Comienzo/Comienzo.csproj) para que coincida.

## Desarrollo

Consulta [`AGENTS.md`](AGENTS.md) para conocer los comandos de validación, las invariantes del hook de teclado y las normas específicas del repositorio.

Los errores y propuestas pueden registrarse en [GitHub Issues](https://github.com/victor141516/comienzo/issues).
