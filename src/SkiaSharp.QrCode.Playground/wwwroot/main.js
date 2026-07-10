import { dotnet } from './_framework/dotnet.js';
import {
  decodeShareHash,
  encodeShareState,
  isShareWithinLimits,
} from './share-payload.js';

/* ─── Theme ─── */

const THEME_STORAGE_KEY = 'skqr-playground-color-mode';
const THEME_CYCLE_ORDER = ['system', 'light', 'dark'];

function colorSchemeDarkQuery() {
  return window.matchMedia('(prefers-color-scheme: dark)');
}

function getStoredColorMode() {
  try {
    const v = localStorage.getItem(THEME_STORAGE_KEY);
    if (v === 'light' || v === 'dark') return v;
  } catch (_) {
    /* ignore */
  }
  return 'system';
}

function setStoredColorMode(mode) {
  try {
    if (mode === 'system') localStorage.removeItem(THEME_STORAGE_KEY);
    else localStorage.setItem(THEME_STORAGE_KEY, mode);
  } catch (_) {
    /* ignore */
  }
}

function applyColorModeToDocument(mode) {
  const root = document.documentElement;
  if (mode === 'light') root.setAttribute('data-theme', 'light');
  else if (mode === 'dark') root.setAttribute('data-theme', 'dark');
  else root.removeAttribute('data-theme');
  const meta = document.getElementById('meta-color-scheme');
  if (meta) {
    if (mode === 'light') meta.setAttribute('content', 'light');
    else if (mode === 'dark') meta.setAttribute('content', 'dark');
    else meta.setAttribute('content', 'light dark');
  }
}

function themeAccessibilityLabel(mode) {
  const suffix = 'Click to cycle: System, Light, Dark.';
  if (mode === 'light') return `Color theme: Light. ${suffix}`;
  if (mode === 'dark') return `Color theme: Dark. ${suffix}`;
  return `Color theme: System. ${suffix}`;
}

const themeCycleBtn = document.getElementById('theme-cycle-btn');

function updateThemeCycleButton() {
  if (!themeCycleBtn) return;
  const mode = getStoredColorMode();
  themeCycleBtn.dataset.themeMode = mode;
  themeCycleBtn.setAttribute('aria-label', themeAccessibilityLabel(mode));
}

if (themeCycleBtn) {
  themeCycleBtn.addEventListener('click', () => {
    const cur = getStoredColorMode();
    const i = Math.max(0, THEME_CYCLE_ORDER.indexOf(cur));
    const next = THEME_CYCLE_ORDER[(i + 1) % THEME_CYCLE_ORDER.length];
    setStoredColorMode(next);
    applyColorModeToDocument(next);
    updateThemeCycleButton();
  });
}
updateThemeCycleButton();
// System-mode visuals follow prefers-color-scheme via CSS; refresh the button label on change.
colorSchemeDarkQuery().addEventListener('change', updateThemeCycleButton);

/* ─── Toasts ─── */

/** @typedef {'error'|'success'|'info'} ToastVariant */
/** @type {Record<ToastVariant, number>} */
const TOAST_DURATION_MS = { error: 8000, success: 3800, info: 4200 };
const toastStack = document.getElementById('toast-stack');

/**
 * @param {string} message
 * @param {ToastVariant} [variant]
 * @param {number} [durationMs]
 */
function showToast(message, variant = 'info', durationMs) {
  if (!toastStack) return;
  const ms = durationMs ?? TOAST_DURATION_MS[variant] ?? TOAST_DURATION_MS.info;

  const wrap = document.createElement('div');
  wrap.className = `toast toast--${variant}`;
  wrap.setAttribute('role', variant === 'error' ? 'alert' : 'status');

  const bodyEl = document.createElement('div');
  bodyEl.className = 'toast__body';
  bodyEl.textContent = message ?? '';

  const dismissBtn = document.createElement('button');
  dismissBtn.type = 'button';
  dismissBtn.className = 'toast__dismiss';
  dismissBtn.setAttribute('aria-label', 'Dismiss notification');
  dismissBtn.textContent = '✕';

  wrap.append(bodyEl, dismissBtn);
  toastStack.appendChild(wrap);
  requestAnimationFrame(() => wrap.classList.add('toast--show'));

  const hideTimer = window.setTimeout(() => removeToastElement(wrap), ms);
  dismissBtn.addEventListener('click', () => {
    window.clearTimeout(hideTimer);
    removeToastElement(wrap);
  });
}

