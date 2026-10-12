# 07 — End-to-End Journeys and Completion Boundaries

[Catalog](README.md) · [Next: adopted design baseline](08-design-baseline.md)

## Daily Observation

The primary path connects all earlier responsibilities: restore intent, establish current desktop context, collect observations, present the compact view, and inspect a selected resource. This is an end-to-end behavior description, not an implementation sequence or a claim of completed features.

A normal user should not need to understand which component collects a metric or places a window. They should know where the panel is, why it may be hidden, what a value means, and how to inspect it. Those are the acceptance boundaries for the adopted architecture.

```mermaid
flowchart TD
    Start["Start application"] --> Restore["Restore preferences and a reachable position"]
    Restore --> Tray["Make tray controls available"]
    Tray --> Observe["Begin truthful current readings and recent history"]
    Observe --> Gate["Evaluate manual intent and current fullscreen policy"]
    Gate --> Panel["When eligible, show the compact panel"]
    Panel --> Arrange["Choose horizontal or vertical layout; move away from obstructed content"]
    Arrange --> Lock["Optionally lock the position"]
    Lock --> Click["Click CPU, memory, network or storage"]
    Click --> Detail["Inspect large recent charts and detailed resource values"]
    Detail --> Close["Dismiss details; compact monitoring continues"]
    Close --> Full["Fullscreen begins on the panel display"]
    Full --> Retreat["With retreat enabled, hide panel and details without moving elsewhere"]
    Retreat --> Resume["After fullscreen, restore only if user intent remains shown"]
```

## Visibility, Recovery and Exit Scenarios

Use scenarios that exercise competing intentions and desktop changes, not just isolated happy paths. Each scenario must preserve the distinction between preferred placement and temporary fallback, and between user hiding and fullscreen retreat. Test both horizontal and vertical layouts and details opened near each edge.

| Area | Evidence needed before declaring the capability complete |
| --- | --- |
| Visibility | Manual hide/show before, during and after fullscreen; ordinary maximized windows; fullscreen on another display. |
| Displays | Mixed scaling, display reordering, disconnect/reconnect, taskbar bounds, and movement across shared edges. |
| Detail interaction | Keyboard and pointer inspection, dismissal, resource changes, and hide/retreat while details are open. |
| Observation semantics | Known inputs for rates and totals, counter resets, resource changes, valid zero, stale values and missing intervals. |
| History and charts | Partial coverage, retention boundaries, consistent units, capacity composition and gaps under reduced chart detail. |
| Lifecycle and cost | Sleep/resume, shell restart, clean exit, responsive interaction, bounded history and no drawing for closed details. |

