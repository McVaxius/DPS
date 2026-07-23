# Dhog Potato System

**Hide the crowd. Gate rendering. Keep the party.**

An experimental Dalamud utility for weak machines. Hide non-party players, pets, chocobos, and minions while keeping party members and, optionally, your current target visible. Toggle foreground or background no-render modes, bind safety and mode hotkeys, and save or restore the game window position and size. Results vary by system; use All Off to restore normal rendering and visibility.

## Principal features

- Party-aware actor suppression for non-party players, pets, chocobos, and minions.
- Optional current-target visibility while crowd suppression is active.
- Foreground and background no-render modes with recovery controls.
- Configurable safety and mode hotkeys plus DTR status/actions.
- Display-change recovery and saved game-window position, display, and size controls.
- Guided setup for the All Off hotkey and exact window X/Y placement.

## Quick start

1. Enter `/dps` to open the plugin window.
2. Use the **Crowd** tab to choose which non-party actors to hide.
3. Use the **Render** tab only after reading its mode descriptions and safety notes.
4. Open **Hotkeys** and select **Set Up All Off Hotkey**. This safety binding restores rendering and visible actors.
5. Open **Window XY** and select **Set Up Window Position** to detect the FFXIV window, enter an exact X/Y (including negative monitor coordinates), apply it, verify the readback, and choose whether to save position/display auto-load.

The existing hotkey table remains available for foreground, background, crowd, All Off, and saved window-plus-size bindings. The existing Window XY editors and save/load/reset controls remain available for direct use.

## Safety

- Configure **All Off** before relying on no-render modes. It restores normal rendering and shows actors hidden by crowd suppression.
- Foreground and background no-render behavior is experimental. Results and performance impact vary by system.
- Display recovery can pause unsafe foreground state when the display topology changes.
- The window-position wizard never resizes the game window and never changes the size auto-load setting.
- Window placement auto-load uses the existing delayed startup safety gate; use **Window XY > Reset This Tab** or `/dps wreset` to clear saved placement and disable both placement and size auto-load.

## Commands

- `/dps` — toggle the main window.
- `/dps roff` — arm background no-render.
- `/dps ron` — disable background no-render.
- `/dps foff` — arm foreground no-render.
- `/dps fon` — disable foreground no-render.
- `/dps debug` — enable session debug mode and open the window.
- `/dps debug off` or `/dps nodebug` — disable session debug mode.
- `/dps ws` — move the plugin window to the top-left.
- `/dps j` — randomize the plugin window within the viewport.
- `/dps wsave` — save the current game-window position, size, and monitor.
- `/dps wload` — restore the saved game-window position/display while preserving current size.
- `/dps wloadall` — restore the saved game-window position/display and size.
- `/dps wreset` — clear saved placement/size and disable placement and size auto-load.

## Install, repository, and support

Dalamud custom repository URL:

```text
https://aethertek.io/x.json
```

- [Dhog Potato System source repository](https://github.com/McVaxius/DPS)
- [Aethertek plugins and guides](https://aethertek.io/)
- [Support development on Ko-fi](https://ko-fi.com/mcvaxius)
