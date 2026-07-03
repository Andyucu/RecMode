/* @ds-bundle: {"format":3,"namespace":"ModernRemoteDesignSystem_e45c88","components":[{"name":"Button","sourcePath":"components/core/Button.jsx"},{"name":"Icon","sourcePath":"components/core/Icon.jsx"},{"name":"IconButton","sourcePath":"components/core/IconButton.jsx"},{"name":"Expander","sourcePath":"components/disclosure/Expander.jsx"},{"name":"InfoBadge","sourcePath":"components/feedback/InfoBadge.jsx"},{"name":"InfoBar","sourcePath":"components/feedback/InfoBar.jsx"},{"name":"ProgressBar","sourcePath":"components/feedback/ProgressBar.jsx"},{"name":"ProgressRing","sourcePath":"components/feedback/ProgressRing.jsx"},{"name":"StatusDot","sourcePath":"components/feedback/StatusDot.jsx"},{"name":"Checkbox","sourcePath":"components/forms/Checkbox.jsx"},{"name":"ComboBox","sourcePath":"components/forms/ComboBox.jsx"},{"name":"NumberBox","sourcePath":"components/forms/NumberBox.jsx"},{"name":"RadioGroup","sourcePath":"components/forms/RadioGroup.jsx"},{"name":"Slider","sourcePath":"components/forms/Slider.jsx"},{"name":"TextBox","sourcePath":"components/forms/TextBox.jsx"},{"name":"ToggleSwitch","sourcePath":"components/forms/ToggleSwitch.jsx"},{"name":"TabStrip","sourcePath":"components/navigation/TabStrip.jsx"},{"name":"TreeItem","sourcePath":"components/navigation/TreeItem.jsx"},{"name":"TreeView","sourcePath":"components/navigation/TreeView.jsx"},{"name":"MenuFlyout","sourcePath":"components/overlays/MenuFlyout.jsx"},{"name":"Tooltip","sourcePath":"components/overlays/Tooltip.jsx"},{"name":"Card","sourcePath":"components/surfaces/Card.jsx"},{"name":"SettingsCard","sourcePath":"components/surfaces/SettingsCard.jsx"}],"sourceHashes":{"components/core/Button.jsx":"c5f7878ae49e","components/core/Icon.jsx":"6019f4ff44d3","components/core/IconButton.jsx":"208489c59518","components/disclosure/Expander.jsx":"31752de77020","components/feedback/InfoBadge.jsx":"7c1432f4ad35","components/feedback/InfoBar.jsx":"8e33b5208934","components/feedback/ProgressBar.jsx":"53bd05521983","components/feedback/ProgressRing.jsx":"9879854e9c29","components/feedback/StatusDot.jsx":"8103943039ee","components/forms/Checkbox.jsx":"8d16aa643f76","components/forms/ComboBox.jsx":"0c596876de6b","components/forms/NumberBox.jsx":"678ff04840e9","components/forms/RadioGroup.jsx":"b410897a0def","components/forms/Slider.jsx":"de05a444ad7c","components/forms/TextBox.jsx":"1b023bf0f6d4","components/forms/ToggleSwitch.jsx":"9c383464d67d","components/navigation/TabStrip.jsx":"93dcb326a90c","components/navigation/TreeItem.jsx":"707831121049","components/navigation/TreeView.jsx":"8a0e45021df7","components/overlays/MenuFlyout.jsx":"55540dde1e29","components/overlays/Tooltip.jsx":"803a8ad40033","components/surfaces/Card.jsx":"05580afdcad2","components/surfaces/SettingsCard.jsx":"173a7bd0003d","ui_kits/modernremote/App.jsx":"41103d345c38","ui_kits/modernremote/chrome.jsx":"295e20b83d9f","ui_kits/modernremote/controls.jsx":"6e90fe6dc270","ui_kits/modernremote/data.jsx":"8281492d8f30","ui_kits/modernremote/dialogs.jsx":"1c7f94db6eb8","ui_kits/modernremote/properties.jsx":"6672783dd17f","ui_kits/modernremote/sessions.jsx":"3fc474b59a63","ui_kits/modernremote/tools.jsx":"b3706e9411db"},"inlinedExternals":[],"unexposedExports":[]} */

(() => {

const __ds_ns = (window.ModernRemoteDesignSystem_e45c88 = window.ModernRemoteDesignSystem_e45c88 || {});

const __ds_scope = {};

(__ds_ns.__errors = __ds_ns.__errors || []);

// components/core/Icon.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const ICON_CDN = "https://cdn.jsdelivr.net/npm/@fluentui/svg-icons@1.1.328/icons/";
const SIZES = [16, 20, 24, 28, 48];

/**
 * Resolve an icon URL. When `window.MR_ICON_BASE` is set (e.g. a vendored
 * `assets/icons/` folder), use size-agnostic `name[_filled].svg` files from
 * there — works offline. Otherwise fall back to the jsDelivr CDN.
 */
function iconUrl(name, px, filled) {
  const base = typeof window !== "undefined" && window.MR_ICON_BASE;
  if (base) return `${base}${name}${filled ? "_filled" : ""}.svg`;
  return `${ICON_CDN}${name}_${px}_${filled ? "filled" : "regular"}.svg`;
}

/**
 * Icon — renders a Microsoft Fluent System Icon (MIT) as a
 * currentColor-tinted glyph via CSS masking, so it themes
 * correctly in light/dark and inherits text color.
 */
function Icon({
  name,
  size = 20,
  filled = false,
  className = "",
  style,
  label,
  ...rest
}) {
  const px = SIZES.includes(size) ? size : 20;
  const url = iconUrl(name, px, filled);
  return /*#__PURE__*/React.createElement("span", _extends({
    className: `mr-icon ${className}`,
    role: label ? "img" : undefined,
    "aria-label": label || undefined,
    "aria-hidden": label ? undefined : true,
    style: {
      fontSize: size,
      "--i": `url('${url}')`,
      ...style
    }
  }, rest));
}
Object.assign(__ds_scope, { Icon });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Icon.jsx", error: String((e && e.message) || e) }); }

// components/core/Button.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Button — Fluent command button.
 * variant: "standard" | "accent" | "subtle" | "hyperlink"
 */
function Button({
  children,
  variant = "standard",
  size = "md",
  icon,
  iconFilled = false,
  iconPosition = "start",
  disabled = false,
  className = "",
  ...rest
}) {
  const cls = ["mr-btn", variant !== "standard" ? `mr-btn--${variant}` : "", size === "sm" ? "mr-btn--sm" : "", disabled ? "mr-btn--disabled" : "", className].filter(Boolean).join(" ");
  const glyph = icon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 16,
    filled: iconFilled
  }) : null;
  return /*#__PURE__*/React.createElement("button", _extends({
    className: cls,
    disabled: disabled
  }, rest), iconPosition === "start" && glyph, children != null && /*#__PURE__*/React.createElement("span", null, children), iconPosition === "end" && glyph);
}
Object.assign(__ds_scope, { Button });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Button.jsx", error: String((e && e.message) || e) }); }

// components/core/IconButton.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * IconButton — square, label-less command button (toolbars, tab close,
 * title-bar actions). tone: "default" | "accent" | "danger".
 */
function IconButton({
  icon,
  iconFilled = false,
  size = 16,
  tone = "default",
  disabled = false,
  label,
  className = "",
  ...rest
}) {
  const cls = ["mr-icon-btn", tone !== "default" ? `mr-icon-btn--${tone}` : "", className].filter(Boolean).join(" ");
  return /*#__PURE__*/React.createElement("button", _extends({
    className: cls,
    disabled: disabled,
    "aria-label": label,
    title: label
  }, rest), /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: size,
    filled: iconFilled
  }));
}
Object.assign(__ds_scope, { IconButton });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/IconButton.jsx", error: String((e && e.message) || e) }); }

// components/disclosure/Expander.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Expander — Fluent disclosure surface. Uncontrolled by default
 * (pass `open` + `onToggle` to control).
 */
function Expander({
  icon,
  title,
  description,
  children,
  defaultOpen = false,
  open,
  onToggle,
  className = "",
  ...rest
}) {
  const [internal, setInternal] = React.useState(defaultOpen);
  const isOpen = open === undefined ? internal : open;
  const toggle = () => onToggle ? onToggle(!isOpen) : setInternal(v => !v);
  return /*#__PURE__*/React.createElement("div", _extends({
    className: `mr-expander ${isOpen ? "mr-expander--open" : ""} ${className}`
  }, rest), /*#__PURE__*/React.createElement("div", {
    className: "mr-expander__header",
    role: "button",
    "aria-expanded": isOpen,
    tabIndex: 0,
    onClick: toggle,
    onKeyDown: e => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        toggle();
      }
    }
  }, icon && /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    className: "mr-expander__icon",
    name: icon,
    size: 20
  }), /*#__PURE__*/React.createElement("div", {
    className: "mr-expander__text"
  }, /*#__PURE__*/React.createElement("div", {
    className: "mr-expander__title"
  }, title), description && /*#__PURE__*/React.createElement("div", {
    className: "mr-expander__desc"
  }, description)), /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    className: "mr-expander__chevron",
    name: "chevron_down",
    size: 16
  })), isOpen && /*#__PURE__*/React.createElement("div", {
    className: "mr-expander__content"
  }, children));
}
Object.assign(__ds_scope, { Expander });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/disclosure/Expander.jsx", error: String((e && e.message) || e) }); }

// components/feedback/InfoBadge.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * InfoBadge — Fluent badge for counts and status accents.
 * variant: "neutral" | "attention" | "success" | "caution" | "critical"
 */
function InfoBadge({
  children,
  variant = "neutral",
  dot = false,
  className = "",
  ...rest
}) {
  const cls = ["mr-badge", variant !== "neutral" ? `mr-badge--${variant}` : "", dot ? "mr-badge--dot" : "", className].filter(Boolean).join(" ");
  return /*#__PURE__*/React.createElement("span", _extends({
    className: cls
  }, rest), dot ? null : children);
}
Object.assign(__ds_scope, { InfoBadge });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/InfoBadge.jsx", error: String((e && e.message) || e) }); }

// components/feedback/InfoBar.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const ICONS = {
  info: "info",
  success: "checkmark_circle",
  caution: "warning",
  critical: "error_circle"
};

/**
 * InfoBar — inline status banner. severity: info | success | caution | critical.
 */
function InfoBar({
  severity = "info",
  title,
  message,
  actions,
  onClose,
  icon,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    className: `mr-infobar ${severity !== "info" ? `mr-infobar--${severity}` : ""} ${className}`,
    role: "status"
  }, rest), /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    className: "mr-infobar__icon",
    name: icon || ICONS[severity],
    size: 20,
    filled: true
  }), /*#__PURE__*/React.createElement("div", {
    className: "mr-infobar__body"
  }, title && /*#__PURE__*/React.createElement("div", {
    className: "mr-infobar__title"
  }, title), message && /*#__PURE__*/React.createElement("div", {
    className: "mr-infobar__msg"
  }, message), actions && /*#__PURE__*/React.createElement("div", {
    className: "mr-infobar__actions"
  }, actions)), onClose && /*#__PURE__*/React.createElement("button", {
    className: "mr-icon-btn",
    "aria-label": "Dismiss",
    onClick: onClose
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "dismiss",
    size: 16
  })));
}
Object.assign(__ds_scope, { InfoBar });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/InfoBar.jsx", error: String((e && e.message) || e) }); }

// components/feedback/ProgressBar.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * ProgressBar — linear progress. Omit `value` for indeterminate; pass 0–100
 * for determinate.
 */
function ProgressBar({
  value,
  className = "",
  style,
  ...rest
}) {
  const indeterminate = value == null;
  const p = indeterminate ? 0 : Math.max(0, Math.min(100, value));
  return /*#__PURE__*/React.createElement("div", _extends({
    className: `mr-pbar ${indeterminate ? "mr-pbar--indeterminate" : ""} ${className}`,
    role: "progressbar",
    "aria-valuenow": indeterminate ? undefined : p,
    style: style
  }, rest), /*#__PURE__*/React.createElement("div", {
    className: "mr-pbar__fill",
    style: indeterminate ? undefined : {
      width: `${p}%`
    }
  }));
}
Object.assign(__ds_scope, { ProgressBar });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/ProgressBar.jsx", error: String((e && e.message) || e) }); }

// components/feedback/ProgressRing.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * ProgressRing — circular progress. Omit `value` for an indeterminate spinner;
 * pass 0–100 for determinate.
 */
function ProgressRing({
  value,
  size = 32,
  className = "",
  style,
  ...rest
}) {
  if (value == null) {
    return /*#__PURE__*/React.createElement("span", _extends({
      className: `mr-ring mr-ring--indeterminate ${className}`,
      role: "progressbar",
      style: {
        width: size,
        height: size,
        ...style
      }
    }, rest));
  }
  const p = Math.max(0, Math.min(100, value));
  return /*#__PURE__*/React.createElement("span", _extends({
    className: `mr-ring ${className}`,
    role: "progressbar",
    "aria-valuenow": p,
    style: {
      width: size,
      height: size,
      ["--p"]: `${p}%`,
      ...style
    }
  }, rest), /*#__PURE__*/React.createElement("span", {
    className: "mr-ring__hole"
  }));
}
Object.assign(__ds_scope, { ProgressRing });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/ProgressRing.jsx", error: String((e && e.message) || e) }); }

// components/feedback/StatusDot.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const TOKENS = {
  connected: "var(--status-connected)",
  connecting: "var(--status-connecting)",
  disconnected: "var(--status-disconnected)",
  error: "var(--status-error)"
};

/**
 * StatusDot — connection-state indicator (ModernRemote domain).
 * status: "connected" | "connecting" | "disconnected" | "error"
 */
function StatusDot({
  status = "disconnected",
  ring = true,
  size = 8,
  className = "",
  style,
  ...rest
}) {
  const color = TOKENS[status] || TOKENS.disconnected;
  const showRing = ring && status !== "disconnected";
  return /*#__PURE__*/React.createElement("span", _extends({
    className: className,
    "aria-label": status,
    style: {
      display: "inline-block",
      width: size,
      height: size,
      borderRadius: "50%",
      flex: "none",
      background: color,
      boxShadow: showRing ? `0 0 0 3px color-mix(in srgb, ${color} 22%, transparent)` : "none",
      ...style
    }
  }, rest));
}
Object.assign(__ds_scope, { StatusDot });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/StatusDot.jsx", error: String((e && e.message) || e) }); }

