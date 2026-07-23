# Sirkyulo

Group Windows desktop icons into one icon. Click it, and they fan out in a circle. 

<p align="center">
  <img src="docs/ring.png" alt="A Sirkyulo group open on the desktop" width="480">
</p>

## Getting started

1. Download **Sirkyulo-setup.exe** from the [latest release](https://github.com/sd100596/Sirkyulo/releases/latest) and run it.
2. Sirkyulo appears in your system tray, near the clock. **Left-click it** and name your group.
   A new icon appears on your desktop.
3. **Drag things onto it** — shortcuts, files, whole folders. They move inside and disappear
   from your desktop.
4. **Click the group** to open it.

## Using it

| Action | What happens |
| --- | --- |
| Click a group icon | Its contents fan out around your cursor |
| Click an item | Launches it |
| Right-click an item | Remove it from the group, or open its file location |
| `Esc`, click away, or switch apps | Closes the circle |
| Rename or move the group icon | Works normally, like any desktop icon |
| Delete the group icon | Everything inside comes back to your desktop |

Groups hold shortcuts, files and folders. They don't hold other groups.

## Uninstalling

**Settings → Apps → Installed apps → Sirkyulo → Uninstall**, or the usual Add/Remove Programs.

Everything inside your groups is returned to your desktop first, so nothing is left behind and
nothing is lost.

## Settings

Right-click the tray icon:

- **New group** — same as left-clicking
- **Run at startup** — tick to launch Sirkyulo when you sign in
- **Exit** — quits. Your groups stay on the desktop and work again next time you run it.

## Requirements

Windows 10 or 11, and the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0).

If Sirkyulo won't start, you're probably missing it. Install it with:

```
winget install Microsoft.DotNet.DesktopRuntime.9
```

Or download the **.NET Desktop Runtime 9.0.x** installer for x64 from the link above.

## Building from source

```sh
dotnet build
dotnet test
dotnet publish src/Sirkyulo.App -c Release -r win-x64 --self-contained false -o publish
```

To build the installer, compile `installer/Sirkyulo.iss` with
[Inno Setup 6](https://jrsoftware.org/isinfo.php). Output lands in `dist/`.
