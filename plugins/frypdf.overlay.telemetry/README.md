# Document Telemetry HUD Plugin (`frypdf.overlay.telemetry`)

Live performance telemetry HUD showing managed heap allocation, GC cycles, and memory trimming in the `shell.overlay` slot.

## Features
- **Managed Heap Monitoring**: Real-time memory allocation in MB.
- **Garbage Collection Counter**: Tracks GC gen0, gen1, gen2 cycles.
- **One-Click Heap Trimming**: Trigger memory compaction and collection.
- **Hardware Architecture**: Displays OS and processor information.

## Building and Packaging
To build and create the `.fryplugin` distribution archive:
```bash
dotnet build -c Release
```
This produces `bin/Release/net10.0/Telemetry.fryplugin`.