// components/forms/Checkbox.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/** Checkbox — Fluent selection control. Controlled via `checked`/`onChange`. */
function Checkbox({
  checked = false,
  onChange,
  disabled = false,
  children,
  className = "",
  ...rest
}) {
  const cls = ["mr-checkbox", checked ? "mr-checkbox--checked" : "", disabled ? "mr-checkbox--disabled" : "", className].filter(Boolean).join(" ");
  return /*#__PURE__*/React.createElement("label", _extends({
    className: cls
  }, rest), /*#__PURE__*/React.createElement("span", {
    className: "mr-checkbox__box",
    role: "checkbox",
    "aria-checked": checked,
    tabIndex: disabled ? -1 : 0,
    onClick: () => !disabled && onChange && onChange(!checked),
    onKeyDown: e => {
      if ((e.key === " " || e.key === "Enter") && !disabled) {
        e.preventDefault();
        onChange && onChange(!checked);
      }
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "checkmark",
    size: 16
  })), children != null && /*#__PURE__*/React.createElement("span", null, children));
}
Object.assign(__ds_scope, { Checkbox });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Checkbox.jsx", error: String((e && e.message) || e) }); }

// components/forms/ComboBox.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * ComboBox — Fluent dropdown trigger. Lightweight: renders the
 * selected value + chevron and fires onClick to open a menu/flyout
 * (menu surface supplied by the consumer). For a real <select>,
 * wire onClick to your own popup.
 */
function ComboBox({
  value,
  placeholder = "Select…",
  disabled = false,
  label,
  className = "",
  ...rest
}) {
  const trigger = /*#__PURE__*/React.createElement("button", _extends({
    type: "button",
    className: `mr-combobox ${className}`,
    disabled: disabled
  }, rest), /*#__PURE__*/React.createElement("span", {
    style: {
      color: value ? "inherit" : "var(--fill-text-tertiary)"
    }
  }, value || placeholder), /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "chevron_down",
    size: 16
  }));
  if (!label) return trigger;
  return /*#__PURE__*/React.createElement("label", {
    className: "mr-field"
  }, /*#__PURE__*/React.createElement("span", {
    className: "mr-field__label"
  }, label), trigger);
}
Object.assign(__ds_scope, { ComboBox });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/ComboBox.jsx", error: String((e && e.message) || e) }); }

// components/forms/NumberBox.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/** NumberBox — numeric input with up/down spinners. Controlled. */
function NumberBox({
  value = 0,
  min = -Infinity,
  max = Infinity,
  step = 1,
  onChange,
  className = "",
  ...rest
}) {
  const clamp = v => Math.max(min, Math.min(max, v));
  const bump = d => onChange && onChange(clamp(Number(value) + d * step));
  return /*#__PURE__*/React.createElement("div", _extends({
    className: `mr-numberbox ${className}`
  }, rest), /*#__PURE__*/React.createElement("input", {
    type: "text",
    inputMode: "numeric",
    value: value,
    onChange: e => {
      const n = Number(e.target.value);
      if (!Number.isNaN(n)) onChange && onChange(n);else onChange && onChange(e.target.value);
    }
  }), /*#__PURE__*/React.createElement("div", {
    className: "mr-numberbox__btns"
  }, /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "mr-numberbox__btn",
    "aria-label": "Increase",
    onClick: () => bump(1)
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "chevron_up",
    size: 12
  })), /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "mr-numberbox__btn",
    "aria-label": "Decrease",
    onClick: () => bump(-1)
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "chevron_down",
    size: 12
  }))));
}
Object.assign(__ds_scope, { NumberBox });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/NumberBox.jsx", error: String((e && e.message) || e) }); }

// components/forms/RadioGroup.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * RadioGroup — single-select list. options: [{ value, label, disabled }].
 * Controlled via `value`/`onChange`.
 */
function RadioGroup({
  options = [],
  value,
  onChange,
  name,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    role: "radiogroup",
    className: className,
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 10
    }
  }, rest), options.map(o => {
    const checked = o.value === value;
    return /*#__PURE__*/React.createElement("label", {
      key: o.value,
      className: `mr-radio ${checked ? "mr-radio--checked" : ""} ${o.disabled ? "mr-radio--disabled" : ""}`
    }, /*#__PURE__*/React.createElement("span", {
      className: "mr-radio__dot",
      role: "radio",
      "aria-checked": checked,
      tabIndex: o.disabled ? -1 : 0,
      onClick: () => !o.disabled && onChange && onChange(o.value),
      onKeyDown: e => {
        if ((e.key === " " || e.key === "Enter") && !o.disabled) {
          e.preventDefault();
          onChange && onChange(o.value);
        }
      }
    }), /*#__PURE__*/React.createElement("span", null, o.label));
  }));
}
Object.assign(__ds_scope, { RadioGroup });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/RadioGroup.jsx", error: String((e && e.message) || e) }); }

// components/forms/Slider.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/** Slider — Fluent range control. Controlled via `value`/`onChange`. */
function Slider({
  value = 50,
  min = 0,
  max = 100,
  step = 1,
  onChange,
  className = "",
  style,
  ...rest
}) {
  const ref = React.useRef(null);
  const pct = (value - min) / (max - min) * 100;
  const setFromClientX = clientX => {
    const el = ref.current;
    if (!el) return;
    const r = el.getBoundingClientRect();
    let t = (clientX - r.left) / r.width;
    t = Math.max(0, Math.min(1, t));
    let v = min + t * (max - min);
    v = Math.round(v / step) * step;
    onChange && onChange(Math.max(min, Math.min(max, v)));
  };
  const onDown = e => {
    setFromClientX(e.clientX);
    const move = ev => setFromClientX(ev.clientX);
    const up = () => {
      window.removeEventListener("mousemove", move);
      window.removeEventListener("mouseup", up);
    };
    window.addEventListener("mousemove", move);
    window.addEventListener("mouseup", up);
  };
  return /*#__PURE__*/React.createElement("div", _extends({
    className: `mr-slider ${className}`,
    ref: ref,
    onMouseDown: onDown,
    role: "slider",
    "aria-valuenow": value,
    "aria-valuemin": min,
    "aria-valuemax": max,
    tabIndex: 0,
    onKeyDown: e => {
      if (e.key === "ArrowRight" || e.key === "ArrowUp") onChange && onChange(Math.min(max, value + step));
      if (e.key === "ArrowLeft" || e.key === "ArrowDown") onChange && onChange(Math.max(min, value - step));
    },
    style: style
  }, rest), /*#__PURE__*/React.createElement("div", {
    className: "mr-slider__track"
  }, /*#__PURE__*/React.createElement("div", {
    className: "mr-slider__fill",
    style: {
      width: `${pct}%`
    }
  }), /*#__PURE__*/React.createElement("div", {
    className: "mr-slider__thumb",
    style: {
      left: `${pct}%`
    }
  })));
}
Object.assign(__ds_scope, { Slider });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Slider.jsx", error: String((e && e.message) || e) }); }

// components/forms/TextBox.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * TextBox — Fluent single-line text input with the signature
 * bottom accent underline on focus. Optional leading icon.
 */
function TextBox({
  value,
  onChange,
  placeholder,
  type = "text",
  icon,
  disabled = false,
  label,
  className = "",
  inputProps = {},
  ...rest
}) {
  const box = /*#__PURE__*/React.createElement("div", _extends({
    className: `mr-textbox ${disabled ? "mr-textbox--disabled" : ""} ${className}`
  }, rest), icon && /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 16
  }), /*#__PURE__*/React.createElement("input", _extends({
    type: type,
    value: value,
    onChange: onChange,
    placeholder: placeholder,
    disabled: disabled
  }, inputProps)));
  if (!label) return box;
  return /*#__PURE__*/React.createElement("label", {
    className: "mr-field"
  }, /*#__PURE__*/React.createElement("span", {
    className: "mr-field__label"
  }, label), box);
}
Object.assign(__ds_scope, { TextBox });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/TextBox.jsx", error: String((e && e.message) || e) }); }

// components/forms/ToggleSwitch.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/** ToggleSwitch — Fluent on/off switch. Controlled via `checked`/`onChange`. */
function ToggleSwitch({
  checked = false,
  onChange,
  disabled = false,
  children,
  className = "",
  ...rest
}) {
  const cls = ["mr-switch", checked ? "mr-switch--on" : "", disabled ? "mr-switch--disabled" : "", className].filter(Boolean).join(" ");
  return /*#__PURE__*/React.createElement("button", _extends({
    type: "button",
    role: "switch",
    "aria-checked": checked,
    className: cls,
    disabled: disabled,
    onClick: () => !disabled && onChange && onChange(!checked)
  }, rest), /*#__PURE__*/React.createElement("span", {
    className: "mr-switch__track"
  }, /*#__PURE__*/React.createElement("span", {
    className: "mr-switch__thumb"
  })), children != null && /*#__PURE__*/React.createElement("span", null, children));
}
Object.assign(__ds_scope, { ToggleSwitch });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/ToggleSwitch.jsx", error: String((e && e.message) || e) }); }

// components/navigation/TabStrip.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * TabStrip — Windows 11 TabView for multi-session tabs.
 * tabs: [{ id, label, icon, status }]
 */
function TabStrip({
  tabs = [],
  activeId,
  onSelect,
  onClose,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    className: `mr-tabs ${className}`,
    role: "tablist"
  }, rest), tabs.map(t => {
    const active = t.id === activeId;
    return /*#__PURE__*/React.createElement("div", {
      key: t.id,
      role: "tab",
      "aria-selected": active,
      className: `mr-tab ${active ? "mr-tab--active" : ""}`,
      onClick: () => onSelect && onSelect(t.id)
    }, t.status ? /*#__PURE__*/React.createElement(__ds_scope.StatusDot, {
      status: t.status,
      size: 8
    }) : t.icon ? /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: t.icon,
      size: 16
    }) : null, /*#__PURE__*/React.createElement("span", {
      className: "mr-tab__label"
    }, t.label), onClose && /*#__PURE__*/React.createElement("span", {
      className: "mr-tab__close",
      role: "button",
      "aria-label": "Close tab",
      onClick: e => {
        e.stopPropagation();
        onClose(t.id);
      }
    }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
      name: "dismiss",
      size: 12
    })));
  }));
}
Object.assign(__ds_scope, { TabStrip });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/TabStrip.jsx", error: String((e && e.message) || e) }); }

// components/navigation/TreeItem.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * TreeItem — one row of the hierarchical connection tree. Compose many
 * (with increasing `depth`) to build the tree; supply your own children
 * rendering for expanded folders.
 */
function TreeItem({
  label,
  icon = "folder",
  iconFilled = false,
  depth = 0,
  open = false,
  selected = false,
  hasChildren = false,
  status,
  onToggle,
  onSelect,
  className = "",
  ...rest
}) {
  const cls = ["mr-tree-item", selected ? "mr-tree-item--selected" : "", open ? "mr-tree-item--open" : "", className].filter(Boolean).join(" ");
  return /*#__PURE__*/React.createElement("div", _extends({
    className: cls,
    style: {
      paddingLeft: 4 + depth * 18
    },
    onClick: () => onSelect && onSelect()
  }, rest), /*#__PURE__*/React.createElement("span", {
    className: "mr-tree-item__chevron",
    onClick: e => {
      e.stopPropagation();
      onToggle && onToggle();
    },
    style: {
      visibility: hasChildren ? "visible" : "hidden"
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "chevron_right",
    size: 16
  })), /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    className: "mr-tree-item__icon",
    name: icon,
    size: 16,
    filled: iconFilled
  }), /*#__PURE__*/React.createElement("span", {
    className: "mr-tree-item__label"
  }, label), status && /*#__PURE__*/React.createElement(__ds_scope.StatusDot, {
    className: "mr-tree-item__status",
    status: status,
    size: 8
  }));
}
Object.assign(__ds_scope, { TreeItem });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/TreeItem.jsx", error: String((e && e.message) || e) }); }

// components/navigation/TreeView.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * TreeView — renders a hierarchical connection tree from data and manages
 * expand/select state internally.
 * nodes: [{ id, label, icon, iconFilled, status, children?: [] }]
 */
function TreeView({
  nodes = [],
  defaultOpen = [],
  selectedId,
  onSelect,
  onActivate,
  className = "",
  ...rest
}) {
  const [open, setOpen] = React.useState(() => new Set(defaultOpen));
  const [internalSel, setInternalSel] = React.useState(null);
  const sel = selectedId === undefined ? internalSel : selectedId;
  const toggle = id => setOpen(s => {
    const n = new Set(s);
    n.has(id) ? n.delete(id) : n.add(id);
    return n;
  });
  const select = node => {
    setInternalSel(node.id);
    onSelect && onSelect(node);
  };
  const render = (list, depth) => list.map(node => {
    const hasChildren = Array.isArray(node.children) && node.children.length > 0;
    const isOpen = open.has(node.id);
    return /*#__PURE__*/React.createElement(React.Fragment, {
      key: node.id
    }, /*#__PURE__*/React.createElement(__ds_scope.TreeItem, {
      label: node.label,
      icon: node.icon || (hasChildren ? isOpen ? "folder_open" : "folder" : "desktop"),
      iconFilled: node.iconFilled || hasChildren && isOpen,
      depth: depth,
      open: isOpen,
      selected: sel === node.id,
      hasChildren: hasChildren,
      status: node.status,
      onToggle: () => toggle(node.id),
      onSelect: () => select(node),
      onDoubleClick: () => !hasChildren && onActivate && onActivate(node)
    }), hasChildren && isOpen && render(node.children, depth + 1));
  });
  return /*#__PURE__*/React.createElement("div", _extends({
    className: className,
    role: "tree"
  }, rest), render(nodes, 0));
}
Object.assign(__ds_scope, { TreeView });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/TreeView.jsx", error: String((e && e.message) || e) }); }

// components/overlays/MenuFlyout.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * MenuFlyout — Fluent acrylic menu. Render `items`:
 *   { label, icon, shortcut, danger, separator, onClick }
 * Position it yourself (absolute/fixed) via `style`.
 */