These are required evidence categories. [Chapter 08](08-design-baseline.md#compatibility-and-evidence-gates) fixes the desktop/application matrix and performance budgets. Record exact tested versions, hardware and results when executing those gates; the design does not substitute for them.

```mermaid
flowchart TD
    Check["Scenario coverage"] --> Manual["Manual hide, then enter and leave fullscreen"]
    Manual --> Still["Panel stays hidden; tray can request Show"]
    Check --> Local["Fullscreen begins on another display"]
    Local --> Unchanged["Panel remains unaffected on its own display"]
    Check --> Move["Move between displays with different scaling"]
    Move --> Readable["Readable layout, reachable details and correct local retreat"]
    Check --> Disconnect["Disconnect the preferred display while the panel is hidden"]
    Disconnect --> Reachable["Recover placement without overriding manual hide"]
    Check --> Sleep["Sleep, resume or restart the desktop shell"]
    Sleep --> Reconcile["Restore controls and placement; show observation gaps honestly"]
    Check --> Quit["Choose Exit through the tray"]
    Quit --> Stopped["Close all surfaces and stop observation; no tray entry remains"]
```

## Second-Round Review Scenarios

These acceptance scenarios are adopted requirements; the static images do not prove them implemented.

| Scenario | Expected result |
| --- | --- |
| Monitor C:, D: and E:, pin C: used capacity, inspect D: | All three histories continue. The panel still shows C:; no extra permanent tiles appear. |
| Compare two devices, then return to one volume | Shared-scale comparison does not change collection or join volume/device totals. |
| Unplug a pinned external drive | The same identity becomes disconnected with its last-observed time. No silent fallback to C: or a same-letter replacement. |
| Stop monitoring an inspected or pinned drive | New samples stop with an explicit boundary; old observations expire normally. Replacement remains an explicit user action. |
| Switch CPU total/logical view or resize details | Existing history remains; narrower layouts reflow/scroll without changing collection cadence. |
| Inspect a historical network point across a gap | The point shows time, direction, units and quality. Current values stay current; missing intervals are not zero. |
| Compare memory headline, composition and facts | In-use + available equals usable total; reserved memory explains the installed difference. Cache/compression are not counted twice. |
| Change Windows theme or disable transparency | Panel, details and settings remain consistent and readable. Foreground data does not fade with the background. |
| Use a small desktop at increased text scale | Essential labels and close/navigation controls remain usable; no uniform shrink-to-fit or edge-hide workaround. |
| Validate the Windows 10 product target | Target Windows 10 21H2 build 19044+ x64, using chapter 08's test matrix. Validate packaging, runtime and solid fallback before changing compatibility claims; the current Windows 11 foundation is not evidence. |

## Complete Capability Deliveries

Delivery follows complete capability boundaries, not a series of disposable demos. The desktop interaction capability includes its visibility state, placement, tray access, both layouts, settings and recovery behavior. Each monitoring capability includes real observations, truthful quality states, compact presentation, history, meaningful details and verification.

The boundaries are not an instruction to invent a framework first or divide implementation across parallel owners. Shared behavior should have one owner because the product needs consistent state, not because every diagram box must become an abstraction. A monitoring capability is not complete when it can display a fabricated number or an isolated chart.

```mermaid
flowchart TB
    Complete["A capability is complete only as a coherent user experience"] --> Window["Desktop interaction capability"]
    Window --> W["Persistent panel and tray; manual visibility; fullscreen retreat; placement and both layouts"]
    W --> WV["Settings, remembered state, mixed-display recovery and matching scenario evidence"]
    Complete --> Monitoring["Each monitoring capability"]
    Monitoring --> M["Real observations; resource identity; truthful current values; bounded recent history"]
    M --> MV["Compact metric, meaningful charts, detailed values, settings and unavailable-data behavior"]
    Complete --> Evidence["Evidence and limits"]
    Evidence --> Honest["A build or diagram is not proof of desktop behavior or measurement correctness"]
    Evidence --> NoDemo["No fabricated metrics or disconnected UI demos presented as a completed module"]
    Evidence --> Document["Document actual behavior, tested scenarios and remaining compatibility limits"]
```

## Source Build and Optional Trusted Installation

Both source-built unpackaged use and explicitly trusted self-signed installation remain supported product paths. Their monitoring behavior is the same. Building a package, trusting a certificate and installing it are separate user decisions; no trust operation is hidden in routine application startup. Detailed commands belong in the root README rather than in the architecture specification.

Public distribution is not the current objective. The existing build/test/release foundation is not evidence that these adopted monitoring and desktop behaviors already work.

```mermaid
flowchart TD
    User["User obtains project source"] --> Build["Build locally"]
    Build --> Choice{"How to use the build?"}
    Choice -->|"Run unpackaged"| Direct["Run the complete local application directory without signing or certificate trust"]
    Choice -->|"Install a self-signed package"| Sign["Use the user's own signing identity; keep private material private"]
    Sign --> Trust["User explicitly chooses whether to trust the public certificate"]
    Trust --> Install["Install through normal signature verification"]
    Direct --> Product["Same monitoring product and settings semantics"]
    Install --> Product
    Boundary["Public distribution and store signing are not current product requirements"] --> Separate["Build and installation procedures remain in the root README"]
```
