# Monitoring and Desktop Operation

## Panel and lifetime

MyFinder runs as a standard-user tray application. Drag the **Move panel** grip to reposition the panel; **Lock panel position** disables movement. Settings offers horizontal/vertical orientation, Balanced/Minimal/Capsules styles, readable size, per-display edge alignment, metric order and explicit visibility. Edge alignment never enables auto-hide. Oversized content remains scrollable on a constrained work area.

Click a metric for a reusable detail window. Resource and trend selection affect inspection only. Escape, outside activation or the native window close action dismisses it; closing Settings likewise keeps monitoring alive. Use **Exit MyFinder** on Settings → **Status** or the tray menu to stop collection and close the application. Repeated launch with the same profile does not change visibility.

Fullscreen retreat uses the foreground window's bounds, maximized state, visibility and cloaking on the panel's display, excluding desktop/taskbar windows. It preserves explicit hide/show intent. Leaving fullscreen restores only the panel, not dismissed details. Exclusive-fullscreen applications and protected overlays require application-specific validation; no injection or Explorer replacement is used.

Placement is stored separately for each identified display. A temporarily absent preferred display uses a reachable fallback without overwriting its preferred placement. The window uses per-monitor-v2 DPI, native work-area bounds and passive activation. Mixed-DPI physical display transitions remain a hardware qualification gate.

## Fluent surfaces

The interface uses native WinUI controls and Windows theme resources. Settings has five navigation categories: **Appearance**, **Placement**, **Resources**, **History** and **Status**. The navigation pane adapts to available width. A persistent footer reports panel visibility and offers one stateful **Show panel** / **Hide panel** action; fullscreen retreat is explained separately from manual intent. **Status** owns diagnostic counts and explicit Exit. The native close action and Escape dismiss Settings. Resource monitoring, compact pins and comparison choices are grouped on **Resources**, with inspection actions disabled and explained while the panel is hidden. Compact inclusion and ordering are separate: **Move first** only reorders an already selected tile. Preferences apply immediately and normalized choices are reflected back into their controls.

Details open at 760 × 640 DIP, clamped to the available work area and anchored beside the panel. They use a resource rail when enlarged and a resource chooser at the default or narrower width. CPU, memory and monitored resources lead the chooser; storage overview and comparison are destinations in that same navigator. The resource heading, current quality, Settings, resource/trend selectors and chart range stay above the scrollable content. Selectors without a meaningful choice are omitted. Empty or unmonitored views offer a direct route to Resources without changing monitoring silently. Headline cards, a dominant trend chart, coherent capacity composition and paired observation/hardware fact groups reflow without shrinking text. Hover previews a chart point; clicking or keyboard navigation holds historical inspection with a visible cursor, exact timestamped values and a conditional **Back to live** action. Storage overview and comparison use the same cards and charts. Inspection continues to leave monitoring and pins unchanged.

The compact panel uses short CPU/MEM/NET/DISK labels, stable tile widths, and thin utilization tracks for CPU/memory in Balanced and Capsules styles. Full resource names remain in tooltips and details. At the default scale, the CPU/memory strip is about 240 × 60 DIP; adding network/storage keeps the strip bounded independently of monitored resource count. Native move and settings glyphs retain accessible names and tooltips; exit uses an explicit text label. Primary values, secondary labels and theme-aware surfaces establish contrast without oversized icons. Layout styles remain independent of orientation and scale. No browser host or additional UI framework is required.

Desktop tests exercise categorized navigation, state-dependent actions, separate inclusion/ordering, native close, continued hidden collection, keyboard chart inspection, address reveal expiry, theme changes, fixed toolbars during scrolling and resource independence. In an unoccluded session they retain owned-window Light/Dark screenshots in `TestResults/ui-*.png` for visual review; the CI test-results artifact includes these images. Screen ownership is checked before capture. Lock-screen occlusion is reported as a skip.

## Appearance

System, Light and Dark themes share the same observations and geometry. Default background opacity is 100%. Optional opacity changes a native color-only composition backdrop, leaving foreground controls opaque. It applies to the panel and details; Settings remains solid. High contrast, disabled advanced transparency effects, energy saver and unavailable composition select an explained solid background while retaining the preference. This backdrop adds no blur.