function MenuFlyout({
  items = [],
  onClose,
  className = "",
  style,
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    className: `mr-menu ${className}`,
    role: "menu",
    style: style
  }, rest), items.map((it, i) => it.separator ? /*#__PURE__*/React.createElement("div", {
    key: i,
    className: "mr-menu__sep"
  }) : /*#__PURE__*/React.createElement("div", {
    key: i,
    role: "menuitem",
    className: `mr-menu__item ${it.danger ? "mr-menu__item--danger" : ""}`,
    onClick: () => {
      it.onClick && it.onClick();
      onClose && onClose();
    }
  }, it.icon && /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: it.icon,
    size: 16
  }), /*#__PURE__*/React.createElement("span", null, it.label), it.shortcut && /*#__PURE__*/React.createElement("span", {
    className: "mr-menu__item__shortcut"
  }, it.shortcut))));
}
Object.assign(__ds_scope, { MenuFlyout });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/overlays/MenuFlyout.jsx", error: String((e && e.message) || e) }); }

// components/overlays/Tooltip.jsx
try { (() => {
/**
 * Tooltip — wraps a single child and shows a Fluent tooltip on hover/focus.
 * Lightweight (top-positioned). For complex placement, use your own popover.
 */
function Tooltip({
  content,
  children,
  className = ""
}) {
  const [show, setShow] = React.useState(false);
  const ref = React.useRef(null);
  return /*#__PURE__*/React.createElement("span", {
    ref: ref,
    style: {
      position: "relative",
      display: "inline-flex"
    },
    onMouseEnter: () => setShow(true),
    onMouseLeave: () => setShow(false),
    onFocus: () => setShow(true),
    onBlur: () => setShow(false)
  }, children, show && content && /*#__PURE__*/React.createElement("span", {
    className: `mr-tooltip ${className}`,
    role: "tooltip",
    style: {
      bottom: "calc(100% + 6px)",
      left: "50%",
      transform: "translateX(-50%)"
    }
  }, content));
}
Object.assign(__ds_scope, { Tooltip });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/overlays/Tooltip.jsx", error: String((e && e.message) || e) }); }

// components/surfaces/Card.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/** Card — Fluent layered surface container. */
function Card({
  title,
  description,
  children,
  flat = false,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    className: `mr-card ${flat ? "mr-card--flat" : ""} ${className}`
  }, rest), title && /*#__PURE__*/React.createElement("div", {
    className: "mr-card__title"
  }, title), description && /*#__PURE__*/React.createElement("div", {
    className: "mr-card__desc"
  }, description), children);
}
Object.assign(__ds_scope, { Card });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/surfaces/Card.jsx", error: String((e && e.message) || e) }); }

// components/surfaces/SettingsCard.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * SettingsCard — the iconic Fluent settings row: leading icon,
 * title + description, and a trailing action slot (switch, combobox,
 * button, …). Used throughout the Settings surface.
 */
function SettingsCard({
  icon,
  iconFilled = false,
  title,
  description,
  action,
  className = "",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    className: `mr-settings-card ${className}`
  }, rest), icon && /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    className: "mr-settings-card__icon",
    name: icon,
    size: 20,
    filled: iconFilled
  }), /*#__PURE__*/React.createElement("div", {
    className: "mr-settings-card__body"
  }, /*#__PURE__*/React.createElement("div", {
    className: "mr-settings-card__title"
  }, title), description && /*#__PURE__*/React.createElement("div", {
    className: "mr-settings-card__desc"
  }, description)), action);
}
Object.assign(__ds_scope, { SettingsCard });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/surfaces/SettingsCard.jsx", error: String((e && e.message) || e) }); }

