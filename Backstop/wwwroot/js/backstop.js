// Small JS interop: read text aloud with the browser's built-in speech. No network, no data leaves the device.
window.backstop = {
  speak(text) {
    if (!('speechSynthesis' in window)) return false;
    window.speechSynthesis.cancel();
    const u = new SpeechSynthesisUtterance(text);
    u.lang = document.documentElement.lang || 'en-GB';
    u.rate = 0.9;
    window.speechSynthesis.speak(u);
    return true;
  },
  stop() {
    if ('speechSynthesis' in window) window.speechSynthesis.cancel();
  }
};