/** @param {HTMLElement} el */
function removeToastElement(el) {
  if (!el?.parentElement || el.dataset.toastClosing) return;
  el.dataset.toastClosing = '1';
  el.classList.remove('toast--show');
  el.classList.add('toast--out');
  window.setTimeout(() => el.remove(), 240);
}

/* ─── Element references ─── */

const loading = document.getElementById('loading');
const versionEl = document.getElementById('playground-version');
const contentEl = document.getElementById('content');
const presetSelect = document.getElementById('preset-select');
const eccSelect = document.getElementById('ecc-select');
const versionSelect = document.getElementById('version-select');
const sizeRange = document.getElementById('size-range');
const sizeOut = document.getElementById('size-out');
const quietRange = document.getElementById('quiet-range');
const quietOut = document.getElementById('quiet-out');
const moduleShapeSelect = document.getElementById('module-shape-select');
const moduleSizeRange = document.getElementById('module-size-range');
const moduleSizeOut = document.getElementById('module-size-out');
const cornerRow = document.getElementById('corner-row');
const cornerRange = document.getElementById('corner-range');
const cornerOut = document.getElementById('corner-out');
const finderSelect = document.getElementById('finder-select');
const fgRow = document.getElementById('fg-row');
const fgColor = document.getElementById('fg-color');
const bgColor = document.getElementById('bg-color');
const bgTransparent = document.getElementById('bg-transparent');
const gradientToggle = document.getElementById('gradient-toggle');
const gradientPanel = document.getElementById('gradient-panel');
const gradientColorsEl = document.getElementById('gradient-colors');
const gradientAddBtn = document.getElementById('gradient-add-btn');
const gradientDirection = document.getElementById('gradient-direction');
const logoModeSelect = document.getElementById('logo-mode-select');
const logoFileRow = document.getElementById('logo-file-row');
const logoFile = document.getElementById('logo-file');
const logoSizeRange = document.getElementById('logo-size-range');
const logoSizeOut = document.getElementById('logo-size-out');
const logoBorderRange = document.getElementById('logo-border-range');
const logoBorderOut = document.getElementById('logo-border-out');
const previewImg = document.getElementById('qr-preview');
const statsEl = document.getElementById('qr-stats');
const generateErrorEl = document.getElementById('generate-error');
const downloadBtn = document.getElementById('download-btn');
const copyImageBtn = document.getElementById('copy-image-btn');
const permalinkBtn = document.getElementById('permalink-btn');
const benchModeSelect = document.getElementById('bench-mode-select');
const benchCountSelect = document.getElementById('bench-count-select');
const benchRunBtn = document.getElementById('bench-run-btn');
const benchCancelBtn = document.getElementById('bench-cancel-btn');
const benchProgress = document.getElementById('bench-progress');
const benchResult = document.getElementById('bench-result');

// Version select: auto + 1..40
{
  const auto = document.createElement('option');
  auto.value = '-1';
  auto.textContent = 'Auto';
  versionSelect.appendChild(auto);
  for (let v = 1; v <= 40; v++) {
    const opt = document.createElement('option');
    opt.value = String(v);
    opt.textContent = `${v} (${17 + 4 * v}×${17 + 4 * v})`;
    versionSelect.appendChild(opt);
  }
  versionSelect.value = '-1';
}

/* ─── Presets ─── */