// ui_kits/modernremote/App.jsx
try { (() => {
/* ModernRemote kit — top-level App. */
const {
  useState: uS,
  useEffect: uE,
  useRef
} = React;
function applyTheme(theme) {
  const root = document.documentElement;
  let mode = theme;
  if (theme === "system") {
    mode = window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  }
  if (mode === "dark") root.setAttribute("data-theme", "dark");else if (mode === "hc") root.setAttribute("data-theme", "hc");else root.removeAttribute("data-theme");
}
let SID = 100;
function App() {
  const [theme, setThemeState] = uS("light");
  const [accent, setAccent] = uS("blue");
  const [density, setDensity] = uS("comfortable");
  const [tree, setTree] = uS(TREE);
  const [sessions, setSessions] = uS(SESSIONS);
  const [activeId, setActiveId] = uS("s1");
  const [selected, setSelected] = uS("c-edge");
  const [dialog, setDialog] = uS(null);
  const [showSettings, setShowSettings] = uS(false);
  const [quick, setQuick] = uS("");
  const [filter, setFilter] = uS("");
  const [menu, setMenu] = uS(null);
  const [propNode, setPropNode] = uS(null);
  const [detached, setDetached] = uS(null);
  const prefillRef = useRef("");
  uE(() => {
    applyTheme(theme);
  }, [theme]);
  uE(() => {
    const r = document.documentElement;
    if (accent === "blue") r.removeAttribute("data-accent");else r.setAttribute("data-accent", accent);
  }, [accent]);
  uE(() => {
    const r = document.documentElement;
    if (density === "compact") r.setAttribute("data-density", "compact");else r.removeAttribute("data-density");
  }, [density]);
  // Promote any initially-connecting sessions to connected (simulated handshake)
  uE(() => {
    SESSIONS.forEach(s => {
      if (s.status === "connecting") promote(s.id);
    });
  }, []);
  const setTheme = t => setThemeState(t);
  const toggleTheme = () => setThemeState(t => t === "dark" ? "light" : "dark");
  const active = sessions.find(s => s.id === activeId) || null;
  function promote(sessionId) {
    // simulate connecting -> connected
    setTimeout(() => {
      setSessions(ss => ss.map(s => s.id === sessionId && s.status === "connecting" ? {
        ...s,
        status: "connected"
      } : s));
    }, 1400);
  }
  function openSession({
    name,
    host,
    protocol
  }, connId) {
    const existing = connId && sessions.find(s => s.connId === connId);
    if (existing) {
      setActiveId(existing.id);
      return;
    }
    const id = "s" + ++SID;
    const status = "connecting";
    setSessions(ss => [...ss, {
      id,
      connId,
      name,
      host,
      protocol,
      status
    }]);
    setActiveId(id);
    promote(id);
  }
  function toggleFolder(id) {
    setTree(t => t.map(n => n.id === id ? {
      ...n,
      open: !n.open
    } : n));
  }
  function connectNode(node) {
    openSession(node, node.id);
  }
  function closeTab(id) {
    setSessions(ss => {
      const idx = ss.findIndex(s => s.id === id);
      const next = ss.filter(s => s.id !== id);
      if (id === activeId) setActiveId(next.length ? next[Math.max(0, idx - 1)].id : null);
      return next;
    });
  }
  function quickConnect(text) {
    openSession({
      name: text,
      host: text,
      protocol: "RDP"
    });
    setQuick("");
  }
  function createConnection(data) {
    setDialog(null);
    openSession(data);
  }
  function onContext(e, node) {
    e.preventDefault();
    setMenu({
      x: e.clientX,
      y: e.clientY,
      node
    });
  }

  // close context menu on global click
  uE(() => {
    if (!menu) return;
    const h = () => setMenu(null);
    window.addEventListener("click", h);
    return () => window.removeEventListener("click", h);
  }, [menu]);
  const tabs = sessions.map(s => ({
    ...s
  }));
  return /*#__PURE__*/React.createElement("div", {
    className: "k-app mr-mica"
  }, /*#__PURE__*/React.createElement(TitleBar, {
    onMin: () => {},
    onMax: () => {},
    onClose: () => {}
  }), /*#__PURE__*/React.createElement(Toolbar, {
    quick: quick,
    setQuick: setQuick,
    onQuickConnect: quickConnect,
    onNew: () => {
      prefillRef.current = "";
      setDialog("new");
    },
    theme: theme,
    toggleTheme: toggleTheme,
    onSettings: () => setShowSettings(true),
    onImport: () => setDialog("import"),
    onScan: () => setDialog("portscan"),
    onVault: () => setDialog("vault")
  }), /*#__PURE__*/React.createElement("div", {
    className: "k-body"
  }, /*#__PURE__*/React.createElement(Sidebar, {
    tree: tree,
    selectedId: selected,
    onSelect: setSelected,
    onConnect: connectNode,
    onToggle: toggleFolder,
    onNew: () => setDialog("new"),
    onContext: onContext,
    filter: filter,
    setFilter: setFilter
  }), /*#__PURE__*/React.createElement("div", {
    className: "k-pane"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-pane__tabs"
  }, /*#__PURE__*/React.createElement("div", {
    className: "mr-tabs",
    role: "tablist"
  }, tabs.map(t => /*#__PURE__*/React.createElement("div", {
    key: t.id,
    role: "tab",
    "aria-selected": t.id === activeId,
    className: `mr-tab ${t.id === activeId ? "mr-tab--active" : ""}`,
    onClick: () => setActiveId(t.id)
  }, /*#__PURE__*/React.createElement(StatusDot, {
    status: t.status,
    size: 8
  }), /*#__PURE__*/React.createElement("span", {
    className: "mr-tab__label"
  }, t.name, " \u2014 ", t.protocol), /*#__PURE__*/React.createElement("span", {
    className: "mr-tab__close",
    role: "button",
    "aria-label": "Close",
    onClick: e => {
      e.stopPropagation();
      closeTab(t.id);
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "dismiss",
    size: 12
  })))), /*#__PURE__*/React.createElement("button", {
    className: "mr-icon-btn",
    style: {
      marginLeft: 4,
      alignSelf: "center"
    },
    "aria-label": "New tab",
    onClick: () => setDialog("new")
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "add",
    size: 16
  })))), /*#__PURE__*/React.createElement("div", {
    className: "k-pane__body"
  }, /*#__PURE__*/React.createElement(SessionArea, {
    session: active,
    onNew: () => setDialog("new")
  }), active && active.status === "connected" && (active.protocol === "RDP" || active.protocol === "VNC") && !showSettings && /*#__PURE__*/React.createElement(SessionToolbar, {
    onDetach: () => setDetached(active),
    onDisconnect: () => closeTab(active.id)
  }), detached && /*#__PURE__*/React.createElement(DetachedWindow, {
    session: detached,
    onClose: () => setDetached(null)
  }), showSettings && /*#__PURE__*/React.createElement(SettingsPanel, {
    onClose: () => setShowSettings(false),
    theme: theme,
    setTheme: setTheme,
    accent: accent,
    setAccent: setAccent,
    density: density,
    setDensity: setDensity
  })))), /*#__PURE__*/React.createElement(StatusBar, {
    session: active
  }), dialog === "new" && /*#__PURE__*/React.createElement(NewConnectionDialog, {
    onClose: () => setDialog(null),
    onCreate: createConnection,
    prefillHost: prefillRef.current
  }), dialog === "properties" && /*#__PURE__*/React.createElement(ConnectionProperties, {
    conn: propNode,
    onClose: () => setDialog(null)
  }), dialog === "import" && /*#__PURE__*/React.createElement(ImportWizard, {
    onClose: () => setDialog(null),
    onDone: () => setDialog(null)
  }), dialog === "portscan" && /*#__PURE__*/React.createElement(PortScanner, {
    onClose: () => setDialog(null)
  }), dialog === "vault" && /*#__PURE__*/React.createElement(VaultPicker, {
    onClose: () => setDialog(null),
    onPick: () => setDialog(null)
  }), menu && /*#__PURE__*/React.createElement("div", {
    className: "k-menu",
    style: {
      left: menu.x,
      top: menu.y
    },
    onClick: e => e.stopPropagation()
  }, menu.node.type !== "folder" && /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    className: "k-menu__item",
    onClick: () => {
      connectNode(menu.node);
      setMenu(null);
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "plug_connected",
    size: 16
  }), "Connect", /*#__PURE__*/React.createElement("span", {
    className: "mr-menu__item__shortcut"
  }, "Enter")), /*#__PURE__*/React.createElement("div", {
    className: "k-menu__item",
    onClick: () => {
      setDetached(menu.node.protocol ? {
        id: "d",
        name: menu.node.name,
        host: menu.node.host,
        protocol: menu.node.protocol,
        status: "connected"
      } : null);
      setMenu(null);
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "window_new",
    size: 16
  }), "Connect in new window"), /*#__PURE__*/React.createElement("div", {
    className: "k-menu__sep"
  })), /*#__PURE__*/React.createElement("div", {
    className: "k-menu__item",
    onClick: () => {
      setDialog("new");
      setMenu(null);
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "add",
    size: 16
  }), "New connection"), /*#__PURE__*/React.createElement("div", {
    className: "k-menu__item",
    onClick: () => setMenu(null)
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "copy",
    size: 16
  }), "Duplicate"), /*#__PURE__*/React.createElement("div", {
    className: "k-menu__item",
    onClick: () => setMenu(null)
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "rename",
    size: 16
  }), "Rename", /*#__PURE__*/React.createElement("span", {
    className: "mr-menu__item__shortcut"
  }, "F2")), /*#__PURE__*/React.createElement("div", {
    className: "k-menu__sep"
  }), /*#__PURE__*/React.createElement("div", {
    className: "k-menu__item",
    onClick: () => {
      setPropNode(menu.node);
      setDialog("properties");
      setMenu(null);
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "settings",
    size: 16
  }), "Properties"), /*#__PURE__*/React.createElement("div", {
    className: "k-menu__item mr-menu__item--danger",
    onClick: () => setMenu(null)
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "delete",
    size: 16,
    style: {
      color: "var(--fill-system-critical)"
    }
  }), "Delete")));
}
Object.assign(window, {
  App
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/modernremote/App.jsx", error: String((e && e.message) || e) }); }

// ui_kits/modernremote/chrome.jsx
try { (() => {
/* ModernRemote kit — window chrome: title bar, toolbar, sidebar, status bar. */

function TitleBar({
  onMin,
  onMax,
  onClose
}) {
  return /*#__PURE__*/React.createElement("div", {
    className: "k-titlebar"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-titlebar__brand"
  }, /*#__PURE__*/React.createElement("img", {
    src: "../../assets/logo-mark.svg",
    width: "22",
    height: "22",
    alt: ""
  }), /*#__PURE__*/React.createElement("span", {
    className: "k-titlebar__title"
  }, "Modern", /*#__PURE__*/React.createElement("b", null, "Remote"))), /*#__PURE__*/React.createElement("div", {
    className: "k-caption",
    style: {
      WebkitAppRegion: "no-drag"
    }
  }, /*#__PURE__*/React.createElement("button", {
    className: "k-cap k-cap--min",
    onClick: onMin,
    "aria-label": "Minimize"
  }, /*#__PURE__*/React.createElement("span", {
    className: "k-cap__glyph"
  })), /*#__PURE__*/React.createElement("button", {
    className: "k-cap k-cap--max",
    onClick: onMax,
    "aria-label": "Maximize"
  }, /*#__PURE__*/React.createElement("span", {
    className: "k-cap__glyph"
  })), /*#__PURE__*/React.createElement("button", {
    className: "k-cap k-cap--close",
    onClick: onClose,
    "aria-label": "Close"
  }, /*#__PURE__*/React.createElement("span", {
    className: "k-cap__glyph"
  }))));
}
function Toolbar({
  quick,
  setQuick,
  onQuickConnect,
  onNew,
  theme,
  toggleTheme,
  onSettings,
  onImport,
  onScan,
  onVault
}) {
  return /*#__PURE__*/React.createElement("div", {
    className: "k-toolbar"
  }, /*#__PURE__*/React.createElement("div", {
    className: "mr-textbox",
    onKeyDown: e => {
      if (e.key === "Enter" && quick.trim()) onQuickConnect(quick.trim());
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "search",
    size: 16
  }), /*#__PURE__*/React.createElement("input", {
    value: quick,
    onChange: e => setQuick(e.target.value),
    placeholder: "Quick connect \u2014 host or name\u2026"
  })), /*#__PURE__*/React.createElement(Btn, {
    variant: "accent",
    icon: "add",
    onClick: onNew
  }, "New connection"), /*#__PURE__*/React.createElement("div", {
    className: "k-divider-v"
  }), /*#__PURE__*/React.createElement(IconBtn, {
    icon: "folder_arrow_right",
    label: "Import connections\u2026",
    onClick: onImport
  }), /*#__PURE__*/React.createElement(IconBtn, {
    icon: "pulse",
    label: "Port scanner",
    onClick: onScan
  }), /*#__PURE__*/React.createElement(IconBtn, {
    icon: "key",
    label: "Credential vault",
    onClick: onVault
  }), /*#__PURE__*/React.createElement(IconBtn, {
    icon: "arrow_clockwise",
    label: "Reconnect all"
  }), /*#__PURE__*/React.createElement("div", {
    className: "k-toolbar__spacer"
  }), /*#__PURE__*/React.createElement(IconBtn, {
    icon: theme === "dark" ? "weather_sunny" : "weather_moon",
    label: "Toggle theme",
    onClick: toggleTheme
  }), /*#__PURE__*/React.createElement(IconBtn, {
    icon: "settings",
    label: "Settings",
    onClick: onSettings
  }));
}
function TreeNode({
  node,
  depth,
  selectedId,
  onSelect,
  onConnect,
  onToggle,
  onContext
}) {
  if (node.type === "folder") {
    return /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
      className: `mr-tree-item ${selectedId === node.id ? "mr-tree-item--selected" : ""} ${node.open ? "mr-tree-item--open" : ""}`,
      style: {
        paddingLeft: 4 + depth * 18
      },
      onClick: () => onSelect(node.id),
      onContextMenu: e => onContext(e, node)
    }, /*#__PURE__*/React.createElement("span", {
      className: "mr-tree-item__chevron",
      onClick: e => {
        e.stopPropagation();
        onToggle(node.id);
      }
    }, /*#__PURE__*/React.createElement(Icon, {
      name: "chevron_right",
      size: 16
    })), /*#__PURE__*/React.createElement(Icon, {
      className: "mr-tree-item__icon",
      name: node.open ? "folder_open" : "folder",
      size: 16,
      filled: node.open
    }), /*#__PURE__*/React.createElement("span", {
      className: "mr-tree-item__label"
    }, node.name)), node.open && node.children.map(c => /*#__PURE__*/React.createElement(TreeNode, {
      key: c.id,
      node: c,
      depth: depth + 1,
      selectedId: selectedId,
      onSelect: onSelect,
      onConnect: onConnect,
      onToggle: onToggle,
      onContext: onContext
    })));
  }
  const proto = PROTOCOLS[node.protocol] || {};
  return /*#__PURE__*/React.createElement("div", {
    className: `mr-tree-item ${selectedId === node.id ? "mr-tree-item--selected" : ""}`,
    style: {
      paddingLeft: 4 + depth * 18
    },
    onClick: () => onSelect(node.id),
    onDoubleClick: () => onConnect(node),
    onContextMenu: e => onContext(e, node),
    title: `${node.protocol} · ${node.host}`
  }, /*#__PURE__*/React.createElement("span", {
    className: "mr-tree-item__chevron",
    style: {
      visibility: "hidden"
    }
  }), /*#__PURE__*/React.createElement(Icon, {
    className: "mr-tree-item__icon",
    name: proto.icon || "desktop",
    size: 16
  }), /*#__PURE__*/React.createElement("span", {
    className: "mr-tree-item__label"
  }, node.name), /*#__PURE__*/React.createElement(StatusDot, {
    status: node.status,
    size: 8
  }));
}
function Sidebar({
  tree,
  selectedId,
  onSelect,
  onConnect,
  onToggle,
  onNew,
  onContext,
  filter,
  setFilter
}) {
  return /*#__PURE__*/React.createElement("div", {
    className: "k-sidebar"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-sidebar__head"
  }, /*#__PURE__*/React.createElement("span", {
    className: "k-sidebar__title"
  }, "Connections"), /*#__PURE__*/React.createElement(IconBtn, {
    icon: "add",
    label: "New connection",
    onClick: onNew
  }), /*#__PURE__*/React.createElement(IconBtn, {
    icon: "chevron_up_down",
    label: "Collapse all"
  })), /*#__PURE__*/React.createElement("div", {
    className: "k-sidebar__search"
  }, /*#__PURE__*/React.createElement("div", {
    className: "mr-textbox"
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "search",
    size: 16
  }), /*#__PURE__*/React.createElement("input", {
    value: filter,
    onChange: e => setFilter(e.target.value),
    placeholder: "Filter connections\u2026"
  }))), /*#__PURE__*/React.createElement("div", {
    className: "k-tree mr-scroll"
  }, tree.map(n => /*#__PURE__*/React.createElement(TreeNode, {
    key: n.id,
    node: n,
    depth: 0,
    selectedId: selectedId,
    onSelect: onSelect,
    onConnect: onConnect,
    onToggle: onToggle,
    onContext: onContext
  }))));
}
function StatusBar({
  session
}) {
  const proto = session ? PROTOCOLS[session.protocol] : null;
  return /*#__PURE__*/React.createElement("div", {
    className: "k-statusbar"
  }, session ? /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("span", {
    className: "k-statusbar__item"
  }, /*#__PURE__*/React.createElement(StatusDot, {
    status: session.status,
    size: 8
  }), session.status === "connected" ? `Connected to ${session.host}` : session.status === "connecting" ? `Connecting to ${session.host}…` : session.host), /*#__PURE__*/React.createElement("div", {
    className: "k-divider-v"
  }), /*#__PURE__*/React.createElement("span", {
    className: "k-statusbar__item"
  }, session.protocol, proto ? ` · :${proto.port}` : ""), session.protocol === "RDP" && /*#__PURE__*/React.createElement("span", {
    className: "k-statusbar__item"
  }, "1920 \xD7 1080"), /*#__PURE__*/React.createElement("span", {
    className: "k-statusbar__spacer"
  }), /*#__PURE__*/React.createElement("span", {
    className: "k-statusbar__item"
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "lock_closed",
    size: 14
  }), "AES-256"), /*#__PURE__*/React.createElement("span", {
    className: "k-statusbar__item"
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "pulse",
    size: 14
  }), "RTT 24 ms")) : /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("span", {
    className: "k-statusbar__item"
  }, /*#__PURE__*/React.createElement(StatusDot, {
    status: "disconnected",
    size: 8
  }), "No active session"), /*#__PURE__*/React.createElement("span", {
    className: "k-statusbar__spacer"
  }), /*#__PURE__*/React.createElement("span", {
    className: "k-statusbar__item"
  }, "9 connections")));
}
Object.assign(window, {
  TitleBar,
  Toolbar,
  Sidebar,
  StatusBar
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/modernremote/chrome.jsx", error: String((e && e.message) || e) }); }

// ui_kits/modernremote/controls.jsx
try { (() => {
/* ModernRemote kit — extra controls used by advanced screens.
   Kit-local copies of the design-system primitives (same .mr-* classes). */
const {
  useState: cuS
} = React;
function Expander({
  icon,
  title,
  description,
  children,
  defaultOpen = false
}) {
  const [open, setOpen] = cuS(defaultOpen);
  return /*#__PURE__*/React.createElement("div", {
    className: `mr-expander ${open ? "mr-expander--open" : ""}`
  }, /*#__PURE__*/React.createElement("div", {
    className: "mr-expander__header",
    role: "button",
    tabIndex: 0,
    onClick: () => setOpen(o => !o)
  }, icon && /*#__PURE__*/React.createElement(Icon, {
    className: "mr-expander__icon",
    name: icon,
    size: 20
  }), /*#__PURE__*/React.createElement("div", {
    className: "mr-expander__text"
  }, /*#__PURE__*/React.createElement("div", {
    className: "mr-expander__title"
  }, title), description && /*#__PURE__*/React.createElement("div", {
    className: "mr-expander__desc"
  }, description)), /*#__PURE__*/React.createElement(Icon, {
    className: "mr-expander__chevron",
    name: "chevron_down",
    size: 16
  })), open && /*#__PURE__*/React.createElement("div", {
    className: "mr-expander__content"
  }, children));
}
function Radio({
  options,
  value,
  onChange
}) {
  return /*#__PURE__*/React.createElement("div", {
    role: "radiogroup",
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 10
    }
  }, options.map(o => /*#__PURE__*/React.createElement("label", {
    key: o.value,
    className: `mr-radio ${o.value === value ? "mr-radio--checked" : ""}`
  }, /*#__PURE__*/React.createElement("span", {
    className: "mr-radio__dot",
    role: "radio",
    "aria-checked": o.value === value,
    tabIndex: 0,
    onClick: () => onChange(o.value),
    onKeyDown: e => {
      if (e.key === " " || e.key === "Enter") {
        e.preventDefault();
        onChange(o.value);
      }
    }
  }), /*#__PURE__*/React.createElement("span", null, o.label))));
}
function Slider({
  value,
  min = 0,
  max = 100,
  step = 1,
  onChange
}) {
  const ref = React.useRef(null);
  const pct = (value - min) / (max - min) * 100;
  const set = clientX => {
    const r = ref.current.getBoundingClientRect();
    let t = Math.max(0, Math.min(1, (clientX - r.left) / r.width));
    onChange(Math.round((min + t * (max - min)) / step) * step);
  };
  const down = e => {
    set(e.clientX);
    const m = ev => set(ev.clientX);
    const u = () => {
      window.removeEventListener("mousemove", m);
      window.removeEventListener("mouseup", u);
    };
    window.addEventListener("mousemove", m);
    window.addEventListener("mouseup", u);
  };
  return /*#__PURE__*/React.createElement("div", {
    className: "mr-slider",
    ref: ref,
    onMouseDown: down
  }, /*#__PURE__*/React.createElement("div", {
    className: "mr-slider__track"
  }, /*#__PURE__*/React.createElement("div", {
    className: "mr-slider__fill",
    style: {
      width: `${pct}%`
    }
  }), /*#__PURE__*/React.createElement("div", {
    className: "mr-slider__thumb",
    style: {
      left: `${pct}%`
    }
  })));
}
function NumberBox({
  value,
  min = -Infinity,
  max = Infinity,
  step = 1,
  onChange
}) {
  const clamp = v => Math.max(min, Math.min(max, v));
  return /*#__PURE__*/React.createElement("div", {
    className: "mr-numberbox"
  }, /*#__PURE__*/React.createElement("input", {
    type: "text",
    inputMode: "numeric",
    value: value,
    onChange: e => {
      const n = Number(e.target.value);
      onChange(Number.isNaN(n) ? e.target.value : n);
    }
  }), /*#__PURE__*/React.createElement("div", {
    className: "mr-numberbox__btns"
  }, /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "mr-numberbox__btn",
    "aria-label": "Increase",
    onClick: () => onChange(clamp(Number(value) + step))
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "chevron_up",
    size: 12
  })), /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "mr-numberbox__btn",
    "aria-label": "Decrease",
    onClick: () => onChange(clamp(Number(value) - step))
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "chevron_down",
    size: 12
  }))));
}
const INFOBAR_ICON = {
  info: "info",
  success: "checkmark_circle",
  caution: "warning",
  critical: "error_circle"
};
function InfoBar({
  severity = "info",
  title,
  message,
  actions,
  onClose
}) {
  return /*#__PURE__*/React.createElement("div", {
    className: `mr-infobar ${severity !== "info" ? `mr-infobar--${severity}` : ""}`,
    role: "status"
  }, /*#__PURE__*/React.createElement(Icon, {
    className: "mr-infobar__icon",
    name: INFOBAR_ICON[severity],
    size: 20,
    filled: true
  }), /*#__PURE__*/React.createElement("div", {
    className: "mr-infobar__body"
  }, title && /*#__PURE__*/React.createElement("div", {
    className: "mr-infobar__title"
  }, title), message && /*#__PURE__*/React.createElement("div", {
    className: "mr-infobar__msg"
  }, message), actions && /*#__PURE__*/React.createElement("div", {
    className: "mr-infobar__actions"
  }, actions)), onClose && /*#__PURE__*/React.createElement("button", {
    className: "mr-icon-btn",
    "aria-label": "Dismiss",
    onClick: onClose
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "dismiss",
    size: 16
  })));
}
function Ring({
  value,
  size = 32
}) {
  if (value == null) return /*#__PURE__*/React.createElement("span", {
    className: "mr-ring mr-ring--indeterminate",
    style: {
      width: size,
      height: size
    }
  });
  return /*#__PURE__*/React.createElement("span", {
    className: "mr-ring",
    style: {
      width: size,
      height: size,
      ["--p"]: `${value}%`
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: "mr-ring__hole"
  }));
}
function Bar({
  value
}) {
  const ind = value == null;
  return /*#__PURE__*/React.createElement("div", {
    className: `mr-pbar ${ind ? "mr-pbar--indeterminate" : ""}`
  }, /*#__PURE__*/React.createElement("div", {
    className: "mr-pbar__fill",
    style: ind ? undefined : {
      width: `${value}%`
    }
  }));
}
Object.assign(window, {
  Expander,
  Radio,
  Slider,
  NumberBox,
  InfoBar,
  Ring,
  Bar
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/modernremote/controls.jsx", error: String((e && e.message) || e) }); }

// ui_kits/modernremote/data.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/* ModernRemote kit — shared primitives + sample data. Exports to window. */
const ICON_CDN = "https://cdn.jsdelivr.net/npm/@fluentui/svg-icons@1.1.328/icons/";
const ICON_SIZES = [16, 20, 24, 28, 48];
function iconUrl(name, px, filled) {
  const base = window.MR_ICON_BASE;
  if (base) return `${base}${name}${filled ? "_filled" : ""}.svg`;
  return `${ICON_CDN}${name}_${px}_${filled ? "filled" : "regular"}.svg`;
}
function Icon({
  name,
  size = 16,
  filled = false,
  className = "",
  style,
  label
}) {
  const px = ICON_SIZES.includes(size) ? size : 16;
  const url = iconUrl(name, px, filled);
  return /*#__PURE__*/React.createElement("span", {
    className: `mr-icon ${className}`,
    role: label ? "img" : undefined,
    "aria-label": label || undefined,
    "aria-hidden": label ? undefined : true,
    style: {
      fontSize: size,
      ["--i"]: `url('${url}')`,
      ...style
    }
  });
}
function Btn({
  variant = "standard",
  size = "md",
  icon,
  iconPosition = "start",
  children,
  className = "",
  ...rest
}) {
  const cls = ["mr-btn", variant !== "standard" ? `mr-btn--${variant}` : "", size === "sm" ? "mr-btn--sm" : "", className].filter(Boolean).join(" ");
  const g = icon ? /*#__PURE__*/React.createElement(Icon, {
    name: icon,
    size: 16
  }) : null;
  return /*#__PURE__*/React.createElement("button", _extends({
    className: cls
  }, rest), iconPosition === "start" && g, children != null && /*#__PURE__*/React.createElement("span", null, children), iconPosition === "end" && g);
}
function IconBtn({
  icon,
  size = 16,
  tone = "default",
  label,
  className = "",
  ...rest
}) {
  const cls = ["mr-icon-btn", tone !== "default" ? `mr-icon-btn--${tone}` : "", className].filter(Boolean).join(" ");
  return /*#__PURE__*/React.createElement("button", _extends({
    className: cls,
    "aria-label": label,
    title: label
  }, rest), /*#__PURE__*/React.createElement(Icon, {
    name: icon,
    size: size
  }));
}
const STATUS_COLOR = {
  connected: "var(--status-connected)",
  connecting: "var(--status-connecting)",
  disconnected: "var(--status-disconnected)",
  error: "var(--status-error)"
};
function StatusDot({
  status = "disconnected",
  size = 8,
  style
}) {
  const c = STATUS_COLOR[status];
  const ring = status === "connected" || status === "connecting" || status === "error";
  return /*#__PURE__*/React.createElement("span", {
    style: {
      display: "inline-block",
      width: size,
      height: size,
      borderRadius: "50%",
      flex: "none",
      background: c,
      boxShadow: ring ? `0 0 0 3px color-mix(in srgb, ${c} 22%, transparent)` : "none",
      ...style
    }
  });
}
function Field({
  label,
  children
}) {
  return /*#__PURE__*/React.createElement("label", {
    className: "mr-field",
    style: {
      width: "100%"
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: "mr-field__label"
  }, label), children);
}
function Input({
  icon,
  value,
  onChange,
  placeholder,
  type = "text",
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    className: "mr-textbox"
  }, rest), icon && /*#__PURE__*/React.createElement(Icon, {
    name: icon,
    size: 16
  }), /*#__PURE__*/React.createElement("input", {
    type: type,
    value: value,
    onChange: onChange,
    placeholder: placeholder
  }));
}
function Combo({
  value,
  placeholder = "Select…",
  onClick
}) {
  return /*#__PURE__*/React.createElement("button", {
    type: "button",
    className: "mr-combobox",
    onClick: onClick
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      color: value ? "inherit" : "var(--fill-text-tertiary)"
    }
  }, value || placeholder), /*#__PURE__*/React.createElement(Icon, {
    name: "chevron_down",
    size: 16
  }));
}
function Check({
  checked,
  onChange,
  children
}) {
  return /*#__PURE__*/React.createElement("label", {
    className: `mr-checkbox ${checked ? "mr-checkbox--checked" : ""}`
  }, /*#__PURE__*/React.createElement("span", {
    className: "mr-checkbox__box",
    role: "checkbox",
    "aria-checked": checked,
    tabIndex: 0,
    onClick: () => onChange(!checked),
    onKeyDown: e => {
      if (e.key === " " || e.key === "Enter") {
        e.preventDefault();
        onChange(!checked);
      }
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "checkmark",
    size: 16
  })), children != null && /*#__PURE__*/React.createElement("span", null, children));
}
function Switch({
  checked,
  onChange
}) {
  return /*#__PURE__*/React.createElement("button", {
    type: "button",
    role: "switch",
    "aria-checked": checked,
    className: `mr-switch ${checked ? "mr-switch--on" : ""}`,
    onClick: () => onChange(!checked)
  }, /*#__PURE__*/React.createElement("span", {
    className: "mr-switch__track"
  }, /*#__PURE__*/React.createElement("span", {
    className: "mr-switch__thumb"
  })));
}

