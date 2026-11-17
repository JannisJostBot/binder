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

    // Each shortcut's icon moves onto its cover: the distance between their centres and the scale between them.
    // Measured from layout offsets, which ignore transforms, so nothing has to stop moving to be measured and a
    // flight in progress is never interrupted. The desktop may be scaled down (narrow windows): k is that scale,
    // and the move is given in the desktop's own, unscaled units.
    var desk = stage.querySelector('.desk');
    var panel = stage.querySelector('.mock-panel');
    function measure() {
      if (!desk || !panel) return;
      var k = desk.getBoundingClientRect().width / desk.offsetWidth || 1;
      var tiles = stage.querySelectorAll('.mock-grid > span');
      var icons = stage.querySelectorAll('.ico[data-tile]');
      for (var i = 0; i < icons.length; i++) {
        var tile = tiles[parseInt(icons[i].getAttribute('data-tile'), 10) - 1];
        var pic = icons[i].querySelector('i');
        if (!tile || !pic || !pic.offsetWidth || !tile.offsetWidth) continue;
        // Centres relative to the stage. The picture's offsets are within the desktop, the tile's within the shelf.
        var ax = desk.offsetLeft + (pic.offsetLeft + pic.offsetWidth / 2) * k;
        var ay = desk.offsetTop + (pic.offsetTop + pic.offsetHeight / 2) * k;
        var bx = panel.offsetLeft + tile.offsetLeft + tile.offsetWidth / 2;
        var by = panel.offsetTop + tile.offsetTop + tile.offsetHeight / 2;
        icons[i].style.setProperty('--dx', ((bx - ax) / k).toFixed(1) + 'px');
        icons[i].style.setProperty('--dy', ((by - ay) / k).toFixed(1) + 'px');
        icons[i].style.setProperty('--s', (tile.offsetWidth / (pic.offsetWidth * k)).toFixed(3));
      }
    }

    // Phones change the window's height while scrolling (the address bar): that moves the line, not the scene, so
    // only a change of width measures again.
    var width = window.innerWidth, pending = 0;
    function resized() {
      pending = 0;
      observe();
      if (window.innerWidth !== width) { width = window.innerWidth; measure(); }
    }
    window.addEventListener('resize', function () { if (!pending) pending = requestAnimationFrame(resized); });
    window.addEventListener('load', measure);
    measure();
    stage.classList.add('is-live');
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', setUp);
  else setUp();
})();
