# ModernRemote Design System

A design system for **ModernRemote** — a ground-up, .NET 10 / WPF reimagining of
[mRemoteNG](https://mremoteng.org) built on the **Windows 11 Fluent** design language.
It preserves what makes mRemoteNG valuable (tabbed multi-protocol sessions, a
hierarchical connection tree, credential inheritance, `.confCons` import) on a modern,
layered MVVM architecture — and looks like a first-class Windows 11 app.

This repository defines the visual + interaction language for that product:
foundations (color, type, spacing, motion), reusable React component recreations of
the Fluent controls, and a high-fidelity UI kit of the ModernRemote application shell.

---

## Sources & provenance

There was **no codebase, Figma file, or screenshot** attached when this system was
authored. It was derived from:

- The **ModernRemote Engineering Plan** (the product brief: goals, inherited
  behavior from mRemoteNG, target stack — .NET 10, WPF, CommunityToolkit.Mvvm,
  AvalonDock, xterm.js-in-WebView2, Serilog, signed plugin SDK).
- The **Windows 11 / Fluent 2 design language** (WinUI 3 theme resources): neutral
  palette, accent ramp, layered Mica/Layer/Card surfaces, the type ramp, 4px grid,
  corner radii, and Fluent motion curves.

> If you have the real ModernRemote WPF source, the mRemoteNG repo, or a Figma file,
> attach them and we can reconcile exact values (especially fonts and any custom
> brand color) against this system.

### ⚠️ Substitutions to confirm (please send real assets)

| What | We point at | Fallback in use | Action |
|---|---|---|---|
| UI font | `Segoe UI Variable` → `Segoe UI` | host system sans | Real Segoe ships with Windows 11 — renders natively on target. Upload the file (or Microsoft's open **Selawik**) to pin cross-platform rendering. |
| Mono font | `Cascadia Code` / `Cascadia Mono` | system monospace | Open-source (SIL OFL). Upload the woff2 to ship it with the system. |
| Icons | **Fluent System Icons** (MIT), **vendored** to `assets/icons/` | — | Real Microsoft set; the proprietary *Segoe Fluent Icons* system glyphs are not redistributable, so these are the supported open substitute. Now self-hosted (offline-capable); CDN is the fallback. See **Iconography**. |
| Logo | original **ModernRemote** mark (`assets/logo-mark.svg`) | — | A placeholder identity (overlapping session windows). Replace if a real mark exists. |

---

## Content fundamentals (voice & copy)

ModernRemote talks like a **precise, calm IT tool for power users** — not a consumer
app, not chatty.

- **Tone:** confident, terse, technical. Trust the reader knows what RDP, NLA, and a
  gateway are. No marketing fluff, no exclamation points.
- **Person:** address the user as **you** ("Encrypt the connection store at rest").
  The app refers to itself as **ModernRemote**, never "we".
- **Casing:** **Sentence case** everywhere — buttons, titles, menus, settings
  ("New connection", "Save & connect", "Check for updates on launch"). Never Title Case.
- **Protocol & product names** keep their canonical casing: RDP, SSH, VNC, Telnet,
  HTTP/S, `.confCons`, RD Gateway, Network Level Authentication, KeePass, HashiCorp Vault.
- **Numbers & units** are exact and inline: `:3389`, `1920 × 1080`, `AES-256`, `RTT 24 ms`,
  `v1.78`. Use a real `×` for resolutions, not `x`.
- **Verbs are imperative** for actions: Connect, Reconnect, Import, Duplicate, Rename, Delete.
- **Status copy** is short and stateful: "Connected to edge-fw01.corp.local",
  "Connecting to web-02…", "No active session". Ellipsis (`…`) signals in-progress.
- **No emoji.** Iconography carries meaning; emoji never appear in product UI.
- **Security phrasing is plain and reassuring:** "Credentials never persist in plaintext.
  Secrets are held as pinned byte arrays and zeroed after delivery." State the guarantee, not the jargon dump.

---

## Visual foundations

The whole system is **token-driven** (`styles.css` → `tokens/*.css`) and **dual-theme**.
Light is the default; dark lives under `:root[data-theme="dark"]`.

- **Color is restrained.** One brand color: the Windows default **blue `#0078D4`** (with a
  dark/light ramp). The whole ramp now **derives from a single `--accent-base`** via `color-mix`,
  so an accent preset (`data-accent="teal|green|purple|orange|red|pink"`) only sets that one value.
  Everything else is a **neutral** built from layered alpha fills. Color appears only on the *one*
  primary action per view, selection indicators, links, and status. Semantic colors (success green,
  caution amber, critical red) are reserved for state.
- **Surfaces are layered, not boxed.** Backgrounds use real Windows **Mica** (`.mr-mica` — an
  opaque, accent-tinted base with faint noise) and translucent **Acrylic** (`.mr-acrylic` —
  blurred flyout/menu material), defined in `assets/materials.css`, with **Layer** and **Card**
  fills stacked on top. Panels are separated by **hairline dividers** (`--stroke-divider`),
  not heavy borders or big shadows.
- **Type** is the Fluent ramp in Segoe UI Variable: Caption 12 / Body 14 / Body Strong 14·600 /
  Subtitle 20·600 / Title 28·600 up to Display 68. Body 14 is the workhorse. Weight, not size,
  carries most hierarchy. Near-zero letter-spacing.
- **Spacing** is a strict **4px grid** (4/8/12/16/20/24/32…). Controls are **32px** tall
  (24px small); the title bar is 48px; list/tree rows 36px; tab strip 36px.
- **Corner radius** is gentle and consistent: **4px** on controls (buttons, inputs, checkboxes),
  **8px** on overlays (cards, flyouts, dialogs), pill for switches & badges.
- **Elevation** is subtle. Cards = 1px stroke + a whisper of shadow. Flyouts/menus = soft
  `--shadow-flyout` on Acrylic. Dialogs = deep `--shadow-dialog` over a 30%-black scrim.
- **Borders & strokes** do the heavy lifting: inputs carry a slightly stronger **bottom stroke**
  that becomes a **2px accent underline on focus** — the signature Fluent text-field tell.
- **Motion** is quick and functional. Durations 100–333ms; entrances **decelerate**
  `cubic-bezier(.1,.9,.2,1)`, exits **accelerate**. Dialogs fade + scale from 0.96. **No bounce**
  on functional UI; honor `prefers-reduced-motion`.
- **Hover / press / focus.** Hover = a faint fill (`--fill-subtle-secondary` ≈ 3–6% ink);
  press = a fainter fill + text drops to secondary; focus-visible = a 2px high-contrast outline.
  The close caption button is the one place hover turns **critical red**.
- **Imagery.** The product has almost none — its "canvas" is the live remote session
  (an RDP desktop, a dark terminal, a web view). Treat session surfaces as full-bleed content;
  chrome stays neutral around them. The terminal is true black (`#0c0c0c`) with ANSI-style colors.
- **Selection** in lists/trees/nav uses a **vertical accent pill** on the left edge plus a subtle fill —
  never a full-bleed accent row.

### Theme variants & density (`tokens/themes.css`)

Opt-in scopes set on `:root` (or any ancestor), composable with light/dark:
- **Accent presets** — `data-accent="teal|green|purple|orange|red|pink"`. Each sets only
  `--accent-base`; the ramp and every semantic accent fill follow.
- **High contrast** — `data-theme="hc"`: pure black/white values, yellow highlight, cyan hover,
  green disabled, 1px borders carrying all structure; materials flatten. Mirrors Windows HC mode.
- **Compact density** — `data-density="compact"`: control height 32→26px, rows 36→28px, title bar
  48→40px. For power users running large connection trees.

---

## Iconography

- **Set:** Microsoft **Fluent System Icons** (MIT) — the open counterpart to Windows 11's
  proprietary *Segoe Fluent Icons*. Two styles: **regular** (outline, default) and **filled**.
  Native sizes 16 / 20 / 24 / 28 / 48; UI uses **16px** in controls, **20px** in settings rows.
- **Delivery & theming:** ~70 glyphs are **vendored** into `assets/icons/` (`<name>.svg` +
  `<name>_filled.svg`) and rendered through a **CSS mask** (`.mr-icon`, see `assets/icons.css`)
  so they inherit `currentColor` and theme correctly. The `Icon` component resolves from
  `window.MR_ICON_BASE` (the vendored folder) when set, falling back to the jsDelivr CDN
  (`@fluentui/svg-icons`) otherwise — so the system is **offline-capable** but still extensible.
- **No emoji, no Unicode dingbats** as icons. Window caption glyphs (minimize / maximize /
  close) are drawn as simple CSS shapes — they match the OS chrome and don't need an icon.
- **Domain mapping:** `desktop` = RDP/VNC, `code` = SSH/Telnet, `globe` = HTTP/S,
  `folder`/`folder_open` = tree groups, `plug_connected` / `plug_disconnected` = session state,
  `key` = credential, `lock_closed` / `shield*` = security, `puzzle_piece` = plugin.

---

## Index / manifest

**Root**
- `styles.css` — the single entry point consumers link. `@import`s everything below.
- `readme.md` — this guide.
- `SKILL.md` — Agent-Skill front-matter so this system works as a downloadable Claude skill.

**Tokens** (`tokens/`)
- `fonts.css` · `colors.css` (light + dark, accent ramp derived from `--accent-base`) ·
  `typography.css` (ramp) · `spacing.css` (4px grid)
- `elevation.css` (radius + shadow) · `motion.css` (easing + duration) · `base.css` (element
  resets, scrollbars, `color-scheme`)
- `themes.css` — accent presets, high-contrast (`data-theme="hc"`), compact density (`data-density`)

**Assets** (`assets/`)
- `logo-mark.svg` — the ModernRemote app mark
- `icons/` — ~70 vendored Fluent SVG glyphs (regular + filled) · `icons.css` — the `.mr-icon` mask helper
- `materials.css` — real Mica (`.mr-mica`) & Acrylic (`.mr-acrylic`) backdrop materials

**Components** (`components/`, namespace `window.ModernRemoteDesignSystem_e45c88`)
- `components.css` — all control styles (token-driven)
- `core/` — **Icon, Button, IconButton**
- `forms/` — **TextBox, Checkbox, ToggleSwitch, ComboBox, RadioGroup, Slider, NumberBox**
- `surfaces/` — **Card, SettingsCard**
- `disclosure/` — **Expander**
- `feedback/` — **InfoBadge, StatusDot, InfoBar, ProgressRing, ProgressBar**
- `overlays/` — **MenuFlyout, Tooltip**
- `navigation/` — **TabStrip, TreeItem, TreeView**

Each component dir has `<Name>.jsx` + `<Name>.d.ts` + `<Name>.prompt.md` and one
`*.card.html` (`@dsCard group="Components"`). Starting points: Button, TextBox, Card, Expander,
InfoBar, TabStrip, TreeView.

**Foundation cards** (`guidelines/cards/`) — the Design System tab specimens
(Colors, Type, Spacing, Brand groups).

**UI kit** (`ui_kits/modernremote/`)
- `index.html` — the interactive ModernRemote app (connection tree, multi-session tabs,
  RDP / SSH terminal / HTTPS surfaces, Mica shell, light/dark/contrast + accent + density).
- Screens & flows: **New-connection dialog**, tabbed **Connection Properties editor**
  (General/Connection/Display/Gateway/Advanced with inheritance), **Import wizard**
  (.confCons / RDM / PuTTY …), **Port scanner**, **Vault credential picker**, **Settings**,
  **in-session toolbar**, and a **detached floating session window**.
- `kit.css` + `data.jsx` · `controls.jsx` · `chrome.jsx` · `sessions.jsx` · `properties.jsx` ·
  `tools.jsx` · `dialogs.jsx` · `App.jsx`. The kit is **self-contained** (its own Babel-loaded
  copies of the primitives) so it runs standalone, and shares `components.css` with the
  design-system components for pixel parity.

### Notes for consumers
- Cards and the kit set `<meta name="darkreader-lock">` — the Dark Reader extension otherwise
  rewrites the carefully-tuned Fluent palette. Keep it on any page you build with this system.
- Component `@dsCard` thumbnails load the generated `_ds_bundle.js`; they render inside the
  Design System tab. The UI kit does **not** depend on the bundle.