/* protocol → glyph + default port */
const PROTOCOLS = {
  RDP: {
    icon: "desktop",
    port: 3389
  },
  SSH: {
    icon: "code",
    port: 22
  },
  VNC: {
    icon: "desktop",
    port: 5900
  },
  Telnet: {
    icon: "code",
    port: 23
  },
  HTTPS: {
    icon: "globe",
    port: 443
  },
  HTTP: {
    icon: "globe",
    port: 80
  }
};

/* ---- Sample connection store (mirrors a .confCons hierarchy) ---- */
const TREE = [{
  id: "f-prod",
  name: "Production",
  type: "folder",
  open: true,
  children: [{
    id: "c-edge",
    name: "edge-fw01",
    host: "edge-fw01.corp.local",
    protocol: "RDP",
    status: "connected"
  }, {
    id: "c-dc1",
    name: "dc-01",
    host: "10.0.0.10",
    protocol: "RDP",
    status: "disconnected"
  }, {
    id: "c-web2",
    name: "web-02",
    host: "10.0.2.22",
    protocol: "SSH",
    status: "connecting"
  }, {
    id: "c-vault",
    name: "vault.corp",
    host: "vault.corp.local",
    protocol: "HTTPS",
    status: "disconnected"
  }]
}, {
  id: "f-net",
  name: "Network",
  type: "folder",
  open: true,
  children: [{
    id: "c-core",
    name: "core-sw01",
    host: "10.0.0.2",
    protocol: "SSH",
    status: "disconnected"
  }, {
    id: "c-rtr",
    name: "edge-rtr",
    host: "10.0.0.1",
    protocol: "Telnet",
    status: "disconnected"
  }]
}, {
  id: "f-lab",
  name: "Lab",
  type: "folder",
  open: false,
  children: [{
    id: "c-lab1",
    name: "lab-win11",
    host: "192.168.56.21",
    protocol: "RDP",
    status: "disconnected"
  }, {
    id: "c-lab2",
    name: "lab-ubuntu",
    host: "192.168.56.22",
    protocol: "VNC",
    status: "disconnected"
  }]
}];

