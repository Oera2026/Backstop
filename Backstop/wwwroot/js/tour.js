// Demo guide: lights up one thing on the page, dims the rest, and keeps the guide card next to it.
// The page underneath stays clickable.
(function () {
  var spot = null, timer = null, target = null, frame = 0;
  var PHONE = 700, GAP = 16, M = 12;

  function ensure() {
    if (spot) return spot;
    spot = document.createElement('div');
    spot.className = 'tour-spot';
    spot.setAttribute('aria-hidden', 'true');
    document.body.appendChild(spot);
    return spot;
  }

  function clamp(v, lo, hi) { return Math.max(lo, Math.min(v, hi)); }

  // Fixed or sticky things (the tab bar, the decision bar) stay where they are when the page scrolls.
  function pinned(el) {
    for (var n = el, i = 0; n && n !== document.body && i < 4; n = n.parentElement, i++) {
      var p = getComputedStyle(n).position;
      if (p === "fixed" || p === "sticky") return true;
    }
    return false;
  }

  // How much of the bottom of the screen the tab bar covers on a phone.
  function tabBarHeight() {
    var nav = document.querySelector('.appnav');
    return nav && getComputedStyle(nav).position === 'fixed' ? nav.offsetHeight : 0;
  }

  // Where the card goes. Wide screens: beside the lit-up thing. Phones: pinned at the bottom, above the tab bar,
  // with the lit-up thing scrolled into the space above it. Things that live in the tab bar get the card at the top.
  function cardPlace(c, r) {
    var vw = window.innerWidth, vh = window.innerHeight, w = c.offsetWidth, h = c.offsetHeight, x, y, zone = 'free';
    if (vw >= PHONE) {
      if (r.right + GAP + w + M <= vw) { x = r.right + GAP; y = r.top + r.height / 2 - h / 2; }
      else if (r.left - GAP - w - M >= 0) { x = r.left - GAP - w; y = r.top + r.height / 2 - h / 2; }
      else {
        x = r.left + r.width / 2 - w / 2;
        var below = vh - r.bottom, above = r.top;
        y = below >= h + GAP + M || below >= above ? r.bottom + GAP : r.top - GAP - h;
      }
    } else {
      x = (vw - w) / 2;
      if (r.top > vh * 0.6 || (pinned(target) && r.top > vh * 0.3)) { y = 72; zone = "top"; }
      else { y = vh - tabBarHeight() - h - M; zone = 'bottom'; }
    }
    return { x: clamp(x, M, vw - w - M), y: clamp(y, M, vh - h - M), zone: zone };
  }

  function place() {
    if (!target || !spot) return;
    if (!document.body.contains(target)) { hide(); return; }
    var r = target.getBoundingClientRect(), pad = 6;
    var top = r.top - pad, height = r.height + pad * 2;
    var c = document.querySelector('.tourcard');
    if (c) {
      var p = cardPlace(c, r);
      c.style.left = p.x + 'px';
      c.style.top = p.y + 'px';
      c.classList.add('tourcard--placed');
      // On a phone the lit-up area stops where the card starts, so it never hides under it.
      if (p.zone === 'bottom' && top + height > p.y - 8) height = Math.max(40, p.y - 8 - top);
      if (p.zone === 'top' && top < p.y + c.offsetHeight + 8) { var cut = p.y + c.offsetHeight + 8 - top; top += cut; height = Math.max(40, height - cut); }
    }
    spot.style.top = top + 'px';
    spot.style.left = (r.left - pad) + 'px';
    spot.style.width = (r.width + pad * 2) + 'px';
    spot.style.height = height + 'px';
  }

  function loop() { place(); frame = requestAnimationFrame(loop); }

  function hide() {
    clearInterval(timer); timer = null; target = null;
    cancelAnimationFrame(frame);
    document.documentElement.classList.remove('tour-on');
    if (spot) { spot.remove(); spot = null; }
  }

  // Brings the lit-up thing into view. On a phone it goes near the top, clear of the card at the bottom.
  function bring(el) {
    if (!document.body.contains(el)) return;
    var r = el.getBoundingClientRect(), vh = window.innerHeight;
    if (window.innerWidth >= PHONE) { try { el.scrollIntoView({ block: 'center', behavior: 'auto' }); } catch (e) { /* older browsers */ } return; }
    if (r.top > vh * 0.6 || pinned(el)) return; // a tab bar item or pinned bar: it is already in view
    window.scrollBy(0, r.top - 96);
  }

  // Looks for the element for up to 4 seconds (the page may still be drawing).
  function show(selector) {
    hide();
    document.documentElement.classList.add('tour-on');
    var tries = 0;
    function find() {
      var el = document.querySelector(selector);
      if (el) {
        clearInterval(timer); timer = null;
        target = el;
        // The page can still be settling (focus, layout), so bring it into view again a moment later.
        [0, 250, 800].forEach(function (ms) { setTimeout(function () { if (target === el) bring(el); }, ms); });
        ensure(); cancelAnimationFrame(frame); loop();
      } else if (++tries > 20) {
        clearInterval(timer); timer = null;
        // Nothing to light up on this page: keep the card in view anyway.
        var c = document.querySelector('.tourcard');
        if (c) { c.style.left = M + 'px'; c.style.top = Math.max(M, window.innerHeight - tabBarHeight() - c.offsetHeight - M) + 'px'; c.classList.add('tourcard--placed'); }
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
  function save(value) { try { sessionStorage.setItem('backstop.tour', value); } catch (e) { /* blocked */ } }
  function load() { try { return sessionStorage.getItem('backstop.tour') || ''; } catch (e) { return ''; } }

  window.backstopTour = { show: show, hide: hide, tap: tap, save: save, load: load };
})();
