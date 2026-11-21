// Binder website: the desktop story at the top of the page. The desktop stays pinned while the captions scroll
// past; the caption crossing the middle of the window sets the scene's step (data-step: 0 the cluttered desktop,
// 1 the shortcuts fly into Binder, 2 the shelf, 3 the game with Binder closed, 4 welcome back) and styles.css moves
// the scene. This script also works out where each game shortcut flies: onto its own cover in the shelf. Nothing
// is fetched; it only reacts to scrolling (at most once a frame) and to the window changing size.
(function () {
  'use strict';

  function setUp() {
    var story = document.querySelector('.story');
    var stage = story && story.querySelector('.stage');
    var steps = document.querySelectorAll('.story-step');
    if (!stage || !steps.length) return;

    // The line a caption crosses to set its step: the middle of the window, or on narrow windows (where the
    // picture is pinned at the top and the captions scroll below it) a little under the picture. Where each caption
    // starts on the page is noted once (and again when the window changes size); scrolling then only compares the
    // scroll position with those numbers, at most once a frame. (Not an IntersectionObserver: inside a frame from
    // another site, as in a preview, browsers ignore its margins, and the step changed too early or got stuck.)
    var narrow = window.matchMedia('(max-width: 900px)');
    var starts = [], lengths = [], line = 0, ticking = 0, measured = false, marked = null;
    story.classList.add('has-steps');
    function place() {
      var y = window.scrollY || window.pageYOffset;
      for (var i = 0; i < steps.length; i++) {
        starts[i] = steps[i].getBoundingClientRect().top + y;
        lengths[i] = steps[i].offsetHeight;
      }
      line = narrow.matches ? stage.offsetHeight + 80 : window.innerHeight / 2;
    }
    function update() {
      ticking = 0;
      var y = window.scrollY || window.pageYOffset;
      // The "Scroll to tidy this desktop" hint goes as soon as the page scrolls.
      stage.classList.toggle('is-scrolled', y > 24);
      var at = y + line, step = steps[0].getAttribute('data-step'), current = 0;
      for (var i = 0; i < steps.length; i++) if (at >= starts[i]) { step = steps[i].getAttribute('data-step'); current = i; }
      // How far through its step the page is (0 to 1): the current caption's progress bar fills with it.
      var through = Math.max(0, Math.min(1, (at - starts[current]) / (lengths[current] || 1)));
      steps[current].style.setProperty('--p', through.toFixed(3));
      // The step on the section too (the headline's window minimises once the story moves on); kept in step even
      // when the scene's own step is unchanged, so the section is never left without one.
      if (story.getAttribute('data-step') !== step) story.setAttribute('data-step', step);
      // The captions' own state: the current one shows, the ones already read have moved up (styles.css).
      if (marked !== step) {
        marked = step;
        var reached = false;
        for (var j = 0; j < steps.length; j++) {
          var here = steps[j].getAttribute('data-step') === step;
          steps[j].classList.toggle('is-current', here);
          steps[j].classList.toggle('is-past', !here && !reached);
          if (here) reached = true;
        }
      }
      if (stage.getAttribute('data-step') === step) return;
      // Measure before the first flight if the page had no size yet when it loaded (a preview's hidden frame).
      if (!measured) measure();
      // Once the shortcuts have flown, going back to the first step flies them home (styles.css, ico-home).
      if (step !== '0') stage.classList.add('has-flown');
      // Where it came from: the shelf is clicked open only coming from the cluttered desktop.
      stage.setAttribute('data-from', stage.getAttribute('data-step'));
      stage.setAttribute('data-step', step);
    }
    function observe() { place(); update(); }
    window.addEventListener('scroll', function () { if (!ticking) ticking = requestAnimationFrame(update); }, { passive: true });
    observe();

    // Each shortcut's icon moves onto its cover: the distance between their centres and the scale between them.
    // Measured from layout offsets, which ignore transforms, so nothing has to stop moving to be measured and a
    // flight in progress is never interrupted. The desktop may be scaled down (narrow windows): k is that scale,
    // and the move is given in the desktop's own, unscaled units.
    var desk = stage.querySelector('.desk');
    var panel = stage.querySelector('.mock-panel');
    var header = document.querySelector('.site-header') || { offsetHeight: 0 };
    var PIC = 46;   // the icon picture's size (styles.css, .ico i)

    // The order they fly in: the shelf's order, about 60 ms apart; a second shortcut for the same game (a demo, a
    // mod tool) follows its first one. Each tile appears as its first shortcut lands (--d on both).
    var GAP = 0.06, AGAIN = 0.12;
    (function order() {
      var seen = {};
      var icons = stage.querySelectorAll('.ico[data-tile]');
      for (var i = 0; i < icons.length; i++) {
        var n = parseInt(icons[i].getAttribute('data-tile'), 10);
        var again = seen[n] = (seen[n] || 0) + 1;
        icons[i].style.setProperty('--d', ((n - 1) * GAP + (again - 1) * AGAIN).toFixed(2) + 's');
      }
      var tiles = stage.querySelectorAll('.mock-grid > span');
      for (var j = 0; j < tiles.length; j++) tiles[j].style.setProperty('--d', (j * GAP).toFixed(2) + 's');
    })();
    function measure() {
      if (!desk || !panel) return;
      var k = desk.getBoundingClientRect().width / desk.offsetWidth || 1;
      var tiles = stage.querySelectorAll('.mock-grid > span');
      var icons = stage.querySelectorAll('.ico[data-tile]');
      for (var i = 0; i < icons.length; i++) {
        var tile = tiles[parseInt(icons[i].getAttribute('data-tile'), 10) - 1];
        var icon = icons[i];
        if (!tile || !icon.offsetWidth || !tile.offsetWidth) continue;
        // Centres relative to the stage, the icon's within the desktop and the tile's within the shelf. The icon's
        // own offsets, not its picture's: once an icon has flown it keeps its transform, and a transformed element
        // becomes the reference for its children's offsets, so measuring the picture again (after a resize) sent
        // the next flight too far. The picture sits centred at the icon's top, PIC px square.
        var ax = desk.offsetLeft + (icon.offsetLeft + icon.offsetWidth / 2) * k;
        var ay = desk.offsetTop + (icon.offsetTop + PIC / 2) * k;
        var bx = panel.offsetLeft + tile.offsetLeft + tile.offsetWidth / 2;
        var by = panel.offsetTop + tile.offsetTop + tile.offsetHeight / 2;
        icons[i].style.setProperty('--dx', ((bx - ax) / k).toFixed(1) + 'px');
        icons[i].style.setProperty('--dy', ((by - ay) / k).toFixed(1) + 'px');
        icons[i].style.setProperty('--s', (tile.offsetWidth / (PIC * k)).toFixed(3));
        // The arc rises up to 140 px, less for icons near the top, so none goes behind the header.
        var room = ay - header.offsetHeight - 40;
        icons[i].style.setProperty('--lift', (Math.max(24, Math.min(140, room)) / k).toFixed(0) + 'px');
        measured = true;
      }
      // What's left once the games have gone (Recycle Bin, Notes.txt, Screenshots) is arranged down the first column,
      // as Windows does: the n-th one moves to the cell in row n, column 1 (the desktop is a 4-column grid). Desktop
      // units, like the flights.
      var all = desk.children, plain = desk.querySelectorAll('.ico-plain');
      for (var p = 0; p < plain.length; p++) {
        var cell = all[p * 4];
        if (!cell) continue;
        plain[p].style.setProperty('--tx', (cell.offsetLeft - plain[p].offsetLeft) + 'px');
        plain[p].style.setProperty('--ty', (cell.offsetTop - plain[p].offsetTop) + 'px');
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
    window.addEventListener('load', function () { observe(); measure(); });
    measure();
    // The game's score: each burst (styles.css, .sf-boom) starts as its shot lands or the crystal is caught, and
    // again every loop; its points are added then, so the score only ever moves on a hit.
    var points = stage.querySelector('.sf-points'), score = 24180;
    function scored(e) {
      if (e.animationName !== 'sf-hit' || !points) return;
      var n = parseInt(e.target.getAttribute('data-points'), 10);
      if (!(n > 0)) return;
      score += n;
      points.textContent = score.toLocaleString('en-US');
    }
    stage.addEventListener('animationstart', scored);
    stage.addEventListener('animationiteration', scored);
    stage.classList.add('is-live');
  }

  // Narrow windows (no desktop story): each caption's small picture plays once when it has scrolled up to about the
  // middle of the window, where it's being looked at (styles.css has the animations; none with reduced motion).
  // Checked on scroll, at most once a frame, like the story above (an IntersectionObserver's margins are ignored in
  // a frame from another site); the listener goes once all have played.
  function watchPictures() {
    var waiting = Array.prototype.slice.call(document.querySelectorAll('.caption-pic'));
    if (!waiting.length) return;
    var ticking = 0;
    function check() {
      ticking = 0;
      var line = window.innerHeight * 0.6;
      waiting = waiting.filter(function (pic) {
        var r = pic.getBoundingClientRect();
        if (!r.height || r.bottom > line || r.bottom < 0) return true;
        pic.classList.add('is-seen');
        return false;
      });
      if (!waiting.length) window.removeEventListener('scroll', onScroll);
    }
    function onScroll() { if (!ticking) ticking = requestAnimationFrame(check); }
    window.addEventListener('scroll', onScroll, { passive: true });
    check();
  }

  function start() { setUp(); watchPictures(); }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