/* Initially open session tabs */
const SESSIONS = [{
  id: "s1",
  connId: "c-edge",
  name: "edge-fw01",
  host: "edge-fw01.corp.local",
  protocol: "RDP",
  status: "connected"
}, {
  id: "s2",
  connId: "c-web2",
  name: "web-02",
  host: "10.0.2.22",
  protocol: "SSH",
  status: "connecting"
}];
Object.assign(window, {
  Icon,
  Btn,
  IconBtn,
  StatusDot,
  Field,
  Input,
  Combo,
  Check,
  Switch,
  PROTOCOLS,
  TREE,
  SESSIONS
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/modernremote/data.jsx", error: String((e && e.message) || e) }); }

// ui_kits/modernremote/dialogs.jsx
try { (() => {
/* ModernRemote kit — New Connection dialog + Settings panel. */
const {
  useState: useS
} = React;
function ProtocolSelect({
  value,
  onChange
}) {
  const [open, setOpen] = useState(false);
  const keys = Object.keys(PROTOCOLS);
  return /*#__PURE__*/React.createElement("div", {
    style: {
      position: "relative",
      width: "100%"
    }
  }, /*#__PURE__*/React.createElement(Combo, {
    value: value,
    onClick: () => setOpen(o => !o)
  }), open && /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    style: {
      position: "fixed",
      inset: 0,
      zIndex: 59
    },
    onClick: () => setOpen(false)
  }), /*#__PURE__*/React.createElement("div", {
    className: "k-menu",
    style: {
      position: "absolute",
      top: "calc(100% + 4px)",
      left: 0,
      right: 0,
      minWidth: 0
    }
  }, keys.map(k => /*#__PURE__*/React.createElement("div", {
    key: k,
    className: "k-menu__item",
    onClick: () => {
      onChange(k);
      setOpen(false);
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: PROTOCOLS[k].icon,
    size: 16
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }, k), /*#__PURE__*/React.createElement("span", {
    style: {
      font: "var(--text-caption)",
      color: "var(--fill-text-tertiary)"
    }
  }, ":", PROTOCOLS[k].port))))));
}
function NewConnectionDialog({
  onClose,
  onCreate,
  prefillHost
}) {
  const [proto, setProto] = useState("RDP");
  const [name, setName] = useState("");
  const [host, setHost] = useState(prefillHost || "");
  const [port, setPort] = useState(PROTOCOLS.RDP.port);
  const [user, setUser] = useState("");
  const [pass, setPass] = useState("");
  const [inherit, setInherit] = useState(true);
  const [nla, setNla] = useState(true);
  const [gateway, setGateway] = useState(false);
  const pickProto = p => {
    setProto(p);
    setPort(PROTOCOLS[p].port);
  };
  const create = () => onCreate({
    name: name || host || "New connection",
    host: host || "host.local",
    protocol: proto
  });
  return /*#__PURE__*/React.createElement("div", {
    className: "k-scrim",
    onMouseDown: e => {
      if (e.target === e.currentTarget) onClose();
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog",
    role: "dialog",
    "aria-label": "New connection"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__head"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__title"
  }, "New connection")), /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__body mr-scroll"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-form-row"
  }, /*#__PURE__*/React.createElement("label", null, "Name"), /*#__PURE__*/React.createElement(Input, {
    value: name,
    onChange: e => setName(e.target.value),
    placeholder: "Friendly name"
  })), /*#__PURE__*/React.createElement("div", {
    className: "k-form-row"
  }, /*#__PURE__*/React.createElement("label", null, "Protocol"), /*#__PURE__*/React.createElement(ProtocolSelect, {
    value: proto,
    onChange: pickProto
  })), /*#__PURE__*/React.createElement("div", {
    className: "k-form-row"
  }, /*#__PURE__*/React.createElement("label", null, "Host"), /*#__PURE__*/React.createElement(Input, {
    value: host,
    onChange: e => setHost(e.target.value),
    placeholder: "hostname or IP"
  })), /*#__PURE__*/React.createElement("div", {
    className: "k-form-row"
  }, /*#__PURE__*/React.createElement("label", null, "Port"), /*#__PURE__*/React.createElement(Input, {
    value: String(port),
    onChange: e => setPort(e.target.value)
  })), /*#__PURE__*/React.createElement("div", {
    className: "k-form-section"
  }, "Credentials"), /*#__PURE__*/React.createElement("div", {
    className: "k-form-row"
  }, /*#__PURE__*/React.createElement("label", null, "Username"), /*#__PURE__*/React.createElement(Input, {
    value: user,
    onChange: e => setUser(e.target.value),
    placeholder: "domain\\\\user"
  })), /*#__PURE__*/React.createElement("div", {
    className: "k-form-row"
  }, /*#__PURE__*/React.createElement("label", null, "Password"), /*#__PURE__*/React.createElement(Input, {
    type: "password",
    value: pass,
    onChange: e => setPass(e.target.value),
    placeholder: "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022"
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 10,
      paddingTop: 10
    }
  }, /*#__PURE__*/React.createElement(Check, {
    checked: inherit,
    onChange: setInherit
  }, "Inherit credentials from parent folder"), /*#__PURE__*/React.createElement(Check, {
    checked: nla,
    onChange: setNla
  }, "Require Network Level Authentication"), /*#__PURE__*/React.createElement(Check, {
    checked: gateway,
    onChange: setGateway
  }, "Connect through RD Gateway"))), /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__foot"
  }, /*#__PURE__*/React.createElement(Btn, {
    onClick: onClose
  }, "Cancel"), /*#__PURE__*/React.createElement(Btn, {
    variant: "accent",
    icon: "plug_connected",
    onClick: create
  }, "Save & connect"))));
}
const SETTINGS_NAV = [{
  id: "general",
  label: "General",
  icon: "settings"
}, {
  id: "appearance",
  label: "Appearance",
  icon: "paint_brush"
}, {
  id: "security",
  label: "Security",
  icon: "shield"
}, {
  id: "connections",
  label: "Connections",
  icon: "plug_connected"
}, {
  id: "plugins",
  label: "Plugins",
  icon: "puzzle_piece"
}, {
  id: "about",
  label: "About",
  icon: "info"
}];
function SettingsPanel({
  onClose,
  theme,
  setTheme,
  accent,
  setAccent,
  density,
  setDensity
}) {
  const [nav, setNav] = useState("appearance");
  const [portable, setPortable] = useState(true);
  const [updates, setUpdates] = useState(true);
  const [signing, setSigning] = useState(true);
  const [masterPw, setMasterPw] = useState(false);
  return /*#__PURE__*/React.createElement("div", {
    className: "k-settings"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-settings__nav"
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 6,
      padding: "0 6px 12px"
    }
  }, /*#__PURE__*/React.createElement(IconBtn, {
    icon: "arrow_left",
    label: "Back",
    onClick: onClose
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      font: "var(--text-body-strong)"
    }
  }, "Settings")), SETTINGS_NAV.map(n => /*#__PURE__*/React.createElement("div", {
    key: n.id,
    className: `k-settings__navitem ${nav === n.id ? "k-settings__navitem--active" : ""}`,
    onClick: () => setNav(n.id)
  }, /*#__PURE__*/React.createElement(Icon, {
    name: n.icon,
    size: 20
  }), /*#__PURE__*/React.createElement("span", null, n.label)))), /*#__PURE__*/React.createElement("div", {
    className: "k-settings__content mr-scroll"
  }, nav === "appearance" && /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    className: "k-settings__h"
  }, "Appearance"), /*#__PURE__*/React.createElement("div", {
    className: "k-settings__sub"
  }, "How ModernRemote looks on this device."), /*#__PURE__*/React.createElement("div", {
    className: "k-settings__group"
  }, /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "paint_brush",
    title: "App theme",
    desc: "Light, dark, high contrast, or follow the system setting",
    action: /*#__PURE__*/React.createElement(ThemeChoice, {
      theme: theme,
      setTheme: setTheme
    })
  }), /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "options",
    title: "Accent color",
    desc: "Match your Windows accent",
    action: /*#__PURE__*/React.createElement(AccentSwatches, {
      accent: accent,
      setAccent: setAccent
    })
  }), /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "line_horizontal_3",
    title: "Compact density",
    desc: "Tighter rows and controls for large connection trees",
    action: /*#__PURE__*/React.createElement(Switch, {
      checked: density === "compact",
      onChange: v => setDensity(v ? "compact" : "comfortable")
    })
  }), /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "text_font",
    title: "Terminal font",
    desc: "Cascadia Code, 13.5 px",
    action: /*#__PURE__*/React.createElement(Combo, {
      value: "Cascadia Code"
    })
  }))), nav === "security" && /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    className: "k-settings__h"
  }, "Security"), /*#__PURE__*/React.createElement("div", {
    className: "k-settings__sub"
  }, "Credentials never persist in plaintext. Secrets are held as pinned byte arrays and zeroed after delivery."), /*#__PURE__*/React.createElement("div", {
    className: "k-settings__group"
  }, /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "key",
    title: "Master password",
    desc: "Encrypt the connection store at rest",
    action: /*#__PURE__*/React.createElement(Switch, {
      checked: masterPw,
      onChange: setMasterPw
    })
  }), /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "shield_checkmark",
    title: "Plugin signature enforcement",
    desc: "Only load signed plugins (recommended)",
    action: /*#__PURE__*/React.createElement(Switch, {
      checked: signing,
      onChange: setSigning
    })
  }))), nav === "general" && /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    className: "k-settings__h"
  }, "General"), /*#__PURE__*/React.createElement("div", {
    className: "k-settings__sub"
  }, "Startup and deployment behavior."), /*#__PURE__*/React.createElement("div", {
    className: "k-settings__group"
  }, /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "usb_stick",
    title: "Portable mode",
    desc: "Zero registry writes \u2014 run from a USB drive",
    action: /*#__PURE__*/React.createElement(Switch, {
      checked: portable,
      onChange: setPortable
    })
  }), /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "arrow_clockwise",
    title: "Check for updates on launch",
    desc: "Versioned JSON schema with migration scripts",
    action: /*#__PURE__*/React.createElement(Switch, {
      checked: updates,
      onChange: setUpdates
    })
  }))), nav === "connections" && /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    className: "k-settings__h"
  }, "Connections"), /*#__PURE__*/React.createElement("div", {
    className: "k-settings__sub"
  }, "Defaults applied to newly created connections."), /*#__PURE__*/React.createElement("div", {
    className: "k-settings__group"
  }, /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "desktop",
    title: "Default protocol",
    desc: "Used by quick connect",
    action: /*#__PURE__*/React.createElement(Combo, {
      value: "RDP"
    })
  }), /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "people_team",
    title: "Default credential",
    desc: "Applied when a node has none",
    action: /*#__PURE__*/React.createElement(Combo, {
      value: "corp\\\\admin"
    })
  }))), nav === "plugins" && /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    className: "k-settings__h"
  }, "Plugins"), /*#__PURE__*/React.createElement("div", {
    className: "k-settings__sub"
  }, "Extensions loaded from the public plugin SDK. Signing is enforced."), /*#__PURE__*/React.createElement("div", {
    className: "k-settings__group"
  }, /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "puzzle_piece",
    title: "KeePass vault",
    desc: "v1.2.0 \xB7 signed",
    action: /*#__PURE__*/React.createElement(Switch, {
      checked: true,
      onChange: () => {}
    })
  }), /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "puzzle_piece",
    title: "HashiCorp Vault",
    desc: "v0.9.4 \xB7 signed",
    action: /*#__PURE__*/React.createElement(Switch, {
      checked: true,
      onChange: () => {}
    })
  }), /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "puzzle_piece",
    title: "Port scanner",
    desc: "built-in",
    action: /*#__PURE__*/React.createElement(Switch, {
      checked: true,
      onChange: () => {}
    })
  }))), nav === "about" && /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    className: "k-settings__h"
  }, "About"), /*#__PURE__*/React.createElement("div", {
    className: "k-settings__sub"
  }, "ModernRemote 2.0 \xB7 .NET 10 \xB7 WPF Fluent"), /*#__PURE__*/React.createElement("div", {
    className: "k-settings__group"
  }, /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "info",
    title: "Version",
    desc: "2.0.0 (build 2026.06)",
    action: /*#__PURE__*/React.createElement(Btn, {
      size: "sm"
    }, "Release notes")
  }), /*#__PURE__*/React.createElement(SettingsRow, {
    icon: "document",
    title: "Imports supported",
    desc: "mRemoteNG \xB7 RDM \xB7 Royal TS \xB7 MobaXterm \xB7 SecureCRT \xB7 PuTTY"
  })))));
}
function SettingsRow({
  icon,
  title,
  desc,
  action
}) {
  return /*#__PURE__*/React.createElement("div", {
    className: "mr-settings-card"
  }, /*#__PURE__*/React.createElement(Icon, {
    className: "mr-settings-card__icon",
    name: icon,
    size: 20
  }), /*#__PURE__*/React.createElement("div", {
    className: "mr-settings-card__body"
  }, /*#__PURE__*/React.createElement("div", {
    className: "mr-settings-card__title"
  }, title), desc && /*#__PURE__*/React.createElement("div", {
    className: "mr-settings-card__desc"
  }, desc)), action);
}
function ThemeChoice({
  theme,
  setTheme
}) {
  const opts = [["light", "Light"], ["dark", "Dark"], ["hc", "Contrast"], ["system", "System"]];
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: "inline-flex",
      border: "1px solid var(--stroke-control)",
      borderRadius: "var(--radius-control)",
      overflow: "hidden"
    }
  }, opts.map(([k, l], i) => /*#__PURE__*/React.createElement("button", {
    key: k,
    onClick: () => setTheme(k),
    style: {
      border: "none",
      padding: "0 14px",
      height: 30,
      cursor: "pointer",
      font: "var(--text-body)",
      borderLeft: i ? "1px solid var(--stroke-control)" : "none",
      background: theme === k ? "var(--fill-accent-default)" : "var(--fill-control-default)",
      color: theme === k ? "var(--fill-text-on-accent)" : "var(--fill-text-primary)"
    }
  }, l)));
}
function AccentSwatches({
  accent,
  setAccent
}) {
  const swatches = [["blue", "#0078d4"], ["teal", "#038387"], ["green", "#498205"], ["purple", "#8764b8"], ["orange", "#ca5010"], ["red", "#c42b1c"], ["pink", "#e3008c"]];
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      gap: 8
    }
  }, swatches.map(([k, c]) => /*#__PURE__*/React.createElement("button", {
    key: k,
    "aria-label": k,
    title: k,
    onClick: () => setAccent(k),
    style: {
      width: 26,
      height: 26,
      borderRadius: "50%",
      cursor: "pointer",
      background: c,
      border: accent === k ? "2px solid var(--fill-text-primary)" : "2px solid transparent",
      boxShadow: accent === k ? "0 0 0 2px var(--fill-surface-base) inset" : "none"
    }
  })));
}
Object.assign(window, {
  NewConnectionDialog,
  SettingsPanel
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/modernremote/dialogs.jsx", error: String((e && e.message) || e) }); }

// ui_kits/modernremote/properties.jsx
try { (() => {
/* ModernRemote kit — Connection Properties editor (tabbed). */
const {
  useState: puS
} = React;
const PROP_TABS = [{
  id: "general",
  label: "General",
  icon: "settings"
}, {
  id: "connection",
  label: "Connection",
  icon: "plug_connected"
}, {
  id: "display",
  label: "Display",
  icon: "desktop"
}, {
  id: "gateway",
  label: "Gateway",
  icon: "shield"
}, {
  id: "advanced",
  label: "Advanced",
  icon: "options"
}];
function Inherited({
  from,
  onOverride
}) {
  return /*#__PURE__*/React.createElement("span", {
    style: {
      display: "inline-flex",
      alignItems: "center",
      gap: 6,
      font: "var(--text-caption)",
      color: "var(--fill-text-tertiary)"
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "folder_arrow_right",
    size: 14
  }), "Inherited from ", from, /*#__PURE__*/React.createElement("a", {
    href: "#",
    onClick: e => {
      e.preventDefault();
      onOverride && onOverride();
    },
    style: {
      color: "var(--fill-accent-text)"
    }
  }, "Override"));
}
function Row({
  label,
  children,
  hint
}) {
  return /*#__PURE__*/React.createElement("div", {
    className: "k-prop-row"
  }, /*#__PURE__*/React.createElement("label", null, label), /*#__PURE__*/React.createElement("div", {
    className: "k-prop-field"
  }, children, hint));
}
function ConnectionProperties({
  conn,
  onClose
}) {
  const [tab, setTab] = puS("general");
  const c = conn || {};
  const [proto, setProto] = puS(c.protocol || "RDP");
  const [host, setHost] = puS(c.host || "");
  const [port, setPort] = puS((PROTOCOLS[c.protocol] || PROTOCOLS.RDP).port);
  const [name, setName] = puS(c.name || "");
  const [showPw, setShowPw] = puS(false);
  const [credInherited, setCredInherited] = puS(true);
  const [mode, setMode] = puS("fit");
  const [scale, setScale] = puS(100);
  const [allMon, setAllMon] = puS(false);
  const [keepAlive, setKeepAlive] = puS(true);
  const [reconnect, setReconnect] = puS(true);
  const [timeout, setTimeout_] = puS(30);
  const [gw, setGw] = puS(false);
  const [redir, setRedir] = puS({
    clipboard: true,
    drives: false,
    printers: false,
    audio: true,
    smartcard: false
  });
  const toggleRedir = k => setRedir(r => ({
    ...r,
    [k]: !r[k]
  }));
  return /*#__PURE__*/React.createElement("div", {
    className: "k-scrim",
    onMouseDown: e => {
      if (e.target === e.currentTarget) onClose();
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog k-dialog--wide",
    role: "dialog",
    "aria-label": "Connection properties"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__head",
    style: {
      display: "flex",
      alignItems: "center",
      gap: 12
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: (PROTOCOLS[proto] || {}).icon || "desktop",
    size: 24
  }), /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__title"
  }, name || "Connection", " \u2014 properties"), /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--text-caption)",
      color: "var(--fill-text-secondary)"
    }
  }, proto, " \xB7 ", host || "host.local", ":", port))), /*#__PURE__*/React.createElement("div", {
    className: "k-prop-body"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-prop-nav"
  }, PROP_TABS.map(t => /*#__PURE__*/React.createElement("div", {
    key: t.id,
    className: `k-settings__navitem ${tab === t.id ? "k-settings__navitem--active" : ""}`,
    onClick: () => setTab(t.id)
  }, /*#__PURE__*/React.createElement(Icon, {
    name: t.icon,
    size: 20
  }), /*#__PURE__*/React.createElement("span", null, t.label)))), /*#__PURE__*/React.createElement("div", {
    className: "k-prop-content mr-scroll"
  }, tab === "general" && /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(Row, {
    label: "Name"
  }, /*#__PURE__*/React.createElement(Input, {
    value: name,
    onChange: e => setName(e.target.value),
    placeholder: "Friendly name"
  })), /*#__PURE__*/React.createElement(Row, {
    label: "Folder"
  }, /*#__PURE__*/React.createElement(Combo, {
    value: "Production"
  })), /*#__PURE__*/React.createElement(Row, {
    label: "Protocol"
  }, /*#__PURE__*/React.createElement(Combo, {
    value: proto,
    onClick: () => {}
  })), /*#__PURE__*/React.createElement(Row, {
    label: "Host"
  }, /*#__PURE__*/React.createElement(Input, {
    value: host,
    onChange: e => setHost(e.target.value),
    placeholder: "hostname or IP"
  })), /*#__PURE__*/React.createElement(Row, {
    label: "Port"
  }, /*#__PURE__*/React.createElement(NumberBox, {
    value: port,
    min: 1,
    max: 65535,
    onChange: setPort
  })), /*#__PURE__*/React.createElement("div", {
    className: "k-prop-section"
  }, "Credentials"), /*#__PURE__*/React.createElement(Row, {
    label: "Username",
    hint: credInherited ? /*#__PURE__*/React.createElement(Inherited, {
      from: "Production",
      onOverride: () => setCredInherited(false)
    }) : null
  }, /*#__PURE__*/React.createElement(Input, {
    value: credInherited ? "corp\\admin" : "",
    onChange: () => {},
    placeholder: "domain\\\\user"
  })), /*#__PURE__*/React.createElement(Row, {
    label: "Password"
  }, /*#__PURE__*/React.createElement("div", {
    className: "mr-textbox",
    style: {
      width: "100%"
    }
  }, /*#__PURE__*/React.createElement("input", {
    type: showPw ? "text" : "password",
    defaultValue: "hunter2hunter2"
  }), /*#__PURE__*/React.createElement("button", {
    className: "mr-icon-btn",
    style: {
      width: 24,
      height: 24
    },
    "aria-label": "Show password",
    onClick: () => setShowPw(s => !s)
  }, /*#__PURE__*/React.createElement(Icon, {
    name: showPw ? "eye_off" : "eye",
    size: 16
  })))), /*#__PURE__*/React.createElement("div", {
    style: {
      paddingTop: 10
    }
  }, /*#__PURE__*/React.createElement(Check, {
    checked: true,
    onChange: () => {}
  }, "Save to encrypted store"))), tab === "connection" && /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(Row, {
    label: "Connect timeout"
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 8
    }
  }, /*#__PURE__*/React.createElement(NumberBox, {
    value: timeout,
    min: 5,
    max: 120,
    step: 5,
    onChange: setTimeout_
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      color: "var(--fill-text-secondary)"
    }
  }, "seconds"))), /*#__PURE__*/React.createElement(Row, {
    label: "Compression"
  }, /*#__PURE__*/React.createElement(Combo, {
    value: "Auto"
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 12,
      paddingTop: 8
    }
  }, /*#__PURE__*/React.createElement("label", {
    className: "k-prop-toggle"
  }, /*#__PURE__*/React.createElement("span", null, "Keep session alive"), /*#__PURE__*/React.createElement(Switch, {
    checked: keepAlive,
    onChange: setKeepAlive
  })), /*#__PURE__*/React.createElement("label", {
    className: "k-prop-toggle"
  }, /*#__PURE__*/React.createElement("span", null, "Reconnect automatically if dropped"), /*#__PURE__*/React.createElement(Switch, {
    checked: reconnect,
    onChange: setReconnect
  })))), tab === "display" && /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(Row, {
    label: "Resolution"
  }, /*#__PURE__*/React.createElement(Combo, {
    value: "1920 \xD7 1080"
  })), /*#__PURE__*/React.createElement(Row, {
    label: "Color depth"
  }, /*#__PURE__*/React.createElement(Combo, {
    value: "32-bit (True Color)"
  })), /*#__PURE__*/React.createElement("div", {
    className: "k-prop-section"
  }, "Display mode"), /*#__PURE__*/React.createElement(Radio, {
    value: mode,
    onChange: setMode,
    options: [{
      value: "fit",
      label: "Fit to window"
    }, {
      value: "100",
      label: "Actual size (100%)"
    }, {
      value: "fill",
      label: "Fill — stretch to client"
    }]
  }), /*#__PURE__*/React.createElement(Row, {
    label: "Scale"
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 12,
      width: 280
    }
  }, /*#__PURE__*/React.createElement(Slider, {
    value: scale,
    min: 50,
    max: 200,
    step: 10,
    onChange: setScale
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      font: "var(--font-mono)",
      fontSize: 13,
      color: "var(--fill-text-secondary)",
      width: 44,
      textAlign: "right"
    }
  }, scale, "%"))), /*#__PURE__*/React.createElement("div", {
    style: {
      paddingTop: 8
    }
  }, /*#__PURE__*/React.createElement("label", {
    className: "k-prop-toggle",
    style: {
      maxWidth: 360
    }
  }, /*#__PURE__*/React.createElement("span", null, "Use all monitors"), /*#__PURE__*/React.createElement(Switch, {
    checked: allMon,
    onChange: setAllMon
  })))), tab === "gateway" && /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(InfoBar, {
    severity: "info",
    message: "An RD Gateway tunnels RDP through HTTPS to reach hosts behind a firewall."
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      paddingTop: 12
    }
  }, /*#__PURE__*/React.createElement("label", {
    className: "k-prop-toggle",
    style: {
      maxWidth: 360
    }
  }, /*#__PURE__*/React.createElement("span", null, "Connect through an RD Gateway"), /*#__PURE__*/React.createElement(Switch, {
    checked: gw,
    onChange: setGw
  }))), gw && /*#__PURE__*/React.createElement("div", {
    style: {
      paddingTop: 8
    }
  }, /*#__PURE__*/React.createElement(Row, {
    label: "Gateway host"
  }, /*#__PURE__*/React.createElement(Input, {
    placeholder: "gw.corp.local"
  })), /*#__PURE__*/React.createElement(Row, {
    label: "Auth method"
  }, /*#__PURE__*/React.createElement(Combo, {
    value: "NTLM"
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      paddingTop: 8
    }
  }, /*#__PURE__*/React.createElement(Check, {
    checked: true,
    onChange: () => {}
  }, "Bypass gateway for local addresses")))), tab === "advanced" && /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    className: "k-prop-section"
  }, "Local resource redirection"), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 10
    }
  }, [["clipboard", "Clipboard"], ["drives", "Local drives"], ["printers", "Printers"], ["audio", "Audio"], ["smartcard", "Smart cards"]].map(([k, l]) => /*#__PURE__*/React.createElement(Check, {
    key: k,
    checked: redir[k],
    onChange: () => toggleRedir(k)
  }, l))), /*#__PURE__*/React.createElement("div", {
    className: "k-prop-section"
  }, "Security"), /*#__PURE__*/React.createElement(Row, {
    label: "Security layer"
  }, /*#__PURE__*/React.createElement(Combo, {
    value: "Negotiate"
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      paddingTop: 4
    }
  }, /*#__PURE__*/React.createElement(Check, {
    checked: true,
    onChange: () => {}
  }, "Require Network Level Authentication"))))), /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__foot"
  }, /*#__PURE__*/React.createElement(Btn, {
    onClick: onClose
  }, "Cancel"), /*#__PURE__*/React.createElement(Btn, {
    onClick: onClose
  }, "Apply"), /*#__PURE__*/React.createElement(Btn, {
    variant: "accent",
    onClick: onClose
  }, "OK"))));
}
Object.assign(window, {
  ConnectionProperties
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/modernremote/properties.jsx", error: String((e && e.message) || e) }); }

// ui_kits/modernremote/sessions.jsx
try { (() => {
/* ModernRemote kit — session surfaces: RDP, SSH terminal, web, empty. */
const {
  useState,
  useEffect
} = React;
function useClock() {
  const [now, setNow] = useState(new Date());
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 1000 * 20);
    return () => clearInterval(t);
  }, []);
  return now;
}
function RdpSession({
  session
}) {
  const now = useClock();
  const time = now.toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit"
  });
  const date = now.toLocaleDateString([], {
    month: "numeric",
    day: "numeric",
    year: "numeric"
  });
  return /*#__PURE__*/React.createElement("div", {
    className: "k-rdp"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-rdp__desktop"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-rdp__center"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-rdp__host"
  }, session.host), /*#__PURE__*/React.createElement("div", {
    className: "k-rdp__meta"
  }, "Windows Server \xB7 RDP session \xB7 ", session.name)), /*#__PURE__*/React.createElement("div", {
    className: "k-rdp__clock"
  }, /*#__PURE__*/React.createElement("div", {
    className: "t"
  }, time), /*#__PURE__*/React.createElement("div", {
    className: "d"
  }, date))), /*#__PURE__*/React.createElement("div", {
    className: "k-rdp__taskbar"
  }, /*#__PURE__*/React.createElement("span", {
    className: "k-rdp__start"
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "grid_dots",
    size: 16
  })), /*#__PURE__*/React.createElement("span", {
    className: "k-rdp__tasksp"
  }), /*#__PURE__*/React.createElement("span", {
    className: "k-rdp__tray"
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "wifi_1",
    size: 16
  }), /*#__PURE__*/React.createElement(Icon, {
    name: "speaker_2",
    size: 16
  }), /*#__PURE__*/React.createElement("span", null, time))));
}
const TERM_LINES = [{
  t: "Last login: Mon Jun  9 08:41:02 2026 from 10.0.4.7",
  c: "gry"
}, {
  html: '<span class="grn">admin@web-02</span>:<span class="cyn">~</span>$ systemctl status nginx'
}, {
  html: '<span class="grn">●</span> nginx.service - A high performance web server'
}, {
  html: '     Loaded: loaded (/lib/systemd/system/nginx.service; <span class="grn">enabled</span>)'
}, {
  html: '     Active: <span class="grn">active (running)</span> since Mon 2026-06-09 06:12:55 UTC'
}, {
  html: '   Main PID: 1180 (nginx)'
}, {
  t: ""
}, {
  html: '<span class="grn">admin@web-02</span>:<span class="cyn">~</span>$ tail -n2 /var/log/nginx/access.log'
}, {
  html: '10.0.4.7 - - [09/Jun/2026:09:02:11] "GET /health" <span class="grn">200</span> 2'
}, {
  html: '10.0.4.7 - - [09/Jun/2026:09:02:14] "GET /api/v1/me" <span class="grn">200</span> 514'
}, {
  html: '<span class="grn">admin@web-02</span>:<span class="cyn">~</span>$ '
}];
function Terminal() {
  return /*#__PURE__*/React.createElement("div", {
    className: "k-term mr-scroll",
    role: "log"
  }, TERM_LINES.map((l, i) => l.html ? /*#__PURE__*/React.createElement("div", {
    key: i,
    dangerouslySetInnerHTML: {
      __html: l.html + (i === TERM_LINES.length - 1 ? '<span class="k-term__cursor"></span>' : "")
    }
  }) : /*#__PURE__*/React.createElement("div", {
    key: i,
    className: l.c || ""
  }, l.t || "\u00a0")));
}
function WebSession({
  session
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      position: "absolute",
      inset: 0,
      background: "#fff",
      display: "flex",
      flexDirection: "column"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 8,
      padding: "8px 12px",
      background: "#f3f3f3",
      borderBottom: "1px solid var(--stroke-divider)",
      color: "#1a1a1a"
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "lock_closed",
    size: 14,
    style: {
      color: "#0f7b0f"
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      font: "var(--text-caption)"
    }
  }, "https://", session.host, "/ui/login")), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      background: "#faf9f8"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      width: 320,
      padding: 28,
      background: "#fff",
      borderRadius: 8,
      boxShadow: "var(--shadow-flyout)",
      textAlign: "center",
      color: "#1a1a1a"
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "shield_keyhole",
    size: 28,
    style: {
      color: "var(--accent-base)"
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--text-subtitle)",
      margin: "10px 0 16px",
      color: "#1a1a1a"
    }
  }, "Vault Sign in"), /*#__PURE__*/React.createElement("div", {
    className: "mr-textbox",
    style: {
      marginBottom: 8
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "person",
    size: 16
  }), /*#__PURE__*/React.createElement("input", {
    placeholder: "Username"
  })), /*#__PURE__*/React.createElement("div", {
    className: "mr-textbox",
    style: {
      marginBottom: 14
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "key",
    size: 16
  }), /*#__PURE__*/React.createElement("input", {
    type: "password",
    placeholder: "Token"
  })), /*#__PURE__*/React.createElement("button", {
    className: "mr-btn mr-btn--accent",
    style: {
      width: "100%"
    }
  }, /*#__PURE__*/React.createElement("span", null, "Unlock")))));
}
function Connecting({
  session
}) {
  return /*#__PURE__*/React.createElement("div", {
    className: "k-connecting"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-spinner"
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--text-body-large)",
      color: "var(--fill-text-primary)"
    }
  }, "Connecting to ", session.name, "\u2026"), /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--text-body)",
      color: "var(--fill-text-secondary)"
    }
  }, session.protocol, " \xB7 ", session.host));
}
function EmptyState({
  onNew
}) {
  return /*#__PURE__*/React.createElement("div", {
    className: "k-empty"
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "plug_disconnected",
    size: 48
  }), /*#__PURE__*/React.createElement("div", {
    className: "k-empty__title"
  }, "No active session"), /*#__PURE__*/React.createElement("div", {
    className: "k-empty__sub"
  }, "Double-click a connection in the tree, type a host in quick connect, or create a new connection to get started."), /*#__PURE__*/React.createElement(Btn, {
    variant: "accent",
    icon: "add",
    onClick: onNew
  }, "New connection"));
}
function SessionArea({
  session,
  onNew
}) {
  if (!session) return /*#__PURE__*/React.createElement(EmptyState, {
    onNew: onNew
  });
  if (session.status === "connecting") return /*#__PURE__*/React.createElement(Connecting, {
    session: session
  });
  if (session.protocol === "SSH" || session.protocol === "Telnet") return /*#__PURE__*/React.createElement(Terminal, {
    session: session
  });
  if (session.protocol === "HTTPS" || session.protocol === "HTTP") return /*#__PURE__*/React.createElement(WebSession, {
    session: session
  });
  return /*#__PURE__*/React.createElement(RdpSession, {
    session: session
  });
}
Object.assign(window, {
  RdpSession,
  Terminal,
  WebSession,
  EmptyState,
  SessionArea,
  Connecting
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/modernremote/sessions.jsx", error: String((e && e.message) || e) }); }

// ui_kits/modernremote/tools.jsx
try { (() => {
/* ModernRemote kit — Import wizard, Port scanner, Vault picker, session chrome. */
const {
  useState: tuS,
  useEffect: tuE
} = React;

/* ---------------- Import wizard ---------------- */
const IMPORT_SOURCES = [{
  value: "confcons",
  label: "mRemoteNG (.confCons XML)",
  icon: "folder_arrow_right"
}, {
  value: "rdm",
  label: "Devolutions RDM (.rdm)",
  icon: "document"
}, {
  value: "royalts",
  label: "Royal TS (.rtsz)",
  icon: "document"
}, {
  value: "moba",
  label: "MobaXterm (.ini)",
  icon: "document"
}, {
  value: "securecrt",
  label: "SecureCRT (sessions)",
  icon: "document"
}, {
  value: "putty",
  label: "PuTTY / SuperPuTTY",
  icon: "document"
}];
function ImportWizard({
  onClose,
  onDone
}) {
  const [step, setStep] = tuS(0);
  const [src, setSrc] = tuS("confcons");
  const [merge, setMerge] = tuS("merge");
  const [importing, setImporting] = tuS(false);
  const [progress, setProgress] = tuS(0);
  const [done, setDone] = tuS(false);
  const startImport = () => {
    setStep(2);
    setImporting(true);
    setProgress(0);
    setDone(false);
    let p = 0;
    const t = setInterval(() => {
      p += 12 + Math.random() * 16;
      if (p >= 100) {
        p = 100;
        clearInterval(t);
        setImporting(false);
        setDone(true);
      }
      setProgress(Math.round(p));
    }, 220);
  };
  const steps = ["Source", "Options", "Review"];
  return /*#__PURE__*/React.createElement("div", {
    className: "k-scrim",
    onMouseDown: e => {
      if (e.target === e.currentTarget) onClose();
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog",
    style: {
      width: 600
    },
    role: "dialog",
    "aria-label": "Import connections"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__head"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__title"
  }, "Import connections")), /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__body mr-scroll"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-steps"
  }, steps.map((s, i) => /*#__PURE__*/React.createElement(React.Fragment, {
    key: s
  }, /*#__PURE__*/React.createElement("div", {
    className: `k-step ${i === step ? "k-step--active" : ""} ${i < step ? "k-step--done" : ""}`
  }, /*#__PURE__*/React.createElement("span", {
    className: "k-step__num"
  }, i < step ? /*#__PURE__*/React.createElement(Icon, {
    name: "checkmark",
    size: 12
  }) : i + 1), s), i < steps.length - 1 && /*#__PURE__*/React.createElement("span", {
    className: "k-step__line"
  })))), step === 0 && /*#__PURE__*/React.createElement("div", {
    style: {
      paddingTop: 8
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--text-body)",
      color: "var(--fill-text-secondary)",
      marginBottom: 14
    }
  }, "Choose the application to import from."), /*#__PURE__*/React.createElement(Radio, {
    value: src,
    onChange: setSrc,
    options: IMPORT_SOURCES.map(s => ({
      value: s.value,
      label: s.label
    }))
  })), step === 1 && /*#__PURE__*/React.createElement("div", {
    style: {
      paddingTop: 8,
      display: "flex",
      flexDirection: "column",
      gap: 14
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dropzone"
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "arrow_download",
    size: 32
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--text-body-strong)",
      color: "var(--fill-text-primary)"
    }
  }, "Drop your export file here"), /*#__PURE__*/React.createElement("div", null, "or ", /*#__PURE__*/React.createElement("a", {
    href: "#",
    onClick: e => e.preventDefault(),
    style: {
      color: "var(--fill-accent-text)"
    }
  }, "browse\u2026"), " \u2014 confCons.xml")), /*#__PURE__*/React.createElement("div", {
    className: "k-prop-section",
    style: {
      marginTop: 4
    }
  }, "On conflict"), /*#__PURE__*/React.createElement(Radio, {
    value: merge,
    onChange: setMerge,
    options: [{
      value: "merge",
      label: "Merge into existing tree (keep both)"
    }, {
      value: "replace",
      label: "Replace the current connection store"
    }]
  }), /*#__PURE__*/React.createElement(Check, {
    checked: true,
    onChange: () => {}
  }, "Decrypt passwords with master password")), step === 2 && /*#__PURE__*/React.createElement("div", {
    style: {
      paddingTop: 12,
      display: "flex",
      flexDirection: "column",
      gap: 14
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 14
    }
  }, /*#__PURE__*/React.createElement(Ring, {
    value: importing ? null : 100,
    size: 40
  }), /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--text-body-strong)"
    }
  }, importing ? "Importing…" : "Import complete"), /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--text-caption)",
      color: "var(--fill-text-secondary)"
    }
  }, IMPORT_SOURCES.find(s => s.value === src).label))), /*#__PURE__*/React.createElement(Bar, {
    value: importing ? progress : 100
  }), done && /*#__PURE__*/React.createElement(InfoBar, {
    severity: "success",
    title: "Imported 142 connections",
    message: "9 folders \xB7 142 nodes \xB7 18 credentials \u2014 no data loss."
  }))), /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__foot"
  }, step > 0 && step < 2 && /*#__PURE__*/React.createElement(Btn, {
    onClick: () => setStep(step - 1)
  }, "Back"), /*#__PURE__*/React.createElement(Btn, {
    onClick: onClose
  }, done ? "Close" : "Cancel"), step === 0 && /*#__PURE__*/React.createElement(Btn, {
    variant: "accent",
    onClick: () => setStep(1)
  }, "Next"), step === 1 && /*#__PURE__*/React.createElement(Btn, {
    variant: "accent",
    icon: "arrow_download",
    onClick: startImport
  }, "Import"), step === 2 && done && /*#__PURE__*/React.createElement(Btn, {
    variant: "accent",
    onClick: () => onDone && onDone()
  }, "Done"))));
}

