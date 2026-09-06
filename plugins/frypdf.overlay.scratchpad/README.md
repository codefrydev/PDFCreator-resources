# Review Scratchpad & Notes Plugin (`frypdf.overlay.scratchpad`)

Floating Markdown scratchpad with live word/char counters and timestamp logs for PDF document review.

## Features
- **Automatic M3 Expressive Chrome**: Automatically docks into the `shell.overlay` slot with drag and pin physics.
- **Real-Time Word & Character Counters**: Dynamically updates as you type notes.
- **Timestamp Logging**: One-click rapid note timestamp inserts.
- **Configurable Settings**: Option to auto-clear notes on close.

## Building and Packaging
To build and create the `.fryplugin` distribution archive:
```bash
dotnet build -c Release
```
This produces `bin/Release/net10.0/Scratchpad.fryplugin`.
