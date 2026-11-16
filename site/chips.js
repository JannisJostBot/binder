// Binder website: hovering the launcher chips turns them into Binder's logo. Each chip flies to one of the logo's
// three covers and becomes it; styles.css animates, this script only works out where each chip goes. Only with a
// mouse (devices that can hover) and when the visitor allows motion; otherwise the list stays as it is. Nothing
// runs until the pointer enters the list.
(function () {
  'use strict';

  // The logo's covers (logo.svg: 20 x 28 with radius 3 in a 48 x 48 box, drawn at 64 px), back to front: where
  // each cover's centre sits from the logo's centre, its angle, and its blue (the middle of its gradient).
  var SCALE = 64 / 48;
  var COVER_W = 20 * SCALE, COVER_H = 28 * SCALE, RADIUS = 3 * SCALE;
  var COVERS = [
    cover(21, 22, 21, 40, -14, '#BEE0F7'),  // back: rotate(-14 21 40)
    cover(27, 22, 27, 40, 10, '#7FC4F0'),   // middle: rotate(10 27 40)
    cover(24, 24, 24, 24, 0, '#3FA2E8')     // front
  ];

  // A cover's centre (cx, cy) turned by deg around (ox, oy), relative to the logo's centre (24, 24), in pixels.
  function cover(cx, cy, ox, oy, deg, tint) {
    var a = deg * Math.PI / 180, x = cx - ox, y = cy - oy;
    var rx = ox + x * Math.cos(a) - y * Math.sin(a), ry = oy + x * Math.sin(a) + y * Math.cos(a);
    return { x: (rx - 24) * SCALE, y: (ry - 24) * SCALE, deg: deg, tint: tint };
  }

  function setUp() {
    var box = document.querySelector('.chips-merge');
    if (!box || !window.matchMedia) return;
    var canHover = window.matchMedia('(hover: hover) and (pointer: fine)');
    var still = window.matchMedia('(prefers-reduced-motion: reduce)');

    function measure() {
      // offsetLeft/offsetTop ignore transforms, so this is right even halfway through the animation.
      var cx = box.clientWidth / 2, cy = box.clientHeight / 2;
      var chips = box.querySelectorAll('.chips li');
      for (var i = 0; i < chips.length; i++) {
        var li = chips[i], w = li.offsetWidth, h = li.offsetHeight;
        // The last chips go to the front cover, so the front is the last to land, on top.
        var c = COVERS[Math.min(2, Math.floor(i * 3 / chips.length))];
        var sx = COVER_W / w, sy = COVER_H / h;
        var set = function (k, v) { li.style.setProperty(k, v); };
        set('--dx', (cx + c.x - (li.offsetLeft + w / 2)).toFixed(1) + 'px');
        set('--dy', (cy + c.y - (li.offsetTop + h / 2)).toFixed(1) + 'px');
        set('--r', c.deg + 'deg');
        set('--sx', sx.toFixed(4));
        set('--sy', sy.toFixed(4));
        set('--rx', (RADIUS / sx).toFixed(1) + 'px');
        set('--ry', (RADIUS / sy).toFixed(1) + 'px');
        set('--tint', c.tint);
        set('--z', String(COVERS.indexOf(c) + 1));
        set('--i', String(i));
      }
    }

    box.addEventListener('pointerenter', function (e) {
      if (e.pointerType !== 'mouse' || !canHover.matches || still.matches) return;
      measure();
      box.classList.add('is-merged');
    });
    box.addEventListener('pointerleave', function () {
      box.classList.remove('is-merged');
    });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', setUp);
  else setUp();
})();
