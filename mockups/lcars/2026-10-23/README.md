# DPS LCARS concepts

Design review only. No product implementation, build, deployment or client action is included.

- [Main: regular and compact](01-main-regular-compact.png)
- [Settings, Mini and supporting surfaces](02-settings-mini-and-auxiliary.png)

The second sheet depicts selected existing supporting surfaces. Other source windows retain their established layout and actions under the proposed shared appearance; this is not a claim that every scroll position or conditional state has a separate image. Browser companions retain browser controls. Main/Mini windows are distinct from density variants. Generated text and examples are design material, not runtime evidence.

## Source scope

Source commit: `49a756cbb6b157a8bf00b7603a23ef5410f59c79`. Existing dirty paths were inspected by count (0); none are included or changed by this mockup task.

Existing performance/rendering utility, not a damage meter. Main controls All Off, Restore FG, Restore BG, Show All, Hotkeys, Advanced; foreground/background no-render status and crowd suppression. Retain clear semantic enabled/off states and the current-target/party preservation purpose. Fabricated idle/off state, no benchmark gain or FPS percentage claims.

Advanced tabs Render, Crowd, RenderTrim, Resolution, Hotkeys, DTR, Window XY, Diagnostics, Appearance, About. Show actual Appearance plus separate RenderTrim master/apply/revert settings reference and crowd controls. Any unavailable host capability is disabled with text. No invented FPS chart or promised performance result.

## Existing UI source inventory

- `DPS/Windows/AdvancedWindow.cs`
- `DPS/Windows/MainWindow.cs`

## Shared appearance contract

Auto and Picker retain their order and values; LCARS is the third option. Preserve native IDs, original actions, saved scope and semantic status colours. The proposed LCARS UI tones default off; the speaker and Settings preference are the same value, with no added volume or pack selector. Krangle obfuscates identifying text. Actual adoption waits for the FrenRider pilot.

Exact generation prompts are in [PROMPTS.md](PROMPTS.md).

