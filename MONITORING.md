# Monitoring and Desktop Operation

## Panel and lifetime

MyFinder runs as a standard-user tray application. Drag **Move** to reposition the panel; **Lock panel position** disables movement. Settings offers horizontal/vertical orientation, Balanced/Minimal/Capsules styles, readable size, per-display edge alignment, metric order and explicit visibility. Edge alignment never enables auto-hide. Oversized content remains scrollable on a constrained work area.

Click a metric for a reusable detail window. Resource and trend selection affect inspection only. Escape, outside activation or **Close details** dismisses it; closing Settings likewise keeps monitoring alive. Use **Exit MyFinder** in Settings or the tray menu to stop collection and close the application. Repeated launch with the same profile does not change visibility.

Fullscreen retreat uses the foreground window's bounds, maximized state, visibility and cloaking on the panel's display, excluding desktop/taskbar windows. It preserves explicit hide/show intent. Leaving fullscreen restores only the panel, not dismissed details. Exclusive-fullscreen applications and protected overlays require application-specific validation; no injection or Explorer replacement is used.

Placement is stored separately for each identified display. A temporarily absent preferred display uses a reachable fallback without overwriting its preferred placement. The window uses per-monitor-v2 DPI, native work-area bounds and passive activation. Mixed-DPI physical display transitions remain a hardware qualification gate.

## Appearance

System, Light and Dark themes share the same observations and geometry. Default background opacity is 100%. Optional opacity changes a native color-only composition backdrop, leaving foreground controls opaque. It applies to the panel and details; Settings remains solid. High contrast, disabled advanced transparency effects, energy saver and unavailable composition select an explained solid background while retaining the preference. This backdrop adds no blur.

## Observations and history

Sampling defaults to one second; Settings offers 0.5, 1 and 2 seconds. CPU and memory share one Windows collection loop independent of visible windows. Snapshots carry resource identity, wall-clock time, monotonic elapsed time, segment and per-field quality. Startup, prolonged gaps and changed monitor selection create continuity boundaries. Missing counters remain unavailable; they are never substituted with zero. An old snapshot is presented as stale rather than a current value.

History defaults to 15 minutes with 5/15/30/60-minute choices and a 64 MiB estimated sample-storage ceiling. Oldest history expires first when the budget is reached, with a visible notice. Expiry continues when every resource is disabled. Increasing retention cannot recreate expired samples. History and temporary inspection are session-only.

Charts preserve gaps and extrema while reducing drawing to visible resolution. Use the pointer or Left/Right keys to inspect an actual observation; Home selects the oldest retained point and End returns to the latest. Live headline values remain live during historical inspection. Percentage axes use 0–100%; physical-memory trends use usable capacity and commit trends use the commit limit. Composition bars/rings show only the coherent in-use/available physical partition.

## CPU sources

- `Processor Information` English PDH counters supply total and group-aware logical-processor non-idle-time percentages. This is time utilization, not Task Manager's frequency-normalized utility algorithm. Logical names retain their processor-group component.
- `Processor Frequency` reports the Windows performance-counter frequency; it is not a guaranteed instantaneous per-core clock.
- `GetPerformanceInfo` supplies process, thread and handle counts. `GetTickCount64` supplies system uptime.
- WMI processor inventory supplies model, sockets, cores, logical count, firmware maximum frequency, firmware virtualization and reported L2/L3 sizes. These slow facts load separately from the sample loop. Firmware facts can be absent or imprecise on virtualized systems.

## Memory sources

- `GetPerformanceInfo` supplies usable physical total, available pages, commit/limit, system cache and kernel pools. **In use = usable total − available**; those two parts alone form the physical-memory composition.
- Cache, compression and pools overlap other memory categories and are labeled as such. They are not added as extra parts of the composition.
- `GetPhysicallyInstalledSystemMemory` supplies installed bytes. Installed minus usable is reported as hardware reserved only when the values are coherent.
- WMI supplies module count, configured transfer rate, slots, form factor and module locations. Unknown firmware values are retained as unknown.
- The optional compressed-page counter shows its PDH failure code when unavailable. No privilege escalation, private kernel API or driver is used to obtain it.

