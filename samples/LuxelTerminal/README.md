# LuxelTerminal

Controls-independent `Luxel.Terminal.UI.TerminalView` sample using ConPTY on Windows and a Unix PTY on Linux x64.

```bash
dotnet run --project samples/LuxelTerminal/LuxelTerminal.csproj -- \
  --shell /bin/bash --font /path/to/YourNerdFontMono-Regular.ttf
```

Use `--nerd-font` when the primary monospace font and Nerd Font fallback are separate. The same paths can be supplied with `LUXEL_TERMINAL_FONT` and `LUXEL_TERMINAL_NERD_FONT`.

Text placement is adjusted through the drawing API rather than by reverse-calculating glyph positions in `TerminalView`. The default cell width is derived from the primary font's `0` advance. Optional overrides are:

```bash
--cell-width 8.5        # 0 or omitted = automatic
--cell-height 19
--glyph-offset-x 0
--glyph-offset-y 0
--glyph-scale 1
--advance-scale 1
```

`--glyph-offset-*` moves the aligned text in logical pixels, `--glyph-scale` changes visual glyph size, and `--advance-scale` adjusts glyph advances and the automatically calculated terminal cell width.

For oh-my-posh, initialize it normally from `.bashrc` or `.zshrc`; the sample does not inject shell scripts. It supplies `TERM=xterm-256color`, `COLORTERM=truecolor`, and `TERM_PROGRAM=Luxel.Terminal` unless explicitly overridden by the launch options.

Publish a Linux Native AOT build without propagating AOT properties into the source-generator project:

```bash
dotnet publish samples/LuxelTerminal/LuxelTerminal.csproj -c Release -r linux-x64 \
  -p:LuxelTerminalAot=true -p:TrimmerSingleWarn=false -o artifacts/terminal-linux-x64-aot
```

The initial Linux support target is glibc x64/X11. The built-in typography renderer currently supports TTF/TTC outlines; use a TTF Nerd Font rather than CFF/CFF2 OTF.