/* ---------------- Port scanner ---------------- */
const SCAN_ROWS = [{
  port: 22,
  service: "SSH",
  state: "open"
}, {
  port: 80,
  service: "HTTP",
  state: "open"
}, {
  port: 135,
  service: "MS-RPC",
  state: "filtered"
}, {
  port: 443,
  service: "HTTPS",
  state: "open"
}, {
  port: 445,
  service: "SMB",
  state: "filtered"
}, {
  port: 3389,
  service: "RDP",
  state: "open"
}, {
  port: 5900,
  service: "VNC",
  state: "closed"
}, {
  port: 8080,
  service: "HTTP-alt",
  state: "closed"
}];
function PortScanner({
  onClose
}) {
  const [scanning, setScanning] = tuS(false);
  const [rows, setRows] = tuS([]);
  const [host, setHost] = tuS("10.0.2.0/24");
  const scan = () => {
    setScanning(true);
    setRows([]);
    let i = 0;
    const t = setInterval(() => {
      i++;
      setRows(SCAN_ROWS.slice(0, i));
      if (i >= SCAN_ROWS.length) {
        clearInterval(t);
        setScanning(false);
      }
    }, 260);
  };
  return /*#__PURE__*/React.createElement("div", {
    className: "k-scrim",
    onMouseDown: e => {
      if (e.target === e.currentTarget) onClose();
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog",
    style: {
      width: 600,
      height: 520
    },
    role: "dialog",
    "aria-label": "Port scanner"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__head"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__title"
  }, "Port scanner")), /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__body mr-scroll",
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 12
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      gap: 8,
      alignItems: "flex-end"
    }
  }, /*#__PURE__*/React.createElement(Field, {
    label: "Host / range"
  }, /*#__PURE__*/React.createElement(Input, {
    value: host,
    onChange: e => setHost(e.target.value)
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      width: 140
    }
  }, /*#__PURE__*/React.createElement(Field, {
    label: "Ports"
  }, /*#__PURE__*/React.createElement(Input, {
    value: "1-1024, 3389, 5900",
    onChange: () => {}
  }))), /*#__PURE__*/React.createElement(Btn, {
    variant: "accent",
    icon: scanning ? "stop" : "search",
    onClick: scan
  }, scanning ? "Scanning…" : "Scan")), scanning && /*#__PURE__*/React.createElement(Bar, null), /*#__PURE__*/React.createElement("table", {
    className: "k-table"
  }, /*#__PURE__*/React.createElement("thead", null, /*#__PURE__*/React.createElement("tr", null, /*#__PURE__*/React.createElement("th", {
    style: {
      width: 80
    }
  }, "Port"), /*#__PURE__*/React.createElement("th", null, "Service"), /*#__PURE__*/React.createElement("th", {
    style: {
      width: 120
    }
  }, "State"))), /*#__PURE__*/React.createElement("tbody", null, rows.map(r => /*#__PURE__*/React.createElement("tr", {
    key: r.port
  }, /*#__PURE__*/React.createElement("td", {
    style: {
      fontFamily: "var(--font-mono)"
    }
  }, r.port), /*#__PURE__*/React.createElement("td", null, r.service), /*#__PURE__*/React.createElement("td", null, /*#__PURE__*/React.createElement("span", {
    className: `k-pill k-pill--${r.state}`
  }, r.state)))), !rows.length && !scanning && /*#__PURE__*/React.createElement("tr", null, /*#__PURE__*/React.createElement("td", {
    colSpan: 3,
    style: {
      color: "var(--fill-text-tertiary)",
      padding: "18px 10px"
    }
  }, "Run a scan to discover open ports."))))), /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__foot"
  }, /*#__PURE__*/React.createElement(Btn, {
    onClick: onClose
  }, "Close"), /*#__PURE__*/React.createElement(Btn, {
    variant: "accent",
    icon: "add",
    disabled: !rows.length
  }, "Create connections"))));
}

