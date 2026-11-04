// Device settings (text size, light or dark, contrast, spacing, motion, voice speed). Stored in this browser only.
// Loaded in <head> so the saved look is applied before the page paints.
(function () {
  var KEY = 'backstop.settings';
  var DEFAULTS = { size: 'normal', theme: 'light', contrast: 'standard', spacing: 'normal', motion: 'normal', rate: 'normal' };
  var HKEY = 'backstop.helperWelcomed';
  var RATES = { slow: 0.7, normal: 0.9, fast: 1.1 };

  function read() {
    var saved = {};
    try { saved = JSON.parse(localStorage.getItem(KEY) || '{}') || {}; } catch (e) { /* storage blocked */ }
    return Object.assign({}, DEFAULTS, saved);
  }

  var dark = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

  function apply(s) {
    var root = document.documentElement;
    Object.keys(DEFAULTS).forEach(function (k) { root.dataset[k] = s[k]; });
    // "Like my device" follows the device; the CSS only ever sees light or dark.
    root.dataset.scheme = s.theme === 'dark' || (s.theme === 'auto' && dark && dark.matches) ? 'dark' : 'light';
    var bar = document.querySelector('meta[name="theme-color"]');
    if (bar) bar.setAttribute('content', root.dataset.scheme === 'dark' ? '#131C42' : '#0F4C5C');
  }

  if (dark && dark.addEventListener) dark.addEventListener('change', function () { apply(read()); });

  function write(s) {
    try { localStorage.setItem(KEY, JSON.stringify(s)); } catch (e) { /* storage blocked */ }
    apply(s);
    return s;
  }

  window.backstopSettings = {
    get: read,
    set: function (key, value) { var s = read(); s[key] = value; return write(s); },
    reset: function () { return write(Object.assign({}, DEFAULTS)); },
    speechRate: function () { return RATES[read().rate] || RATES.normal; },
    // First run: the helper welcome shows once per device.
    markHelperWelcomed: function () { try { localStorage.setItem(HKEY, "1"); } catch (e) { /* storage blocked */ } }
  };

  apply(read());

  // Tell the server this device's time zone, so times show as the reader's own clock does.
  try {
    var tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
    if (tz) document.cookie = 'backstop.tz=' + encodeURIComponent(tz) + '; path=/; max-age=31536000; samesite=lax';
  } catch (e) { /* the server's zone is used instead */ }

  // The base address opens the start page, where the visitor picks a side. The demo panel shows pages in frames,
  // so frames are left alone, and the guided walk-around (?tour=1) goes straight to its side.
  if (location.pathname === "/" && window.self === window.top && location.search.indexOf("tour=1") < 0) location.replace("/demo-start");
})();

// First visit to the helper queue goes to the helper welcome. Frames (the demo panel) are left alone.
(function () {
  var done; try { done = localStorage.getItem("backstop.helperWelcomed") === "1"; } catch (e) { done = true; }
  if (location.pathname === "/helper" && window.self === window.top && !done && location.search.indexOf("tour=1") < 0) location.replace("/helper/welcome");
})();

// Inside the demo panel the guide bar would repeat itself, so hide it in frames.
if (window.self !== window.top) document.documentElement.classList.add("framed");
