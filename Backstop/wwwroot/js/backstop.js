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
  // Like speak, but tells the page when the voice ends (done, stopped, or replaced by another). Prefers an en-GB voice.
  speakWithEnd(ref, text) {
    if (!window.backstop.canSpeak()) return false;
    const synth = window.speechSynthesis;
    const id = (window.backstop._spk = (window.backstop._spk || 0) + 1);
    synth.cancel();
    const u = new SpeechSynthesisUtterance(text);
    u.lang = 'en-GB';
    const voice = synth.getVoices().find(v => (v.lang || '').replace('_', '-').toLowerCase() === 'en-gb');
    if (voice) u.voice = voice;
    u.rate = window.backstopSettings ? window.backstopSettings.speechRate() : 0.9;
    const done = () => { if (window.backstop._spk === id) ref.invokeMethodAsync('Ended').catch(() => {}); };
    u.onend = done;
    u.onerror = done;
    synth.speak(u);
    return true;
  },
  speak(text) {
    if (!('speechSynthesis' in window)) return false;
    window.speechSynthesis.cancel();
    const u = new SpeechSynthesisUtterance(text);
    u.lang = document.documentElement.lang || 'en-GB';
    u.rate = window.backstopSettings ? window.backstopSettings.speechRate() : 0.9;
    window.speechSynthesis.speak(u);
    return true;
  },
  stop() {
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

// Sign in: "Show my password" turns the password box into a plain text box and back.
document.addEventListener('change', function (e) {
  var box = e.target;
  if (!box || !box.dataset || !box.dataset.showPassword) return;
  var field = document.getElementById(box.dataset.showPassword);
  if (field) field.type = box.checked ? 'text' : 'password';
});

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
