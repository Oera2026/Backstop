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