const PRESETS = {
  instagram: {
    ecc: 'H',
    moduleShape: 'rounded',
    moduleSizePercent: 0.92,
    moduleCornerRadius: 0.45,
    finderShape: 'roundedCircle',
    foreground: '#111111',
    background: '#ffffff',
    gradientEnabled: true,
    gradientColors: ['#F77737', '#E1306C', '#833AB4', '#5851DB'],
    gradientDirection: 'BottomLeftToTopRight',
    logoMode: 'default',
    logoSizePercent: 18,
    logoBorderWidth: 6,
  },
  classic: {
    ecc: 'M',
    moduleShape: 'rectangle',
    moduleSizePercent: 1.0,
    moduleCornerRadius: 0.3,
    finderShape: 'auto',
    foreground: '#000000',
    background: '#ffffff',
    gradientEnabled: false,
    gradientColors: ['#000000', '#444444'],
    gradientDirection: 'TopLeftToBottomRight',
    logoMode: 'none',
    logoSizePercent: 18,
    logoBorderWidth: 6,
  },
  midnight: {
    ecc: 'Q',
    moduleShape: 'circle',
    moduleSizePercent: 0.95,
    moduleCornerRadius: 0.3,
    finderShape: 'roundedCircle',
    foreground: '#22d3ee',
    background: '#0b1220',
    gradientEnabled: true,
    gradientColors: ['#22d3ee', '#818cf8', '#e879f9'],
    gradientDirection: 'TopToBottom',
    logoMode: 'none',
    logoSizePercent: 18,
    logoBorderWidth: 6,
  },
  ocean: {
    ecc: 'Q',
    moduleShape: 'rounded',
    moduleSizePercent: 0.95,
    moduleCornerRadius: 0.6,
    finderShape: 'rounded',
    foreground: '#0c4a6e',
    background: '#f0f9ff',
    gradientEnabled: true,
    gradientColors: ['#0ea5e9', '#0369a1', '#1e3a8a'],
    gradientDirection: 'TopLeftToBottomRight',
    logoMode: 'none',
    logoSizePercent: 18,
    logoBorderWidth: 6,
  },
  sunset: {
    ecc: 'Q',
    moduleShape: 'rounded',
    moduleSizePercent: 0.9,
    moduleCornerRadius: 0.35,
    finderShape: 'rounded',
    foreground: '#7c2d12',
    background: '#fffbeb',
    gradientEnabled: true,
    gradientColors: ['#b45309', '#dc2626', '#9d174d'],
    gradientDirection: 'BottomToTop',
    logoMode: 'none',
    logoSizePercent: 18,
    logoBorderWidth: 6,
  },
};

/* ─── State <-> DOM ─── */

const EMPTY_BYTES = new Uint8Array(0);
/** Uploaded logo image bytes (not part of share links). */
let customLogoBytes = EMPTY_BYTES;
/** True while applyStateToControls runs, so input events do not flip the preset to (custom). */
let applyingState = false;

/** @returns {object} playground state; doubles as the WASM QrRequest payload (camelCase) */
function collectState() {
  return {
    content: contentEl.value,
    ecc: eccSelect.value,
    size: Number(sizeRange.value),
    quietZone: Number(quietRange.value),
    version: Number(versionSelect.value),
    moduleShape: moduleShapeSelect.value,
    moduleSizePercent: Number(moduleSizeRange.value) / 100,
    moduleCornerRadius: Number(cornerRange.value) / 100,
    finderShape: finderSelect.value,
    foreground: fgColor.value,
    background: bgTransparent.checked ? 'transparent' : bgColor.value,
    gradient: {
      enabled: gradientToggle.checked,
      colors: getGradientColors(),
      direction: gradientDirection.value,
    },
    logo: {
      mode: logoModeSelect.value,
      sizePercent: Number(logoSizeRange.value),
      borderWidth: Number(logoBorderRange.value),
    },
  };
}

