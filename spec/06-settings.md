# 06 — Settings and Remembered State

[Catalog](README.md) · [Next: user journeys](07-user-journeys.md)

## Settings by User Intent

Settings are organized around user questions: where should the panel be, when should it appear, what should it show, and how much recent context should be available? They do not expose every internal threshold or sampling detail as a separate mode. The tray remains the route to settings while the panel is hidden.

The application has one coherent set of preferences. A tray visibility action updates the same manual intent that settings reflects. Observation and display preferences are separate: changing orientation does not reset history, and selecting a recent range does not change the underlying sampling interval.

```mermaid
flowchart TB
    Settings["Settings reached through the tray"] --> Place["Where and how it appears"]
    Place --> Display["Target display, position and optional edge alignment"]
    Place --> Layout["Orientation, style, readable size and position lock"]
    Settings --> Visibility["When it is visible"]
    Visibility --> Manual["Explicit show or hide; separate from Exit"]
    Visibility --> Fullscreen["Display-local fullscreen retreat; enabled by default"]
    Settings --> Content["What it shows"]
    Content --> Metrics["Visible metrics and their order"]
    Content --> Resources["Monitored identities and independent compact pins"]
    Settings --> Observation["How recent observations are presented"]
    Observation --> Timing["Sampling cadence, retention and default chart range"]
    Observation --> Units["Consistent units and composition preference"]
    Settings --> Excluded["No edge-hide timers, collapse modes or fullscreen mini-indicators"]
```

## Appearance and Compatibility

Follow Windows light/dark appearance by default across the panel, details and settings; explicit Light and Dark overrides are included. Orientation, style and readable size are separate controls. Theme changes do not reset monitoring, visibility intent or placement.

Background opacity is an optional adjustment shared by the panel and details, with an opaque default. Settings stay solid. Text, charts, focus indicators and input targets do not fade. Alpha, desktop blur and click-through are different behaviors; the initial product includes only plain background alpha. Blur materials, click-through and whole-window fading are excluded. Transparency disabled, high contrast, battery saver or unavailable support forces a readable solid background without erasing the saved preference. Settings explains the effective fallback.

The design targets Windows 10 and newer. Appearance should degrade through readable fallbacks rather than make basic monitoring depend on optional platform effects. Actual supported builds and runtime requirements are operational facts documented in the project README, separate from this conceptual design.

## Remembered Choices versus Temporary Context

Persist user choices across restarts, not incidental desktop state. Remembering hidden intent is different from remembering that fullscreen happened to be active when the application exited. A temporary fallback monitor does not replace the preferred display unless the user makes a new placement choice.

Preferences are application-local and per-user, with no cloud synchronization or export platform. Saving preferences must preserve unrelated choices and avoid a partially written configuration. Unreadable or unavailable choices produce a reachable application with clear feedback, not elevated operation or silent erasure of all valid preferences. Storage format and migration mechanisms are outside this high-level design.

```mermaid
flowchart TB
    State["User experience state"] --> Remembered["Remembered user choices"]
    Remembered --> Preference["Manual intent, orientation, style, size, theme, opacity, monitored set and compact pins"]
    Remembered --> Position["Preferred display, per-display position and position lock"]
    Remembered --> Policy["Fullscreen policy, observation preferences, units and chart preferences"]
    State --> Temporary["Temporary context; never rewritten as a user preference"]
    Temporary --> Retreat["Current fullscreen retreat and unavailable displays"]
    Temporary --> Detail["Open details, inspected resource, comparison subset, address reveal, focus and point"]
    Temporary --> Data["Observations, freshness and availability"]
    Start["Application starts"] --> Restore["Restore remembered choices"]
    Restore --> Reconcile["Reconcile displays, resources and fullscreen context"]
    Reconcile --> Present["Apply visibility policy; do not reopen details or take focus for passive readings"]
```

## Changing a Setting

Apply ordinary reversible settings directly, without restarting. Configuration of an unavailable resource retains the choice and explains its status instead of selecting a different device. Inspection and comparison remain temporary; neither action edits the monitored set or compact pins.

History-affecting changes have visible consequences. Extending retention cannot recover discarded samples; shortening it may remove old observations. Changing units preserves measurements and changes presentation. Changing resources changes identity and scope, so histories cannot be silently joined. No generic configuration transaction framework is needed to express these behavior-specific rules.

```mermaid
flowchart TD
    Edit["User changes a preference"] --> Meaning["Explain its effect"]
    Meaning --> Apply["Apply valid choices and remember them"]
    Apply --> Reconcile["Reevaluate placement, visibility or observation context as needed"]
    Reconcile --> Feedback["Show resulting state or an unavailable-resource explanation"]
    Feedback --> Preserve["Keep unrelated choices and valid history"]
    Apply --> Scope["Reflect changed resource identity or units in all labels"]
    Scope --> NoMix["Never silently blend incompatible histories"]
```

## Settings Boundaries

A setting should express a user-visible choice, not an internal mechanism. The useful distinction is between preferences that affect observation, presentation and visibility. Making these effects clear avoids unnecessary mode combinations and prevents one setting from resetting unrelated state.

Defaults provide a coherent starting experience: a visible compact panel, system-following appearance and useful built-in resource summaries. They do not create a second monitoring model. Fine-grained control ranges and initial numeric values belong to detailed design rather than this responsibility model.

```mermaid
flowchart LR
    Choice["User preference"] --> Collection["Observation scope and recent context"]
    Choice --> Presentation["Layout, appearance and units"]
    Choice --> Visibility["Manual intent and fullscreen policy"]
    Collection --> Preserve["Preserve unrelated preferences and resource identity"]
    Presentation --> Preserve
    Visibility --> Preserve
```
