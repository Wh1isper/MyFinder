# Visual Prototypes

These are **static illustrations of the adopted design baseline**, not application screenshots. Every reading, history series, resource name and capacity is illustrative. No prototype measures the computer. The owner authorized finalizing the design on October 12, 2026; [chapter 08](../08-design-baseline.md) owns exact defaults, dimensions, scope and acceptance targets. Native layout and compatibility remain to be implemented and verified.

Open the PNG images directly; no application, local server or browser tooling is required. Each numbered board also has an editable SVG source beside it. These are presentation assets, not a proposed UI implementation stack.

## Overview

![All seventeen round-two prototype boards](00-overview.png)

## Review Order

1. Start with **04–07**: richer CPU, memory, network and storage inspection, organized around resource navigation, trends, current readings and facts. Board **17** adds logical-processor history, narrow detail reflow and offline inspection.
2. Review **15–16**: simultaneous monitoring, independent compact pin and inspection, then a multi-drive overview with comparable device charts.
3. Compare **01–02** and **13** for footprint. Balanced, Minimal and Capsules are three adopted styles with horizontal/vertical arrangements, not six separate modes. Base sizes are adopted targets; actual content and text scaling can increase the usable minimum.
4. Inspect **14** for consistent system-following light/dark appearance and optional background-only alpha; **09** summarizes the revised controls. **03, 08, 10–12** retain placement, visibility and quality scenarios.

Round two contains **17 numbered PNG boards, 17 editable SVG sources and one PNG contact sheet**. Numbered boards are 1600 × 1000; board 13 explicitly labels its desktop and component presentation scales. Other large detail illustrations are not specifications of native pixel dimensions.

The small-panel sparklines are illustrative shape studies, not synchronized miniature copies of the detailed sample histories. Every figure is sample data. Windows 10 21H2 build 19044+ x64 is the product target, not the current implementation claim: the foundation still requires Windows 11 build 22000+. Native focus, DPI, text scale, fullscreen compatibility, optional materials and performance require implementation evidence.

## Image Catalog

| Board | Review question | Related design |
| --- | --- | --- |
| [01 — Horizontal directions](01-horizontal-directions.png) | Balanced strip, minimal readout, or capsules? | [User surfaces](../01-user-surfaces.md) |
| [02 — Vertical directions](02-vertical-directions.png) | How much context should remain visible? | [User surfaces](../01-user-surfaces.md) |
| [03 — Desktop placement](03-desktop-placement.png) | Where can the panel sit without becoming a dock? | [Placement](../03-placement-and-displays.md) |
| [04 — CPU details](04-cpu-details.png) | Is the glance-to-inspection hierarchy useful? | [Charts and details](../05-charts-and-details.md) |
| [05 — Memory details](05-memory-details.png) | Are composition and recent change both clear? | [Observation meaning](../04-observations-and-history.md) |
| [06 — Network details](06-network-details.png) | Are adapter, rates, totals and gaps unambiguous? | [Charts and details](../05-charts-and-details.md) |
| [07 — Storage details](07-storage-details.png) | Are volume capacity and device activity distinct? | [Storage journey](../05-charts-and-details.md#storage-detail-journey) |
| [08 — Tray and visibility](08-tray-and-visibility.png) | Does the user know why the panel is absent? | [Visibility](../02-visibility.md) |
| [09 — Placement settings](09-settings-placement.png) | Are settings organized around user intent? | [Settings](../06-settings.md) |
| [10 — Fullscreen retreat](10-fullscreen-retreat.png) | Does temporary retreat preserve manual intent? | [Visibility](../02-visibility.md) |
| [11 — Multiple displays](11-multi-display.png) | Are retreat and details local to the right display? | [Placement](../03-placement-and-displays.md) |
| [12 — Data quality](12-data-quality.png) | Are zero, stale, unavailable and missing history distinct? | [Observations](../04-observations-and-history.md) |
| [13 — Footprint and sizing](13-footprint-and-sizing.png) | Is the permanent desktop occupation justified? | [Surface density](../01-user-surfaces.md#footprint-style-and-density) |
| [14 — Theme and transparency](14-theme-and-transparency.png) | Are appearance and foreground readability consistent? | [Compatibility](../06-settings.md#appearance-and-compatibility) |
| [15 — Monitor, pin, inspect](15-monitor-pin-inspect.png) | Are the three resource choices independent? | [Resource choices](../04-observations-and-history.md#monitor-display-and-inspect-are-separate-choices) |
| [16 — Multi-drive overview](16-multi-drive-overview.png) | Can all drives be reviewed without a larger panel? | [Storage details](../05-charts-and-details.md#storage-detail-journey) |
| [17 — Detail states and reflow](17-detail-states-and-reflow.png) | Does depth survive constrained space and missing data? | [Review scenarios](../07-user-journeys.md#second-round-review-scenarios) |

## 01 — Horizontal Directions

![Three horizontal panel alternatives](01-horizontal-directions.png)

## 02 — Vertical Directions

![Three vertical panel alternatives](02-vertical-directions.png)

## 03 — Desktop Placement

![Four-edge placement examples](03-desktop-placement.png)

## 04 — CPU Details

![CPU detail view with recent history](04-cpu-details.png)

## 05 — Memory Details

![Memory composition and recent history](05-memory-details.png)

## 06 — Network Details

![Selected network adapter, rates and missing observations](06-network-details.png)

## 07 — Storage Details

![Volume capacity and device activity](07-storage-details.png)

## 08 — Tray and Visibility

![Shown, manually hidden and fullscreen-retreated tray states](08-tray-and-visibility.png)

## 09 — Placement Settings

![Adopted settings organization](09-settings-placement.png)

## 10 — Fullscreen Retreat

![Fullscreen retreat and manual-hide behavior](10-fullscreen-retreat.png)

## 11 — Multiple Displays

![Display-local retreat and inward details](11-multi-display.png)

## 12 — Data Quality

![Fresh, zero, stale, unavailable, disconnected and partial-history states](12-data-quality.png)

## 13 — Footprint and Sizing

![Scale-labeled desktop occupation and usable minimum](13-footprint-and-sizing.png)

## 14 — Theme and Transparency

![Consistent light and dark appearance and optional background opacity](14-theme-and-transparency.png)

## 15 — Monitor, Pin and Inspect

![Independent monitoring checkboxes, compact pin and detail selection](15-monitor-pin-inspect.png)

## 16 — Multi-Drive Overview

![Per-volume capacities and shared-scale device comparisons](16-multi-drive-overview.png)

## 17 — Detail States and Reflow

![CPU logical histories, narrow memory inspection and disconnected storage](17-detail-states-and-reflow.png)

## Reference Boundary

The organization is informed by Microsoft's historical Task Manager Performance explanation, **Windows 8 Task Manager In-Depth** (June 6, 2013). It is a historical information-hierarchy reference, not evidence of the latest Windows 11 Task Manager UI. All boards are independently drawn; no third-party artwork or source code is included. Source: `https://blogs.windows.com/windowsexperience/2013/06/06/windows-8-task-manager-in-depth/`.

Material considerations were checked against Microsoft's **Acrylic** guidance: `https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic`. The adopted design uses plain alpha backgrounds at 80%–100%, default 100%, with opaque fallbacks; Acrylic/Mica and blur are excluded. The board is not an acrylic implementation or evidence of native support.

[Back to design catalog](../README.md)