## Observations and history

Sampling defaults to one second; Settings offers 0.5, 1 and 2 seconds. CPU and memory share one Windows collection loop independent of visible windows. Snapshots carry resource identity, wall-clock time, monotonic elapsed time, segment and per-field quality. Startup, prolonged gaps and changed monitor selection create continuity boundaries. Missing counters remain unavailable; they are never substituted with zero. An old snapshot is presented as stale rather than a current value.

History defaults to 15 minutes with 5/15/30/60-minute choices and a 64 MiB estimated sample-storage ceiling. Oldest history expires first when the budget is reached, with a visible notice. Expiry continues when every resource is disabled. Increasing retention cannot recreate expired samples. History and temporary inspection are session-only.

Charts preserve gaps and extrema while reducing drawing to visible resolution. Hover previews an actual observation without leaving live mode. Click or use Left/Right to hold a point; Home selects the oldest retained point. **Back to live** or End resumes live mode; changing resource/range or expiration of the held sample also clears inspection. Live headline values remain live during historical inspection. Percentage axes use 0–100%; physical-memory trends use usable capacity and commit trends use the commit limit. Composition bars/rings show only the coherent in-use/available physical partition.

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

Settings independently chooses monitored adapters, one compact pin and byte/s or bit/s display. First discovery selects exactly one active physical adapter when unambiguous. Otherwise Settings requests a choice; later route changes never repin. **Refresh list** updates choices after topology changes. Saved absent resources remain selectable and disconnected. Unselected removed entries expire with retention; the catalog retains at most 512 unselected entries in addition to selected resources. Histories share the global time/byte bounds.

Network details show both directions on one scale, explicit units, current readings and observed-segment totals. Addresses stay outside routine snapshots, preferences, logs and history. **Reveal addresses for 30 seconds** makes an on-demand temporary query; changing resource, closing details or expiry clears the display. Loading can be canceled, and visible/hidden state is explicit in the label and accessible name. Address queries are single-flight and do not block collection.

Tests cover real native discovery/counters and deterministic rename, replacement, disconnect, reset, pause, ambiguity and churn behavior. A controlled 16 MiB same-host TCP transfer is exercised separately: Windows can bypass interface octet accounting for loopback, in which case directional counter validation is explicitly skipped. Physical/VPN unplug/reconnect and controlled traffic through another endpoint require an appropriate test environment; no adapter is disabled or reconfigured on the owner's machine.

## Volumes and storage devices

Volumes and devices are separate resources. Settings chooses the monitored set, one compact storage pin, and up to four explicitly selected devices for comparison. First-run storage selection pins and monitors the Windows volume when its identity is known; later discovery never silently substitutes another volume. The default compact tile shows used/reserved capacity. A device pin instead shows read/write rates. Adding monitored volumes or devices does not add tiles.

**Storage overview** lists every monitored storage resource, including unavailable entries. **Storage comparison** uses the selected devices as small multiples with shared axes for read/write, activity or response time. Neither view sums parent-device activity with child-volume capacity or adds device-busy percentages. Inspecting another resource leaves monitoring, pin and comparison choices unchanged.

### Capacity and topology

- `FindFirstVolume` enumerates local volume GUID paths; drive letters and labels are presentation only. `GetDiskSpaceInformation` provides actual allocation units independent of quotas. Total and free form a coherent partition; **used / reserved = total − free**. Caller-available quota space is a separate field.
- On a filesystem reporting no quota support, `GetDiskFreeSpaceEx` supplies capacity if the allocation-unit query is unsupported. On quota-capable filesystems an unavailable actual total remains unavailable rather than being replaced by the caller's quota.
- Capacity defaults to 30 seconds, with 10/30/60-second settings. Its timestamp, freshness and chart continuity use this slower cadence. WMI boot-volume, system boot-files volume and page-file roles refresh in a separate single-flight inventory operation.
- `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS` supplies zero, one or multiple reported device numbers. Each mapped device's identity is queried separately and included in that volume snapshot. Unknown/partial mappings retain useful capacity and never attribute an unrelated disk's activity. Network shares are outside local volume enumeration and have no local-device attribution.
- SetupAPI enumerates present disk interfaces. A device-associated VPD identifier is preferred; reported bus/model/vendor/serial identity is the fallback. Raw identifiers are hashed for resource keys. A device without a usable identity has a session-only entry and explicitly unavailable activity. Multiple present claimants for one identity are ambiguous; an absent old interface path does not suppress a single returning device.