Memory sizes use binary units (KiB/MiB/GiB); raw measurements are retained without display rounding.

## Network adapters

The network loop uses `GetIfTable2` / `MIB_IF_ROW2` separately from CPU/memory. Interface GUIDs identify preferences and history; mutable names and indices never identify an adapter. A changed runtime LUID breaks the counter baseline. The catalog distinguishes physical, virtual/software, loopback and tunnel interfaces and excludes filter interfaces. Link state, reported directional link speeds and model are resource facts, not measured traffic rates.

Receive/send use error-free `InOctets`/`OutOctets` deltas over actual monotonic intervals. The source includes unicast, multicast and broadcast and is not application payload accounting. Reset, missing/down interfaces, monitoring changes and long gaps break the baseline; the next sample warms up. Each direction has an observed-byte total, covered duration and segment count. Totals include only valid observed segments in the current application session. They exclude unseen intervals and are neither lifetime totals nor billing estimates. Virtual/physical interface traffic can overlap and is never added into an Internet-wide total.

Settings independently chooses monitored adapters, one compact pin and byte/s or bit/s display. First discovery selects exactly one active physical adapter when unambiguous. Otherwise Settings requests a choice; later route changes never repin. **Refresh available resources** updates choices after topology changes. Saved absent resources remain selectable and disconnected. Unselected removed entries expire with retention; the catalog retains at most 512 unselected entries in addition to selected resources. Histories share the global time/byte bounds.

Network details show both directions on one scale, explicit units, current readings and observed-segment totals. Addresses stay outside routine snapshots, preferences, logs and history. **Reveal addresses for 30 seconds** makes an on-demand temporary query; changing resource, closing details or expiry clears the display. Address queries are single-flight and do not block collection.

Tests cover real native discovery/counters and deterministic rename, replacement, disconnect, reset, pause, ambiguity and churn behavior. A controlled 16 MiB same-host TCP transfer is exercised separately: Windows can bypass interface octet accounting for loopback, in which case directional counter validation is explicitly skipped. Physical/VPN unplug/reconnect and controlled traffic through another endpoint require an appropriate test environment; no adapter is disabled or reconfigured on the owner's machine.

## Preferences and diagnostics

Defaults are stored under `%LOCALAPPDATA%\MyFinder\preferences.json`. Use `MyFinder.exe --settings` to open Settings at launch, or `--profile <directory>` for an independent profile. There is one instance per profile. Profile paths are per-user data, not files beside the executable.

Each save serializes a validated snapshot through a same-directory temporary file and atomic replacement. Invalid individual fields fall back without discarding other choices. Unreadable documents and newer schema versions remain read-only with a visible warning; the original file is preserved. Save failures are visible in Settings. An unhandled application exception is recorded as `last-error.txt` in the selected profile and remains a failure, not a synthetic successful session.

## Verification boundary

Function-style Pester tests exercise production policy, rates, history, preference recovery, actual Windows CPU/memory sources and compiled UI controls with isolated profiles. Build/package/signing checks remain in the normal test entry point. Desktop integration requires an interactive Windows session. The pixel test uses a small owned black/white underlay, verifies the panel owns the captured region, and inspects only that rectangle; it also accepts an explicitly reported system-policy solid fallback. An active Windows lock screen blocks pixel validation and is reported as a skip, never as proof of alpha rendering.

The current application minimum remains Windows 11 build 22000. Windows 10 build 19044 is a product target pending a matching runtime/packaged test environment. Physical mixed-DPI hot-plug, exclusive fullscreen, sleep/lock, Explorer restart and system-wide accessibility/energy transitions require a designated test session. Tests on the owner's desktop do not modify those settings, restart Explorer, install packages or change certificate trust. A passed build or deterministic policy test does not certify those environment-specific cases.
