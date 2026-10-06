# Dhog Potato System

**Hide the crowd. Gate rendering. Keep the party.**

An experimental Dalamud utility for weak machines. Hide non-party players, pets, chocobos, and minions while keeping party members and, optionally, your current target visible. Toggle foreground or background no-render modes, bind safety and mode hotkeys, and save or restore the game window position and size. Results vary by system; use All Off to restore normal rendering and visibility.

## Principal features

- Party-aware actor suppression for non-party players, pets, chocobos, and minions.
- Optional current-target visibility while crowd suppression is active.
- Foreground and background no-render modes with recovery controls.
- Configurable safety and mode hotkeys plus DTR status/actions.
- Resolution controls through optional XA Slave IPC or first-party Custom Resolution's native Display, Gameplay, and Common settings, with provider install buttons and a toggle hotkey.
- Display-change recovery and saved game-window position, display, and size controls.
- Guided setup for the All Off hotkey and exact window X/Y placement.

The main header and Advanced window share a language selector, theme colour
swatch, and **C** compact-mode checkbox. English, German, French, Spanish,
Italian, Russian, Japanese, Korean and Simplified Chinese are available across
DPS controls, diagnostics and guided setup. Teal, blue, pink and custom RGB
colours apply to the interface; rendering and recovery status colours retain
their meanings. These preferences use the existing configuration save path.
Compact mode reduces spacing while retaining the same controls and saved choices.
Custom Resolution's embedded settings remain owned by that provider.

## Quick start

1. Enter `/dps` to open the plugin window.
2. Use the **Crowd** tab to choose which non-party actors to hide.
3. Use the **Render** tab only after reading its mode descriptions and safety notes.
4. Open **Hotkeys** and select **Set Up All Off Hotkey**. This safety binding restores rendering and visible actors.
5. Open **Window XY** and select **Set Up Window Position** to detect the FFXIV window, enter an exact X/Y (including negative monitor coordinates), apply it, verify the readback, and choose whether to save position/display auto-load.

The existing hotkey table remains available for foreground, background, crowd, All Off, and saved window-plus-size bindings. The existing Window XY editors and save/load/reset controls remain available for direct use.

Open **Resolution**, select **XA Slave** or **Custom Resolution**, and install the selected provider if needed. XA Slave has its own scale and enable/disable buttons. Its toggle follows successful DPS requests in the current session; changes made directly in XA Slave are not reflected in that toggle state.

Custom Resolution embeds the installed plugin's **Display**, **Gameplay**, and **Common** settings, including **Save and apply**. The first **Enable gameplay scaling** action (or hotkey toggle to on) applies the DPS preset once: display disabled at 1.0x in scale mode; gameplay enabled at 0.05x in scale mode; both stored sizes 1024x1024; Point graphics upscaling; WindowSize display mode; DXVK workaround Off; minimum window size Unchanged; fullscreen fixes and auto-disable on lag enabled; both native hotkeys unbound with no modifier. DPS records completion only after the provider saves successfully. These values are embedded in DPS; no other account's files are read at runtime. Later enables and provider switches preserve saved settings.

Bind **Resolution** in **Hotkeys** for the DPS gameplay toggle. With Custom Resolution, Off sets gameplay to 1.0x and keeps native gameplay scaling enabled so that value takes effect. On restores the last saved gameplay scale remembered during this session. If a new session starts at 1.0x, On uses 0.05x. Display settings stay untouched by the toggle after the initial preset. Use the native **Save and apply** button to commit edits before toggling.

**Advanced options** at the top reveals the non-default foreground frozen-frame choice without changing your saved render mode.

## Safety

- Configure **All Off** before relying on no-render modes. It restores normal rendering and shows actors hidden by crowd suppression.
- Foreground and background no-render behavior is experimental. Results and performance impact vary by system.
- Display recovery can pause unsafe foreground state when the display topology changes.
- The window-position wizard never resizes the game window and never changes the size auto-load setting.
- Under **Window XY**, choose a monitor directly from the saved-window dropdown to move the live window and preserve that physical target even if Windows renumbers `DISPLAY` devices.
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