/** @param {object} state partial state (missing fields keep current control values) */
function applyStateToControls(state) {
  applyingState = true;
  try {
    if (typeof state.content === 'string') contentEl.value = state.content;
    if (state.ecc) eccSelect.value = state.ecc;
    if (Number.isFinite(state.size)) sizeRange.value = String(clamp(state.size, 128, 1024));
    if (Number.isFinite(state.quietZone)) quietRange.value = String(clamp(state.quietZone, 0, 10));
    if (Number.isFinite(state.version)) versionSelect.value = String(state.version);
    if (state.moduleShape) moduleShapeSelect.value = state.moduleShape;
    if (Number.isFinite(state.moduleSizePercent)) moduleSizeRange.value = String(Math.round(clamp(state.moduleSizePercent, 0.5, 1) * 100));
    if (Number.isFinite(state.moduleCornerRadius)) cornerRange.value = String(Math.round(clamp(state.moduleCornerRadius, 0, 1) * 100));
    if (state.finderShape) finderSelect.value = state.finderShape;
    if (state.foreground) fgColor.value = state.foreground;
    if (state.background === 'transparent') {
      bgTransparent.checked = true;
    } else if (state.background) {
      bgTransparent.checked = false;
      bgColor.value = state.background;
    }
    if (state.gradient) {
      if (typeof state.gradient.enabled === 'boolean') gradientToggle.checked = state.gradient.enabled;
      if (Array.isArray(state.gradient.colors) && state.gradient.colors.length >= 2) {
        renderGradientColors(state.gradient.colors);
      }
      if (state.gradient.direction) gradientDirection.value = state.gradient.direction;
    }
    if (state.logo) {
      if (state.logo.mode) logoModeSelect.value = state.logo.mode;
      if (Number.isFinite(state.logo.sizePercent)) logoSizeRange.value = String(clamp(state.logo.sizePercent, 5, 35));
      if (Number.isFinite(state.logo.borderWidth)) logoBorderRange.value = String(clamp(state.logo.borderWidth, 0, 24));
    }
    syncDerivedControls();
  } finally {
    applyingState = false;
  }
}

