// Demo guide: lights up one thing on the page, dims the rest, and keeps the guide card next to it.
// The page underneath stays clickable.
(function () {
  var spot = null, timer = null, target = null, frame = 0;

  function ensure() {
    if (spot) return spot;
    spot = document.createElement('div');
    spot.className = 'tour-spot';
    spot.setAttribute('aria-hidden', 'true');
    document.body.appendChild(spot);
    return spot;
  }

  function clamp(v, lo, hi) { return Math.max(lo, Math.min(v, hi)); }

  // The card goes beside the lit-up thing: to the right, then the left, then below, then above.
  function placeCard(r) {
    var c = document.querySelector('.tourcard');
    if (!c) return;
    var vw = window.innerWidth, vh = window.innerHeight, m = 12, gap = 16;
    var w = c.offsetWidth, h = c.offsetHeight, x, y;
    if (vw >= 700 && r.right + gap + w + m <= vw) { x = r.right + gap; y = r.top + r.height / 2 - h / 2; }
    else if (vw >= 700 && r.left - gap - w - m >= 0) { x = r.left - gap - w; y = r.top + r.height / 2 - h / 2; }
    else {
      x = r.left + r.width / 2 - w / 2;
      var below = vh - r.bottom, above = r.top;
      y = below >= h + gap + m || below >= above ? r.bottom + gap : r.top - gap - h;
    }
    c.style.left = clamp(x, m, vw - w - m) + 'px';
    c.style.top = clamp(y, m, vh - h - m) + 'px';
    c.classList.add('tourcard--placed');
  }

  function place() {
    if (!target || !spot) return;
    if (!document.body.contains(target)) { hide(); return; }
    var r = target.getBoundingClientRect(), pad = 6;
    spot.style.top = (r.top - pad) + 'px';
    spot.style.left = (r.left - pad) + 'px';
    spot.style.width = (r.width + pad * 2) + 'px';
    spot.style.height = (r.height + pad * 2) + 'px';
    placeCard(r);
  }

  function loop() { place(); frame = requestAnimationFrame(loop); }

  function hide() {
    clearInterval(timer); timer = null; target = null;
    cancelAnimationFrame(frame);
    if (spot) { spot.remove(); spot = null; }
  }

  // Looks for the element for up to 4 seconds (the page may still be drawing).
  function show(selector) {
    hide();
    var tries = 0;
    function find() {
      var el = document.querySelector(selector);
      if (el) {
        clearInterval(timer); timer = null;
        target = el;
        // The page can still be settling (focus, layout), so bring it into view again a moment later.
        [0, 300, 900].forEach(function (ms) { setTimeout(function () { if (target === el && document.body.contains(el)) { try { el.scrollIntoView({ block: "center", behavior: "auto" }); } catch (e) { /* older browsers */ } } }, ms); });
        ensure(); cancelAnimationFrame(frame); loop();
      } else if (++tries > 20) {
        clearInterval(timer); timer = null;
        // Nothing to light up on this page: keep the card in view anyway.
        var c = document.querySelector('.tourcard');
        if (c) { c.style.left = '12px'; c.style.top = Math.max(12, window.innerHeight - c.offsetHeight - 12) + 'px'; c.classList.add('tourcard--placed'); }
      }
    }
    timer = setInterval(find, 200);
    find();
  }

  // "Show me" taps the lit-up link for the visitor. Says whether there was something to tap.
  function tap(selector) {
    var el = document.querySelector(selector);
    if (!el) return false;
    el.click();
    return true;
  }

  // Keeps the place in the guide when the page is reloaded (this tab only).
  function save(value) { try { sessionStorage.setItem("backstop.tour", value); } catch (e) { /* blocked */ } }
  function load() { try { return sessionStorage.getItem("backstop.tour") || ""; } catch (e) { return ""; } }

  window.backstopTour = { show: show, hide: hide, tap: tap, save: save, load: load };
})();
