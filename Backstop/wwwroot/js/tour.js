// Demo guide: lights up one thing on the page and dims the rest. The page underneath stays clickable.
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

  function place() {
    if (!target || !spot) return;
    if (!document.body.contains(target)) { hide(); return; }
    var r = target.getBoundingClientRect(), pad = 6;
    spot.style.top = (r.top - pad) + 'px';
    spot.style.left = (r.left - pad) + 'px';
    spot.style.width = (r.width + pad * 2) + 'px';
    spot.style.height = (r.height + pad * 2) + 'px';
  }

  function loop() { place(); frame = requestAnimationFrame(loop); }

  function hide() {
    clearInterval(timer); timer = null; target = null;
    cancelAnimationFrame(frame);
    if (spot) { spot.remove(); spot = null; }
  }

  // Looks for the element for up to 4 seconds (the page may still be drawing). Says whether the card
  // should sit at the "top" or the "bottom" so it never covers what is lit up.
  function show(selector) {
    hide();
    return new Promise(function (resolve) {
      var tries = 0;
      function find() {
        var el = document.querySelector(selector);
        if (el) {
          clearInterval(timer); timer = null;
          target = el;
          try { el.scrollIntoView({ block: 'center', behavior: 'auto' }); } catch (e) { /* older browsers */ }
          ensure(); place(); cancelAnimationFrame(frame); loop();
          var r = el.getBoundingClientRect();
          resolve(r.top + r.height / 2 > window.innerHeight / 2 ? 'top' : 'bottom');
        } else if (++tries > 20) {
          clearInterval(timer); timer = null; resolve('bottom');
        }
      }
      timer = setInterval(find, 200);
      find();
    });
  }

  window.backstopTour = { show: show, hide: hide };
})();