### Device activity and source isolation

`PhysicalDisk` PDH supplies read/write byte rates, idle-time percentage and average seconds per completed transfer. The collector checks a fresh native device number and durable identity before and after the counter snapshot. A changed binding, selection generation or prolonged gap establishes a new baseline. **Activity = 100 − idle-time percentage**; response time is shown in milliseconds. These are Windows-reported device metrics shared by all its volumes, including caching/storage-stack effects. They do not identify hidden hardware below RAID, virtualization or Storage Spaces.

A coordinator schedules at most eight outstanding storage operations, one discovery and one role-inventory operation. A slow native request keeps its slot; timeouts never spawn replacement workers behind it. Other slots and the independent CPU/network loops continue. If all eight storage slots are blocked, additional storage work waits and old values become stale. No synchronous storage I/O runs on the UI thread. Changed generations discard late results and cached metrics; stopped intervals cannot be backfilled on re-enable. Removed unselected entries expire with retention and have a 512-entry catalog bound in addition to current/selected resources. Shared history retains its global 64 MiB/time bounds.

Native tests use read-only discovery and dedicated temporary files with delete-on-close for controlled I/O. They never format, mount, raw-write, change storage policy or delete user files. Three-or-more-volume histories and the native UI are exercised when the host provides them. Real two-device comparison, removable hot-plug, complex topology and I/O on disposable test volumes require their matching environments; absence is a reported qualification gap.

## Preferences and diagnostics

Defaults are stored under `%LOCALAPPDATA%\MyFinder\preferences.json`. Use `MyFinder.exe --settings` to open Settings at launch, or `--profile <directory>` for an independent profile. There is one instance per profile. Profile paths are per-user data, not files beside the executable.

Each save serializes a validated snapshot through a same-directory temporary file and atomic replacement. Invalid individual fields fall back without discarding other choices. Unreadable documents and newer schema versions remain read-only with a visible warning; the original file is preserved. Save failures are visible in Settings. An unhandled application exception is recorded as `last-error.txt` in the selected profile and remains a failure, not a synthetic successful session.

## Runtime measurements

After building, run `./scripts/measure-runtime.ps1 -SecondsPerPhase 60` in an appropriate Windows test session. It starts an isolated profile, monitors available local resources (up to 8 adapters, 16 volumes and 16 devices), measures visible-panel/closed-detail and open-CPU-detail phases, then exits that instance. JSON includes actual resource counts, total-machine CPU share, private bytes and handle samples. The test-only scenario host supplies native inventory and is not packaged with the application. No address reveal or screen capture is used.

Record desktop occlusion, hardware and workload with the JSON. Short runs do not establish long-soak stability, unoccluded rendering cost or a resource-count workload the host does not provide. The initial engineering targets remain below 1% total-machine CPU with details closed, below 3% with one detail open and below 200 MiB steady private bytes; measurements must be reported even when they exceed a target. Full qualification includes at least two maximum-retention windows with lifecycle and resource churn, not just an idle process left running.

## Verification boundary

Function-style Pester tests exercise production policy, rates, history, preference recovery, actual Windows CPU/memory sources and compiled UI controls with isolated profiles. Build/package/signing checks remain in the normal test entry point. Desktop integration requires an interactive Windows session. The pixel test uses a small owned black/white underlay, verifies the panel owns the captured region, and inspects only that rectangle; it also accepts an explicitly reported system-policy solid fallback. An active Windows lock screen blocks pixel validation and is reported as a skip, never as proof of alpha rendering.

The current application minimum remains Windows 11 build 22000. Windows 10 build 19044 is a product target pending a matching runtime/packaged test environment. Physical mixed-DPI hot-plug, exclusive fullscreen, sleep/lock, Explorer restart and system-wide accessibility/energy transitions require a designated test session. Tests on the owner's desktop do not modify those settings, restart Explorer, install packages or change certificate trust. A passed build or deterministic policy test does not certify those environment-specific cases.
