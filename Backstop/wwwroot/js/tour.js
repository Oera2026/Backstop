// Demo guide: lights up one thing on the page, dims the rest, and keeps the guide card next to it.
// The page underneath stays clickable.
(function () {
  var spot = null, target = null, settled = null, sel = '', started = 0, frame = 0, queued = 0, glideTimer = 0;
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
    c.style.width = '';
    var vw = window.innerWidth, vh = window.innerHeight, w = c.offsetWidth, h = c.offsetHeight, x, y, zone = 'free';
    if (vw >= PHONE) {
      if (r.right + GAP + w + M <= vw) { x = r.right + GAP; y = r.top + r.height / 2 - h / 2; }
      else if (r.left - GAP - w - M >= 0) { x = r.left - GAP - w; y = r.top + r.height / 2 - h / 2; }
      else {
        var roomL = r.left - GAP - M, roomR = vw - r.right - GAP - M, side = Math.max(roomL, roomR);
        if (r.height > vh * 0.3 && side >= 270) {
          // Tall thing, no room for a full card beside it: a narrower card beside it beats one on top of it.
          w = Math.min(w, side); c.style.width = w + 'px';
          x = roomR >= roomL ? r.right + GAP : r.left - GAP - w; y = r.top + r.height / 2 - h / 2;
        } else {
          x = r.left + r.width / 2 - w / 2;
          var below = vh - r.bottom, above = r.top;
          y = below >= h + GAP + M || below >= above ? r.bottom + GAP : r.top - GAP - h;
        }
      }
    } else {
      x = (vw - w) / 2;
      if (r.top > vh * 0.6 || (pinned(target) && r.top > vh * 0.3)) { y = 72; zone = "top"; }
      else { y = vh - tabBarHeight() - h - M; zone = 'bottom'; }
    }
    return { x: clamp(x, M, vw - w - M), y: clamp(y, M, vh - h - M), zone: zone };
  }

  // Skeleton and start-up shapes are grey stand-ins, and hidden things (the Home tab on a phone) have no size: neither can be lit up.
  function visible(el) {
    if (el.closest('.skel, .boot')) return false;
    var r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  }

  // The first visible match, trying each listed selector in the order it is written.
  function pick(selector) {
    var parts = selector.split(',');
    for (var i = 0; i < parts.length; i++) {
      var list = document.querySelectorAll(parts[i].trim());
      for (var j = 0; j < list.length; j++) if (visible(list[j])) return list[j];
    }
    return null;
  }

  function place() {
    if (!spot) return;
    if (!target || !document.body.contains(target) || !visible(target)) target = null;
    if (!target) target = pick(sel);
    var c = document.querySelector('.tourcard');
    if (!target) {
      // Nothing to light up (yet): no dimming, and after a moment the card still has to be in view.
      spot.style.display = 'none';
      if (c && Date.now() - started > 3000) { c.style.left = M + 'px'; c.style.top = Math.max(M, window.innerHeight - tabBarHeight() - c.offsetHeight - M) + 'px'; c.classList.add('tourcard--placed'); }
      return;
    }
    if (target !== settled) { glide(c); settled = target; [0, 250, 800].forEach(function (ms) { setTimeout(function () { if (target === settled) bring(target); }, ms); }); }
    spot.style.display = '';
    var r = target.getBoundingClientRect(), pad = 6;
    var top = r.top - pad, height = r.height + pad * 2;
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

  // A new thing to light up: the spot and the card slide to it instead of jumping. The slide switches off again
  // once it has landed, so scrolling the page is followed at once.
  function glide(c) {
    if (!spot) return;
    spot.classList.add('tour-spot--glide');
    if (c) c.classList.add('tourcard--glide');
    clearTimeout(glideTimer);
    glideTimer = setTimeout(function () {
      if (spot) spot.classList.remove('tour-spot--glide');
      var card = document.querySelector('.tourcard');
      if (card) card.classList.remove('tourcard--glide');
    }, 500);
  }

  // Scrolling and resizing move the spot at once. A slower check catches the page changing under it, and finds the thing
  // while the page is still drawing. Nothing runs every frame, which saves a phone's battery.
  function follow() {
    if (queued) return;
    queued = requestAnimationFrame(function () { queued = 0; place(); });
  }
  function loop() { place(); frame = setTimeout(loop, target ? 300 : 100); }
  function listen(on) {
    var change = on ? 'addEventListener' : 'removeEventListener';
    window[change]('scroll', follow, { passive: true, capture: true });
    window[change]('resize', follow);
  }

  function hide() {
    target = null; settled = null; sel = '';
    clearTimeout(frame);
    cancelAnimationFrame(queued); queued = 0;
    listen(false);
    document.documentElement.classList.remove('tour-on');
    if (spot) { spot.remove(); spot = null; }
  }

  // Brings the lit-up thing into view. On a phone it goes near the top, clear of the card at the bottom.
  function bring(el) {
    if (!document.body.contains(el)) return;
    var r = el.getBoundingClientRect(), vh = window.innerHeight;
    if (window.innerWidth >= PHONE) { try { el.scrollIntoView({ block: 'center', behavior: 'auto' }); } catch (e) { /* older browsers */ } return; }
    if (pinned(el)) return; // a tab bar item or pinned bar: it is already in view
    window.scrollBy(0, r.top - 96);
  }

  // Lights up the thing the selector names. The page may still be drawing, so the search goes on until it is found,
  // and starts again if the page replaces it.
  function show(selector) {
    if (framed()) { hide(); return; }
    // The spot stays on screen between slides so it can slide to the next place.
    target = null; settled = null;
    document.documentElement.classList.add('tour-on');
    sel = selector; started = Date.now();
    ensure(); clearTimeout(frame); listen(false); listen(true); loop();
  }

  // "Show me" taps the lit-up link for the visitor. Says whether there was something to tap.
  function tap(selector) {
    var el = document.querySelector(selector);
    if (!el) return false;
    el.click();
    return true;
  }

  // Inside a frame (the split screen on the demo panel) there is no guide: the page around it has the controls.
  function framed() { return window.self !== window.top; }

  // Keeps the place in the guide when the page is reloaded (this tab only).
  function save(value) { try { sessionStorage.setItem('backstop.tour', value); } catch (e) { /* blocked */ } }
  function load() { try { return sessionStorage.getItem('backstop.tour') || ''; } catch (e) { return ''; } }

  window.backstopTour = { show: show, hide: hide, tap: tap, save: save, load: load, framed: framed };
})();
