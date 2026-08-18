# Dhog Potato System UI/UX Recommendations

**Review date:** 2026-08-18  
**Scope:** UI code review only; no runtime behaviour or implementation changes are included in this document.

## Product goal

Reduce rendering load safely, always retain a reliable recovery path, and understand the effect and risk of each suppression mode.

## Reviewed surfaces

- `DPS/Windows/MainWindow.cs`

## What is already working

- All Off, Restore FG, Restore BG, and Show All are persistently available in the top bar.
- Foreground, background, crowd, hotkey, DTR, window placement, diagnostics, and recovery settings are separated into tabs.
- Hotkey and window-position setup wizards already address two error-prone tasks.

## Prioritized recommendations

| Priority | Recommendation | Rationale and completion signal |
| --- | --- | --- |
| P0 | Make All Off the permanent safety anchor. | Keep it visually distinct and reachable from every tab and DTR interaction; after use, show exactly which render/crowd states were restored. |
| P0 | Explain effect and risk before enabling a mode. | For foreground, background, crowd, safe recovery frames, and black-screen legacy mode, show what disappears, when it applies, expected benefit, and recovery method. |
| P0 | Demote unsafe tuning. | Throttle sleep, raw recovery timings, diagnostics, and texture experiments should require an Advanced/Experimental reveal and preserve a known-safe preset. |
| P1 | Show one unified current-state summary. | Display foreground, background, crowd, recovery guard, target exceptions, and active hotkeys in plain language instead of requiring tab-by-tab inspection. |
| P1 | Finish setup wizards with validation. | Hotkey setup should show conflicts and a test result; window XY setup should show original versus proposed geometry and confirm read-back before saving. |
| P1 | Pair colours with stable labels. | Warnings, helper status, and render state must remain understandable with colour disabled or altered. |
| P2 | Move DTR customization away from core controls. | Keep DTR enable and preview in Interface/Advanced while retaining only the current DTR state in the top bar. |

## Suggested information hierarchy

1. Safety anchor and current state
2. Recommended render controls
3. Crowd controls
4. Recovery
5. Advanced tuning and diagnostics

## Validation checklist

- A new user can identify the primary action and current blocker within five seconds.
- Every disabled control has a nearby plain-language reason and, when possible, a direct corrective action.
- Healthy, warning, error, running, and disabled states remain distinguishable without colour.
- The UI remains usable at narrow window widths and common Dalamud UI scales without clipped labels or unreachable controls.
- Destructive, global, or high-impact actions identify their scope and require confirmation or provide a safe undo.
- Empty, loading, stale-data, success, partial-success, and failure states each provide an appropriate next action.
- Settings clearly identify whether they apply globally, per account, per character, per preset, or only for the current session.
- Advanced diagnostics are still reachable but do not compete with the everyday workflow.

## Recommended implementation order

1. Implement P0 items and validate the primary workflow plus blocker recovery.
2. Implement P1 information-architecture and configuration improvements.
3. Apply P2 polish, then test at multiple UI scales with both fresh and mature configurations.
