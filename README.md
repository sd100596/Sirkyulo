# Sirkyulo

Group Windows desktop icons into one icon. Click it, and they fan out in a circle.

<p align="center">
  <img src="docs/ring.png" alt="A Sirkyulo group open on the desktop" width="480">
</p>

## Getting started

1. Download and run **Sirkyulo.exe**. It sits in your system tray, near the clock.
2. **Left-click the tray icon** and give your group a name. A new icon appears on your desktop.
3. **Drag shortcuts onto it.** They move inside, and disappear from your desktop.
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

Groups hold shortcuts, folders and files. They don't hold other groups.

## Your shortcuts are safe

Shortcuts you drag into a group are stored in `%APPDATA%\Sirkyulo`. You never need to go
there.

- Files and folders are **never moved**. Only shortcuts are, and only into the group.
- Deleting a group puts everything back on your desktop first.
- If anything ever goes wrong, Sirkyulo returns your shortcuts to the desktop on next start.

To uninstall, delete each group icon first so its contents return to your desktop, then close
Sirkyulo from the tray and delete the app.

## Settings

Right-click the tray icon:

- **New group** — same as left-clicking
- **Run at startup** — tick to launch Sirkyulo when you sign in
- **Exit** — quits. Your groups stay on the desktop and work again next time you run it.

## Requirements

Windows 10 or 11, and the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0).

## Building from source

```sh
dotnet build
dotnet test
dotnet publish src/Sirkyulo.App -c Release -r win-x64 --self-contained false -o publish
```
