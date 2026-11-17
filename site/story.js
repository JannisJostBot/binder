// Binder website: the desktop story at the top of the page. The desktop stays pinned while the captions scroll
// past; the caption crossing the middle of the window sets the scene's step (data-step: 0 the cluttered desktop,
// 1 the shortcuts fly into Binder, 2 the shelf, 3 the game with Binder closed, 4 welcome back) and styles.css moves
// the scene. This script also works out where each game shortcut flies: onto its own cover in the shelf. Nothing
// is fetched; it only reacts to scrolling (IntersectionObserver) and to the window changing size.
(function () {
  'use strict';

  function setUp() {
    var stage = document.querySelector('.story .stage');
    var steps = document.querySelectorAll('.story-step');
    if (!stage || !steps.length || !('IntersectionObserver' in window)) return;

    // The line a caption crosses to set its step: the middle of the window, or on narrow windows (where the
    // picture is pinned at the top and the captions scroll below it) a little under the picture.
    var narrow = window.matchMedia('(max-width: 900px)');
    var observer = null;
    function observe() {
      if (observer) observer.disconnect();
      var line = Math.round(window.innerHeight / 2);
      if (narrow.matches) line = Math.min(window.innerHeight - 2, Math.round(parseFloat(getComputedStyle(stage).top) + stage.offsetHeight + 80));
      observer = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
          if (entry.isIntersecting) stage.setAttribute('data-step', entry.target.getAttribute('data-step'));
        });
      }, { rootMargin: '-' + line + 'px 0px -' + (window.innerHeight - line - 1) + 'px 0px' });
      for (var i = 0; i < steps.length; i++) observer.observe(steps[i]);
    }
    observe();

    // Each shortcut's icon moves onto its cover: the distance between their centres and the scale between them,
    // measured with the scene at rest (no transforms), so it's right in any step and at any window size.
    function measure() {
      pending = 0;
      var tiles = stage.querySelectorAll('.mock-grid > span');
      var icons = stage.querySelectorAll('.ico[data-tile]');
      stage.classList.add('is-measuring');
      var moves = [];
      for (var i = 0; i < icons.length; i++) {
        var tile = tiles[parseInt(icons[i].getAttribute('data-tile'), 10) - 1];
        var pic = icons[i].querySelector('i');
        if (!tile || !pic) continue;
        var a = pic.getBoundingClientRect(), b = tile.getBoundingClientRect();
        if (!a.width || !b.width) continue;
        moves.push([icons[i], b.left + b.width / 2 - (a.left + a.width / 2), b.top + b.height / 2 - (a.top + a.height / 2), b.width / a.width]);
      }
      stage.classList.remove('is-measuring');
      moves.forEach(function (m) {
        m[0].style.setProperty('--dx', m[1].toFixed(1) + 'px');
        m[0].style.setProperty('--dy', m[2].toFixed(1) + 'px');
        m[0].style.setProperty('--s', m[3].toFixed(3));
      });
    }

    var pending = 0;
    function later() { if (!pending) pending = requestAnimationFrame(function () { observe(); measure(); }); }
    window.addEventListener('resize', later);
    window.addEventListener('load', later);
    measure();
    stage.classList.add('is-live');
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', setUp);
  else setUp();
})();