/* ---------------- Vault credential picker ---------------- */
const VAULT_CREDS = [{
  id: "v1",
  name: "corp\\admin",
  vault: "KeePass · Infrastructure",
  icon: "key"
}, {
  id: "v2",
  name: "root@web-02",
  vault: "HashiCorp Vault · prod/ssh",
  icon: "key"
}, {
  id: "v3",
  name: "svc-backup",
  vault: "KeePass · Service accounts",
  icon: "key"
}, {
  id: "v4",
  name: "netadmin",
  vault: "Thycotic Secret Server",
  icon: "shield_keyhole"
}];
function VaultPicker({
  onClose,
  onPick
}) {
  const [sel, setSel] = tuS("v1");
  const [q, setQ] = tuS("");
  const list = VAULT_CREDS.filter(c => (c.name + c.vault).toLowerCase().includes(q.toLowerCase()));
  return /*#__PURE__*/React.createElement("div", {
    className: "k-scrim",
    onMouseDown: e => {
      if (e.target === e.currentTarget) onClose();
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog",
    style: {
      width: 480,
      height: 460
    },
    role: "dialog",
    "aria-label": "Choose credential"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__head"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__title"
  }, "Choose credential")), /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__body mr-scroll",
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 10
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "mr-textbox"
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "search",
    size: 16
  }), /*#__PURE__*/React.createElement("input", {
    value: q,
    onChange: e => setQ(e.target.value),
    placeholder: "Search vaults\u2026"
  })), /*#__PURE__*/React.createElement("div", null, list.map(c => /*#__PURE__*/React.createElement("div", {
    key: c.id,
    className: `k-vault-item ${sel === c.id ? "k-vault-item--sel" : ""}`,
    onClick: () => setSel(c.id)
  }, /*#__PURE__*/React.createElement("span", {
    className: "k-vault-item__icon"
  }, /*#__PURE__*/React.createElement(Icon, {
    name: c.icon,
    size: 18
  })), /*#__PURE__*/React.createElement("div", {
    className: "k-vault-item__body"
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--text-body)"
    }
  }, c.name), /*#__PURE__*/React.createElement("div", {
    style: {
      font: "var(--text-caption)",
      color: "var(--fill-text-secondary)"
    }
  }, c.vault)), sel === c.id && /*#__PURE__*/React.createElement(Icon, {
    name: "checkmark",
    size: 18,
    style: {
      color: "var(--fill-accent-default)"
    }
  }))))), /*#__PURE__*/React.createElement("div", {
    className: "k-dialog__foot"
  }, /*#__PURE__*/React.createElement(Btn, {
    onClick: onClose
  }, "Cancel"), /*#__PURE__*/React.createElement(Btn, {
    variant: "accent",
    onClick: () => onPick && onPick(VAULT_CREDS.find(c => c.id === sel))
  }, "Use credential"))));
}

/* ---------------- In-session toolbar ---------------- */
function SessionToolbar({
  onDetach,
  onDisconnect,
  onScale
}) {
  return /*#__PURE__*/React.createElement("div", {
    className: "k-sesstoolbar"
  }, /*#__PURE__*/React.createElement("span", {
    className: "k-sesstoolbar__grip"
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "grid_dots",
    size: 16
  })), /*#__PURE__*/React.createElement(IconBtn, {
    icon: "arrow_clockwise",
    label: "Reconnect"
  }), /*#__PURE__*/React.createElement(IconBtn, {
    icon: "full_screen_maximize",
    label: "Full screen"
  }), /*#__PURE__*/React.createElement(IconBtn, {
    icon: "window_new",
    label: "Detach to window",
    onClick: onDetach
  }), /*#__PURE__*/React.createElement("span", {
    className: "k-sesstoolbar__sep"
  }), /*#__PURE__*/React.createElement(IconBtn, {
    icon: "copy",
    label: "Send Ctrl+Alt+Del"
  }), /*#__PURE__*/React.createElement(IconBtn, {
    icon: "save",
    label: "Screenshot"
  }), /*#__PURE__*/React.createElement("span", {
    className: "k-sesstoolbar__sep"
  }), /*#__PURE__*/React.createElement(IconBtn, {
    icon: "plug_disconnected",
    tone: "danger",
    label: "Disconnect",
    onClick: onDisconnect
  }));
}

/* ---------------- Detached floating window ---------------- */
function DetachedWindow({
  session,
  onClose
}) {
  if (!session) return null;
  return /*#__PURE__*/React.createElement("div", {
    className: "k-float"
  }, /*#__PURE__*/React.createElement("div", {
    className: "k-float__bar"
  }, /*#__PURE__*/React.createElement(Icon, {
    name: (PROTOCOLS[session.protocol] || {}).icon || "desktop",
    size: 14
  }), /*#__PURE__*/React.createElement("span", {
    className: "k-float__title"
  }, session.name, " \u2014 ", session.protocol), /*#__PURE__*/React.createElement(IconBtn, {
    icon: "arrow_minimize",
    label: "Re-attach",
    onClick: onClose
  }), /*#__PURE__*/React.createElement("button", {
    className: "k-cap k-cap--close",
    style: {
      width: 32,
      height: 28
    },
    "aria-label": "Close",
    onClick: onClose
  }, /*#__PURE__*/React.createElement("span", {
    className: "k-cap__glyph"
  }))), /*#__PURE__*/React.createElement("div", {
    className: "k-float__body"
  }, /*#__PURE__*/React.createElement(SessionArea, {
    session: session
  })));
}
Object.assign(window, {
  ImportWizard,
  PortScanner,
  VaultPicker,
  SessionToolbar,
  DetachedWindow
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/modernremote/tools.jsx", error: String((e && e.message) || e) }); }

__ds_ns.Button = __ds_scope.Button;

__ds_ns.Icon = __ds_scope.Icon;

__ds_ns.IconButton = __ds_scope.IconButton;

__ds_ns.Expander = __ds_scope.Expander;

__ds_ns.InfoBadge = __ds_scope.InfoBadge;

__ds_ns.InfoBar = __ds_scope.InfoBar;

__ds_ns.ProgressBar = __ds_scope.ProgressBar;

__ds_ns.ProgressRing = __ds_scope.ProgressRing;

__ds_ns.StatusDot = __ds_scope.StatusDot;

__ds_ns.Checkbox = __ds_scope.Checkbox;

__ds_ns.ComboBox = __ds_scope.ComboBox;

__ds_ns.NumberBox = __ds_scope.NumberBox;

__ds_ns.RadioGroup = __ds_scope.RadioGroup;

__ds_ns.Slider = __ds_scope.Slider;

__ds_ns.TextBox = __ds_scope.TextBox;

__ds_ns.ToggleSwitch = __ds_scope.ToggleSwitch;

__ds_ns.TabStrip = __ds_scope.TabStrip;

__ds_ns.TreeItem = __ds_scope.TreeItem;

__ds_ns.TreeView = __ds_scope.TreeView;

__ds_ns.MenuFlyout = __ds_scope.MenuFlyout;

__ds_ns.Tooltip = __ds_scope.Tooltip;

__ds_ns.Card = __ds_scope.Card;

__ds_ns.SettingsCard = __ds_scope.SettingsCard;

})();
