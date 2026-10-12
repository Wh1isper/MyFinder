# 02 — Visibility and Fullscreen Retreat

[Catalog](README.md) · [Next: placement and displays](03-placement-and-displays.md)

## Visibility Decision

Visibility has two independent inputs: **persistent user intent** and **temporary desktop context**. The user's intent is shown or hidden. Fullscreen retreat is a temporary reason not to present an otherwise shown panel; it never rewrites that intent. This distinction is the central state-management decision.

One presentation policy derives the result after any relevant change. Manual hiding always wins. With retreat enabled, fullscreen on the current display temporarily suppresses the panel and its details. A tray Show action during fullscreen changes intent to shown but does not bypass the active retreat policy; the tray should explain why the panel is still absent. The user can explicitly change the fullscreen preference if different behavior is wanted.

This is not a collection of independent hide/show commands whose ordering happens to produce the right result. Reevaluate the latest intent and context together so that fullscreen exit, display changes or late notifications cannot reshow a manually hidden panel.

```mermaid
flowchart TD
    Change["User intent or desktop context changes"] --> Running{"Application running?"}
    Running -->|"No"| Exit["No panel, details or tray entry"]
    Running -->|"Yes"| Hidden{"User has explicitly hidden the panel?"}
    Hidden -->|"Yes"| Manual["Panel and details hidden; tray remains available"]
    Hidden -->|"No"| Enabled{"Fullscreen retreat enabled?"}
    Enabled -->|"No"| Visible["Panel visible at chosen position; details only by user request"]
    Enabled -->|"Yes"| Fullscreen{"Fullscreen content on the panel's current display?"}
    Fullscreen -->|"No"| Visible
    Fullscreen -->|"Yes"| Retreat["Temporarily hide panel and dismiss details; tray entry remains"]
    Retreat --> Intent["Do not change the user's show or hide preference"]
    Manual --> Input["No hidden hit targets or edge wake-up zones"]
    Retreat --> Input
```

## Fullscreen Is Not Ordinary Obstruction

Fullscreen classification uses relevant application state, visible display coverage and display association together. A single maximized flag or size comparison is not an adequate product definition. Ordinary maximized windows remain ordinary windows even when they occupy most of a monitor.

Desktop-context changes should prompt reevaluation; window movement and activation transitions should settle without visible hide/show oscillation. A small amount of internal transition stabilization may be necessary, but it is not the removed user-facing delayed-collapse feature. The implementation must meet the 500 ms settled-context response and no-oscillation acceptance target in chapter 08. Internal scheduling is not a user-facing timer or an open product decision.

The [adopted acceptance matrix](08-design-baseline.md#compatibility-and-evidence-gates) names Edge, VLC, LibreOffice Impress, SuperTuxKart and a controlled exclusive-fullscreen test application, including fullscreen on a non-panel display. This document does not claim a universal fullscreen detector. Disabling retreat means keeping ordinary floating-window behavior, not a guarantee of appearing above exclusive-fullscreen content.

```mermaid
flowchart TD
    Context["Window and display context"] --> Kind{"Observed situation"}
    Kind -->|"Ordinary or maximized application"| Stay["Keep panel placement; user can move or hide it"]
    Kind -->|"Fullscreen on another display"| Local["Do not retreat on an unaffected display"]
    Kind -->|"Borderless or exclusive fullscreen on panel display"| Policy["Apply fullscreen-retreat preference"]
    Policy --> NoOverlay["Retreat rather than promise an overlay above fullscreen content"]
    Context --> Evidence["Fullscreen classification needs target-application evidence"]
    Evidence --> Distinction["Do not equate a maximized window with fullscreen"]
```

## Intent Survives Temporary Retreat

Restoring visibility must preserve position and avoid focus changes. Transient detail views are dismissed, not silently reopened. Settings explicitly opened from the tray are user-requested interaction and are not an automatically displayed monitoring overlay; opening them must not force the panel visible. Exit is the separate action that ends observation and removes the tray entry.

```mermaid
flowchart TD
    Start["User preference: shown"] --> Full["Fullscreen begins; panel retreats"]
    Full --> Action{"User action while retreated"}
    Action -->|"No change"| Finish["Fullscreen ends"]
    Action -->|"Hide through tray"| Hidden["User preference becomes hidden"]
    Action -->|"Show through tray"| Shown["User preference becomes shown; explain active retreat"]
    Shown --> Finish
    Finish --> Restore["Show panel at its previous position without taking focus"]
    Hidden --> End["Fullscreen ends; panel stays hidden"]
    End --> Show["User explicitly chooses Show"]
    Show --> Evaluate["Reevaluate current fullscreen policy before showing"]
    Restore --> Closed["Previously dismissed details stay closed"]
```
