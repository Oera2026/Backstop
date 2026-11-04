// Small JS interop: read text aloud with the browser's built-in speech. No network, no data leaves the device.
window.backstop = {
  scrollToEnd(el) {
    if (!el) return;
    const calm = window.matchMedia("(prefers-reduced-motion: reduce)").matches || document.documentElement.dataset.motion === "calm";
    // Wait two frames so the bubble that was just added has been laid out, then measure.
    requestAnimationFrame(() => requestAnimationFrame(() => {
      // The composer sticks to the bottom of the screen, so stop short of it instead of scrolling behind it.
      const composer = el.closest(".chat")?.querySelector(".composer") ?? document.querySelector(".composer");
      el.style.scrollMarginBottom = composer ? Math.max(0, window.innerHeight - composer.getBoundingClientRect().top) + 8 + "px" : "0px";
      el.scrollIntoView({ block: "end", behavior: calm ? "auto" : "smooth" });
    }));
  },
  // Put keyboard and screen-reader focus on a block and show it from its top. The browser's own focus scroll
  // lands on the end of a block that is taller than the screen.
  focusAtTop(el) {
    if (!el) return;
    const calm = window.matchMedia("(prefers-reduced-motion: reduce)").matches || document.documentElement.dataset.motion === "calm";
    el.focus({ preventScroll: true });
    el.scrollIntoView({ block: "start", behavior: calm ? "auto" : "smooth" });
  },
  canSpeak() {
    return 'speechSynthesis' in window && typeof SpeechSynthesisUtterance !== 'undefined';
  },
  // Some browsers cut a voice off after about 15 seconds, so long text goes out a sentence or two at a time.
  _chunks(text) {
    const MAX = 200, parts = [];
    for (const line of String(text ?? '').split(/\n+/)) {
      for (const sentence of line.match(/[^.!?]+[.!?]*\s*/g) || []) {
        let rest = sentence.trim();
        while (rest.length > MAX) {
          const cut = rest.lastIndexOf(' ', MAX);
          const at = cut > 0 ? cut : MAX;
          parts.push(rest.slice(0, at));
          rest = rest.slice(at).trim();
        }
        if (rest) parts.push(rest);
      }
    }
    const out = [];
    for (const part of parts) {
      if (out.length && out[out.length - 1].length + part.length < MAX) out[out.length - 1] += ' ' + part;
      else out.push(part);
    }
    return out;
  },
  // An en-GB voice, one on the device first. The list can be empty until the browser has loaded it (see voiceschanged below).
  _voice() {
    const b = window.backstop;
    if (b._v === undefined || b._v === null) {
      const gb = window.speechSynthesis.getVoices().filter(v => (v.lang || '').replace('_', '-').toLowerCase() === 'en-gb');
      b._v = gb.find(v => v.localService) || gb[0] || null;
    }
    return b._v;
  },
  // Tells whoever is reading that they have stopped, so their button goes back to "Read this to me".
  _endCurrent() {
    const b = window.backstop, ref = b._spkRef;
    b._spkRef = null;
    if (!ref) return;
    try { ref.invokeMethodAsync('Ended').catch(() => {}); } catch (e) { /* that page has gone */ }
  },
  // Reads text aloud and tells the page when the voice ends (done, stopped, or replaced by another).
  speakWithEnd(ref, text) {
    const b = window.backstop;
    if (!b.canSpeak()) return false;
    const chunks = b._chunks(text);
    if (chunks.length === 0) return false;
    const synth = window.speechSynthesis;
    b._endCurrent();
    const id = (b._spk = (b._spk || 0) + 1);
    synth.cancel();
    b._spkRef = ref;
    let at = 0;
    const finish = () => { if (b._spk === id) b._endCurrent(); };
    const next = () => {
      if (b._spk !== id) return;
      if (at >= chunks.length) { finish(); return; }
      const u = new SpeechSynthesisUtterance(chunks[at++]);
      u.lang = 'en-GB';
      const voice = b._voice();
      if (voice) u.voice = voice;
      u.rate = window.backstopSettings ? window.backstopSettings.speechRate() : 0.9;
      u.onend = next;
      u.onerror = finish;
      synth.speak(u);
    };
    next();
    return true;
  },
  stop() {
    const b = window.backstop;
    b._spk = (b._spk || 0) + 1;
    b._endCurrent();
    if ('speechSynthesis' in window) window.speechSynthesis.cancel();
  },
  // Puts "(3) " in front of the tab title while things wait for the helper. 0 takes it off again.
  setTitleCount(n) {
    const b = window.backstop;
    b._n = n;
    const t = document.querySelector('title');
    if (t && !b._obs) {
      b._obs = new MutationObserver(() => b._paintTitle());
      b._obs.observe(t, { childList: true, characterData: true, subtree: true });
    }
    b._paintTitle();
  },
  _paintTitle() {
    const base = document.title.replace(/^\(\d+\)\s*/, '');
    const want = this._n > 0 ? '(' + this._n + ') ' + base : base;
    if (document.title !== want) document.title = want;
  }
};

// The voice list can arrive after the page opens, and a voice must not carry on after the page has gone.
if ('speechSynthesis' in window) {
  window.speechSynthesis.getVoices();
  if (window.speechSynthesis.addEventListener) window.speechSynthesis.addEventListener('voiceschanged', function () { window.backstop._v = null; });
  window.addEventListener('pagehide', function () { window.speechSynthesis.cancel(); });
}

// Enter in a field marked data-enter-next moves to the next field. It never sends the form.
document.addEventListener('keydown', function (e) {
  var f = e.target;
  if (e.key !== 'Enter' || e.isComposing || !f || !f.dataset || f.dataset.enterNext === undefined) return;
  e.preventDefault();
  var all = Array.prototype.filter.call(f.form ? f.form.elements : [], function (el) {
    return (el.matches('input, textarea, select')) && !el.readOnly && !el.disabled && el.type !== 'hidden';
  });
  var next = all[all.indexOf(f) + 1];
  if (next) next.focus();
});

// The skip link moves focus inside this page. With <base href="/">, following "#main" would go to the Inbox instead.
document.addEventListener('click', function (e) {
  var link = e.target && e.target.closest && e.target.closest('a.skip-link');
  if (!link) return;
  e.preventDefault();
  e.stopPropagation();
  var main = document.getElementById('main');
  if (main) { main.focus(); main.scrollIntoView({ block: 'start' }); }
}, true);

// While the page connects: says it is starting, and offers a reload if that takes too long.
(function () {
  var LONG_MS = 20000;
  function arrived() { return document.getElementById('main'); }
  function done() { document.documentElement.classList.add('booted'); }
  if (arrived()) { done(); return; }
  var slow = setTimeout(function () {
    if (arrived()) return;
    var line = document.getElementById('boot-status'), again = document.getElementById('boot-reload');
    if (line) line.textContent = 'This is taking a long time. Please reload this page.';
    if (again) { again.hidden = false; again.addEventListener('click', function () { location.reload(); }); }
  }, LONG_MS);
  var watch = setInterval(function () {
    if (!arrived()) return;
    clearInterval(watch);
    clearTimeout(slow);
    done();
  }, 250);
})();