/** Flat preset entry → state shape used by applyStateToControls / share links. */
function presetToState(preset) {
  return {
    ecc: preset.ecc,
    moduleShape: preset.moduleShape,
    moduleSizePercent: preset.moduleSizePercent,
    moduleCornerRadius: preset.moduleCornerRadius,
    finderShape: preset.finderShape,
    foreground: preset.foreground,
    background: preset.background,
    gradient: {
      enabled: preset.gradientEnabled,
      colors: preset.gradientColors,
      direction: preset.gradientDirection,
    },
    logo: {
      mode: preset.logoMode,
      sizePercent: preset.logoSizePercent,
      borderWidth: preset.logoBorderWidth,
    },
  };
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

/** Updates range outputs and conditional row visibility from current control values. */
function syncDerivedControls() {
  sizeOut.textContent = sizeRange.value;
  quietOut.textContent = quietRange.value;
  moduleSizeOut.textContent = moduleSizeRange.value;
  cornerOut.textContent = cornerRange.value;
  logoSizeOut.textContent = logoSizeRange.value;
  logoBorderOut.textContent = logoBorderRange.value;
  cornerRow.hidden = moduleShapeSelect.value !== 'rounded';
  gradientPanel.hidden = !gradientToggle.checked;
  fgRow.classList.toggle('field--disabled', gradientToggle.checked);
  fgColor.disabled = gradientToggle.checked;
  bgColor.disabled = bgTransparent.checked;
  logoFileRow.hidden = logoModeSelect.value !== 'custom';
}

/* ─── Gradient color list ─── */

const GRADIENT_MIN_COLORS = 2;
const GRADIENT_MAX_COLORS = 6;

function getGradientColors() {
  return [...gradientColorsEl.querySelectorAll('input[type="color"]')].map((i) => i.value);
}

/** @param {string[]} colors */
function renderGradientColors(colors) {
  gradientColorsEl.replaceChildren();
  for (const color of colors.slice(0, GRADIENT_MAX_COLORS)) {
    gradientColorsEl.appendChild(createGradientColorRow(color));
  }
  syncGradientButtons();
}

/** @param {string} color */
function createGradientColorRow(color) {
  const row = document.createElement('div');
  row.className = 'gradient-color-row';

  const input = document.createElement('input');
  input.type = 'color';
  input.value = normalizeHexColor(color);

  const remove = document.createElement('button');
  remove.type = 'button';
  remove.className = 'small-btn gradient-color-row__remove';
  remove.textContent = '✕';
  remove.setAttribute('aria-label', 'Remove this gradient color');
  remove.addEventListener('click', () => {
    if (gradientColorsEl.children.length <= GRADIENT_MIN_COLORS) return;
    row.remove();
    syncGradientButtons();
    markCustomPreset();
    scheduleGenerate();
  });

  row.append(input, remove);
  return row;
}

function syncGradientButtons() {
  const count = gradientColorsEl.children.length;
  gradientAddBtn.disabled = count >= GRADIENT_MAX_COLORS;
  for (const btn of gradientColorsEl.querySelectorAll('.gradient-color-row__remove')) {
    btn.disabled = count <= GRADIENT_MIN_COLORS;
  }
}

/** <input type=color> requires #rrggbb; anything unparsable falls back to black. */
function normalizeHexColor(value) {
  return /^#[0-9a-fA-F]{6}$/.test(value) ? value.toLowerCase() : '#000000';
}

gradientAddBtn.addEventListener('click', () => {
  if (gradientColorsEl.children.length >= GRADIENT_MAX_COLORS) return;
  const colors = getGradientColors();
  gradientColorsEl.appendChild(createGradientColorRow(colors[colors.length - 1] ?? '#833ab4'));
  syncGradientButtons();
  markCustomPreset();
  scheduleGenerate();
});

/* ─── WASM runtime ─── */

let exports = null;
let runtimeReady = false;
let runtimeAlive = true;

const RELEASE_TAG_BASE_URL = 'https://github.com/guitarrapc/SkiaSharp.QrCode/releases/tag/';

function isRuntimeDeadError(err) {
  if (!err) return false;
  const msg = String(err?.message ?? err).toLowerCase();
  return msg.includes('.net runtime already exited')
    || msg.includes('runtime already exited')
    || msg.includes('runtime has already exited');
}

function handleRuntimeDeath() {
  runtimeAlive = false;
  showToast('The WebAssembly runtime has crashed. Please reload the page to continue.', 'error', 60000);
  generateErrorEl.hidden = false;
  generateErrorEl.textContent = 'Runtime crashed — please reload the page.';
}

function syncVersionBadge() {
  if (!exports || !versionEl) return;
  try {
    const v = exports.SkiaSharp.QrCode.Playground.QrInterop.GetProductVersion();
    if (typeof v === 'string' && v.length > 0 && v !== 'unknown') {
      versionEl.textContent = `v${v}`;
      versionEl.href = RELEASE_TAG_BASE_URL + encodeURIComponent(v);
      versionEl.setAttribute('aria-label', `Release ${v} — open on GitHub`);
      versionEl.hidden = false;
    }
  } catch {
    /* ignore — older bundles or trimmed exports */
  }
}

/* ─── Generation ─── */

const utf8Decoder = new TextDecoder();
/** Most recent successful PNG, backing Download / Copy image. */
let lastPngBytes = null;
let lastObjectUrl = null;
let generateTimer = null;

// setTimeout (not requestAnimationFrame): rAF does not fire in hidden/background tabs,
// which would silently stall regeneration. 60ms coalesces slider drags nicely.
const GENERATE_DEBOUNCE_MS = 60;

function scheduleGenerate() {
  if (generateTimer !== null) return;
  generateTimer = window.setTimeout(() => {
    generateTimer = null;
    generate();
  }, GENERATE_DEBOUNCE_MS);
}

function generate() {
  if (!runtimeAlive || !runtimeReady || !exports) return;

  const state = collectState();
  if (!state.content.trim()) {
    showGenerateError('Enter content to encode.');
    return;
  }

  const logoBytes = state.logo.mode === 'custom' ? customLogoBytes : EMPTY_BYTES;
  let bytes;
  const t0 = performance.now();
  try {
    bytes = exports.SkiaSharp.QrCode.Playground.QrInterop.Generate(JSON.stringify(state), logoBytes);
  } catch (err) {
    if (isRuntimeDeadError(err)) {
      handleRuntimeDeath();
      return;
    }
    showGenerateError(err?.message ?? String(err));
    return;
  }
  const interopMs = performance.now() - t0;

  // Error envelope is UTF-8 JSON starting with '{'; PNG always starts with 0x89.
  if (bytes.length === 0 || bytes[0] === 0x7b) {
    let message = 'QR generation failed.';
    try {
      message = JSON.parse(utf8Decoder.decode(bytes)).error ?? message;
    } catch {
      /* keep generic message */
    }
    showGenerateError(message);
    return;
  }

  lastPngBytes = bytes;
  const blob = new Blob([bytes], { type: 'image/png' });
  const url = URL.createObjectURL(blob);
  if (lastObjectUrl) URL.revokeObjectURL(lastObjectUrl);
  lastObjectUrl = url;
  previewImg.src = url;
  previewImg.hidden = false;
  generateErrorEl.hidden = true;
  downloadBtn.disabled = false;
  copyImageBtn.disabled = false;

  try {
    const meta = JSON.parse(exports.SkiaSharp.QrCode.Playground.QrInterop.GetLastMeta());
    statsEl.textContent =
      `QR version ${meta.qrVersion} · ${meta.matrixSize}×${meta.matrixSize} modules · `
      + `${meta.totalMs} ms in WASM (${interopMs.toFixed(1)} ms total) · ${formatBytes(meta.bytes)}`;
  } catch {
    statsEl.textContent = '';
  }
}

/** Shows an inline error while keeping the last good image visible. */
function showGenerateError(message) {
  generateErrorEl.hidden = false;
  generateErrorEl.textContent = message;
}

function formatBytes(n) {
  if (!Number.isFinite(n)) return '';
  if (n < 1024) return `${n} B`;
  return `${(n / 1024).toFixed(1)} KB`;
}

/* ─── Control events ─── */

function markCustomPreset() {
  if (!applyingState) presetSelect.value = 'custom';
}

for (const el of [
  contentEl, eccSelect, versionSelect, sizeRange, quietRange,
  moduleShapeSelect, moduleSizeRange, cornerRange, finderSelect,
  fgColor, bgColor, bgTransparent,
  gradientToggle, gradientDirection,
  logoModeSelect, logoSizeRange, logoBorderRange,
]) {
  el.addEventListener('input', () => {
    syncDerivedControls();
    markCustomPreset();
    scheduleGenerate();
  });
}

// Color inputs inside the dynamic gradient list (event delegation).
gradientColorsEl.addEventListener('input', () => {
  markCustomPreset();
  scheduleGenerate();
});

presetSelect.addEventListener('input', () => {
  const preset = PRESETS[presetSelect.value];
  if (!preset) return;
  applyStateToControls(presetToState(preset));
  scheduleGenerate();
});

logoFile.addEventListener('change', async () => {
  const file = logoFile.files?.[0];
  if (!file) return;
  try {
    customLogoBytes = new Uint8Array(await file.arrayBuffer());
    logoModeSelect.value = 'custom';
    syncDerivedControls();
    markCustomPreset();
    scheduleGenerate();
  } catch (e) {
    showToast(`Could not read the image file: ${e?.message ?? e}`, 'error');
  }
});

/* ─── Download / copy image ─── */

downloadBtn.addEventListener('click', () => {
  if (!lastPngBytes) return;
  const blob = new Blob([lastPngBytes], { type: 'image/png' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = 'qrcode.png';
  document.body.appendChild(a);
  a.click();
  a.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
});

copyImageBtn.addEventListener('click', async () => {
  if (!lastPngBytes) return;
  try {
    const blob = new Blob([lastPngBytes], { type: 'image/png' });
    await navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]);
    showToast('Image copied to clipboard.', 'success');
  } catch (e) {
    showToast(`Could not copy image: ${e?.message ?? e}`, 'error');
  }
});

/* ─── Share link ─── */

/**
 * Synchronous clipboard fallback (helps while the originating click is still a "user gesture").
 * @param {string} text
 * @returns {boolean}
 */
function tryClipboardCopyViaTextArea(text) {
  const ta = document.createElement('textarea');
  ta.value = text;
  ta.setAttribute('readonly', '');
  ta.style.position = 'fixed';
  ta.style.left = '-9999px';
  ta.style.top = '0';
  document.body.appendChild(ta);
  try {
    ta.focus();
    ta.select();
    return document.execCommand('copy');
  } catch {
    return false;
  } finally {
    ta.remove();
  }
}

/** @param {string} text */
async function copyTextToClipboard(text) {
  if (tryClipboardCopyViaTextArea(text)) return true;
  const w = navigator.clipboard?.writeText;
  if (!w) return false;
  try {
    await w.call(navigator.clipboard, text);
    return true;
  } catch {
    return false;
  }
}

permalinkBtn.addEventListener('click', async () => {
  try {
    const state = collectState();
    if (state.logo.mode === 'custom') {
      // Uploaded image bytes do not fit in a URL; the link falls back to the built-in logo.
      state.logo = { ...state.logo, mode: 'default' };
      showToast('Uploaded logo images are not included in share links — the link uses the built-in logo instead.', 'info');
    }
    const hash = await encodeShareState(state);
    const url = `${location.pathname}${location.search}#${hash}`;
    const fullUrl = `${location.origin}${url}`;
    if (!isShareWithinLimits(hash, fullUrl)) {
      showToast('Content is too long to fit in a share URL. Shorten the QR content and try again.', 'error');
      return;
    }
    history.replaceState(null, '', url);
    const copied = await copyTextToClipboard(location.href);
    showToast(
      copied ? 'Link copied to clipboard.' : 'URL updated — copy from the address bar if clipboard was blocked.',
      copied ? 'success' : 'info',
    );
  } catch (e) {
    showToast(e?.message ?? String(e), 'error');
  }
});

/* ─── Performance benchmark ─── */

/** Target wall-clock per BenchmarkBatch call; keeps the UI responsive between batches. */
const BENCH_BATCH_TARGET_MS = 150;

let benchRunning = false;
let benchCancelled = false;

function formatRate(count, ms) {
  if (ms <= 0) return '—';
  const rate = (count / ms) * 1000;
  return `${rate >= 100 ? Math.round(rate).toLocaleString() : rate.toFixed(1)} codes/s`;
}

function setBenchControlsRunning(running) {
  benchRunBtn.disabled = running || !runtimeReady || !runtimeAlive;
  benchCancelBtn.hidden = !running;
  benchModeSelect.disabled = running;
  benchCountSelect.disabled = running;
}

benchCancelBtn.addEventListener('click', () => {
  benchCancelled = true;
  benchCancelBtn.disabled = true;
});

benchRunBtn.addEventListener('click', () => void runBenchmark());

async function runBenchmark() {
  if (benchRunning || !runtimeReady || !runtimeAlive) return;

  const state = collectState();
  if (!state.content.trim()) {
    showToast('Enter content to encode before running the benchmark.', 'info');
    return;
  }
  // Snapshot options once so mid-run control edits do not skew results.
  const optionsJson = JSON.stringify(state);
  const mode = benchModeSelect.value;
  const total = Number(benchCountSelect.value);
  const logoBytes = mode === 'render' && state.logo.mode === 'custom' ? customLogoBytes : EMPTY_BYTES;

  benchRunning = true;
  benchCancelled = false;
  benchCancelBtn.disabled = false;
  setBenchControlsRunning(true);
  benchProgress.hidden = false;
  benchProgress.max = total;
  benchProgress.value = 0;
  benchResult.textContent = 'Running…';

  let done = 0;
  let wasmMs = 0;
  let meta = null;
  // Encode mode is orders of magnitude faster than render mode; start small and adapt.
  let batch = mode === 'encode' ? 128 : 4;
  const wallStart = performance.now();

  try {
    while (done < total && !benchCancelled) {
      const count = Math.min(batch, total - done);
      const json = exports.SkiaSharp.QrCode.Playground.QrInterop.BenchmarkBatch(optionsJson, mode, done, count, logoBytes);
      const result = JSON.parse(json);
      if (result.error) {
        benchResult.textContent = `Failed: ${result.error}`;
        break;
      }
      done += result.count;
      wasmMs += result.elapsedMs;
      meta = result;

      benchProgress.value = done;
      benchResult.textContent = `${done.toLocaleString()} / ${total.toLocaleString()} — ${formatRate(done, wasmMs)}`;

      // Resize the next batch toward the wall-clock target (bounded so one batch cannot stall the tab).
      const perItem = Math.max(result.elapsedMs / result.count, 0.0001);
      batch = Math.max(1, Math.min(50000, Math.round(BENCH_BATCH_TARGET_MS / perItem)));

      // Yield to the event loop so progress paints and Cancel stays clickable.
      await new Promise((resolve) => setTimeout(resolve, 0));
    }

    if (done > 0 && meta && !benchResult.textContent.startsWith('Failed')) {
      const wallSeconds = ((performance.now() - wallStart) / 1000).toFixed(2);
      const avgMs = wasmMs / done;
      const prefix = benchCancelled ? `Cancelled — ${done.toLocaleString()} of ${total.toLocaleString()}` : done.toLocaleString();
      benchResult.textContent =
        `${prefix} codes in ${(wasmMs / 1000).toFixed(2)} s of WASM time — ${formatRate(done, wasmMs)} `
        + `(avg ${avgMs >= 1 ? avgMs.toFixed(2) : avgMs.toFixed(3)} ms/code, QR v${meta.qrVersion} ${meta.matrixSize}×${meta.matrixSize}, wall ${wallSeconds} s)`;
    }
  } catch (err) {
    if (isRuntimeDeadError(err)) {
      handleRuntimeDeath();
    } else {
      benchResult.textContent = `Failed: ${err?.message ?? err}`;
    }
  } finally {
    benchRunning = false;
    setBenchControlsRunning(false);
    benchProgress.hidden = true;
  }
}

/* ─── Startup ─── */

async function restoreFromLocationHash() {
  const raw = window.location.hash;
  if (!raw || raw.length <= 1) return false;
  const decoded = await decodeShareHash(raw.slice(1));
  if (!decoded.ok) {
    showToast(`Could not restore from URL: ${decoded.error}. Showing defaults.`, 'info', 6000);
    return false;
  }
  applyStateToControls(decoded.state);
  presetSelect.value = 'custom';
  return true;
}

async function initializeRuntime() {
  try {
    const restored = await restoreFromLocationHash();
    if (!restored) {
      applyStateToControls(presetToState(PRESETS.instagram));
    }

    const runtime = await dotnet
      .withApplicationArguments('playground')
      .create();
    const config = runtime.getConfig();
    exports = await runtime.getAssemblyExports(config.mainAssemblyName);
    await runtime.runMain();
    runtimeReady = true;
    syncVersionBadge();
    loading.style.display = 'none';
    permalinkBtn.disabled = false;
    benchRunBtn.disabled = false;
    generate();
  } catch (err) {
    if (isRuntimeDeadError(err)) {
      handleRuntimeDeath();
      return;
    }
    runtimeAlive = false;
    runtimeReady = false;
    loading.style.display = 'none';
    showToast(err?.message ?? String(err), 'error', 60000);
  }
}

renderGradientColors(PRESETS.instagram.gradientColors);
syncDerivedControls();
void initializeRuntime();
