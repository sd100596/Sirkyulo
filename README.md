# Sirkyulo

**Group desktop icons into one icon. Click it, and they fan out in a circle.**

A Windows tray app that does exactly one thing: collapses a cluttered desktop into a few
group icons, and shows each group as a radial menu when you click it. No launcher concept to
learn, no config file, no window. You drag icons onto a group the same way you'd drag them
into a folder.

<p align="center">
  <img src="docs/ring.png" alt="A Sirkyulo group open on the desktop, showing five game shortcuts arranged in a circle around the group name" width="520">
</p>

---

## Why

A desktop accumulates dozens of loose shortcuts. Existing tools either box them into
rectangular panes (Fences) or give you a general-purpose pie launcher with its own
configuration model (Kando). Both ask you to adopt a system.

Sirkyulo asks you to learn nothing. A group looks and behaves like a desktop icon, because it
*is* one.

## How it works

The design rests on a single decision: **a group is a `.lnk` shortcut pointing at
`Sirkyulo.exe`, carrying `--group {guid}` in its arguments.**

That one choice buys Explorer's native behaviour for free:

| You do this | Windows does the work |
|---|---|
| Drag shortcuts onto a group | Explorer launches the exe with the group's stored args **plus** the dropped paths |
| Click a group | Normal shortcut activation, no dropped paths |
| Rename a group | Native inline rename — identity lives in the guid, not the filename |
| Move a group | Native; Explorer remembers the grid position |

No shell extension, no COM icon handler, no registry footprint beyond the optional
run-at-startup entry.

The visible cost is the shortcut-arrow overlay on group icons. Windows draws that per file
*type*, so it can't be suppressed for one `.lnk` without blanking the arrow for every shortcut
on the system. A custom file type would remove it, at the cost of needing a COM handler before
per-group icons could ever work. The arrow was judged the better trade.

## Icons

Shell icons alone aren't good enough. Asked for a 96px icon, Windows will happily return a
32px asset padded onto a white document card — which is why some Steam games look broken on
the desktop. Sirkyulo resolves artwork in order of quality:

1. **Steam library cover art** — for `steam://rungameid/` shortcuts, the cached 600×900 art,
   centre-cropped. Always full size, unlike Steam's wildly inconsistent `.ico` cache.
2. **The shortcut's own `.ico`** — read directly at its largest frame, bypassing the shell's
   undersized-icon padding.
3. **The shell** — correct for everything that isn't an internet shortcut.

`.url` files get special handling throughout: they're INI text, not COM shell links, so
`IShellLinkW` can't read them at all — and Steam writes every desktop game shortcut in that
format.

## Your files are never at risk

This app moves your shortcuts around, so the rules are deliberately conservative:

- **Shortcuts** (`.lnk`, `.url`) are *moved* into the group, with their origin recorded.
- **Real files and folders are never relocated.** A link is created in the group and the
  original stays exactly where it is.
- **Every destructive path restores first.** Deleting a group — from its menu *or* by deleting
  the desktop icon — returns every shortcut before removing anything.
- **Nothing can be stranded.** If the group index is ever unreadable, startup sweeps orphaned
  group folders back onto the desktop and removes dead group icons. Worst case you lose the
  grouping, never a shortcut.

## Using it

Sirkyulo lives in the notification area.

- **Left-click the tray icon** — create a group (a new icon appears on your desktop)
- **Drag shortcuts onto a group icon** — they move in and vanish from the desktop
- **Click a group icon** — the ring opens at your cursor
- **Click an item** — launches it; **right-click** — remove from group, or open file location
- **`Esc`, click away, or switch apps** — dismisses

Groups stay one level deep. Dropping a group onto another is refused, with an explanation.

## Building

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download). Windows only — roughly 70% of
this codebase is Windows Shell interop with no cross-platform equivalent.

```sh
dotnet build                 # build everything
dotnet test                  # 43 tests, all in Sirkyulo.Core
dotnet publish src/Sirkyulo.App -c Release -r win-x64 --self-contained false -o publish
```

The published app is ~3 MB and needs the **.NET 9 Desktop Runtime**. Publish with
`--self-contained true` instead if you'd rather ship something that needs nothing installed.

## Layout

```
src/
  Sirkyulo.Core/            net9.0          Model, store, ring geometry, .url parsing
  Sirkyulo.Shell.Windows/   net9.0-windows  All Win32/COM interop, isolated here
  Sirkyulo.App/             net9.0-windows  WPF: tray, ring window, services
tests/
  Sirkyulo.Core.Tests/      xUnit
```

`Sirkyulo.Core` holds no UI types and no P/Invoke, which is what makes the ring geometry and
storage rules testable. `RingLayout` in particular is a pure function and carries all the
layout rules — including why per-item labels don't exist (real shortcut names are far wider
than a 64px icon and collide at any radius that still reads as a ring; the hovered item's
name is drawn in the empty centre instead).

## Design

The UI follows [Vercel's Geist](https://vercel.com/geist/introduction) design tokens, dark
variant — chosen because every surface floats over an arbitrary, often bright wallpaper, where
Geist's `#FAFAFA` light surface would glare. Weights 400/500/600 only, 12px card radius, 6px
functional radius, `#0072F5` reserved strictly for interactive affordances.

## Status

Working and in daily use. Not yet done:

- **Packaging** — no installer. The uninstall step must ungroup everything before removing the app.
- **`GroupService` is untested.** It lives in the WPF app project, which targets
  `net9.0-windows` and so can't be referenced from the `net9.0` test project. The code that
  moves your shortcuts is verified only by manual runs. Moving it into `Sirkyulo.Core` behind
  a filesystem abstraction is the fix.

Deliberately out of scope for now: nested groups, keyboard navigation and type-to-filter,
per-group custom icons, a global summon hotkey, a settings window, drag-to-reorder.
