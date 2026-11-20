// Binder website: the story's wallpaper, three ribbons that move like silk in a gentle breeze while the page is at
// the top: waves travel along each one, and each swells and narrows as it folds. It stops where it is as soon as the
// page scrolls (story.js marks the stage is-scrolled), so nothing moves behind the story, and carries on back at the
// top. It draws at most 30 times a second, only while the tab and the desktop are visible (narrow windows hide the
// desktop); with reduced motion it draws one still frame. Nothing is fetched. Without it, styles.css shows the same
// ribbons, still.
(function () {
  'use strict';

  var NS = 'http://www.w3.org/2000/svg';
  var W = 1600, H = 900, STEPS = 26;

  // Each ribbon: its centre line from the left edge (y0) to the right (y1), its thickness, and its waves: a long one
  // travelling along it, a shorter one against it, and the folds (thickness). Speeds are radians per second.
  var RIBBONS = [
    { cls: 'wall-r1', y0: 760, y1: 330, h: 150, a: 70, l: 1500, s: 0.32, a2: 26, l2: 620, s2: 0.21, f: 0.45, lf: 900, sf: 0.27, o: 0 },
    { cls: 'wall-r2', y0: 880, y1: 500, h: 120, a: 60, l: 1700, s: 0.27, a2: 22, l2: 540, s2: 0.18, f: 0.5, lf: 1100, sf: 0.23, o: 1.7 },
    { cls: 'wall-r3', y0: -40, y1: 320, h: 64, a: 46, l: 1300, s: 0.36, a2: 16, l2: 480, s2: 0.25, f: 0.4, lf: 800, sf: 0.31, o: 3.1 }
  ];

  function el(name, cls) {
    var e = document.createElementNS(NS, name);
    if (cls) e.setAttribute('class', cls);
    return e;
  }

  // A smooth curve through the points (Catmull-Rom as cubic Béziers), continuing an open path.
  function through(pts) {
    var d = '';
    for (var i = 0; i < pts.length - 1; i++) {
      var p0 = pts[Math.max(i - 1, 0)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.min(i + 2, pts.length - 1)];
      d += ' C' + (p1[0] + (p2[0] - p0[0]) / 6).toFixed(1) + ' ' + (p1[1] + (p2[1] - p0[1]) / 6).toFixed(1) +
        ' ' + (p2[0] - (p3[0] - p1[0]) / 6).toFixed(1) + ' ' + (p2[1] - (p3[1] - p1[1]) / 6).toFixed(1) +
        ' ' + p2[0].toFixed(1) + ' ' + p2[1].toFixed(1);
    }
    return d;
  }

  // The ribbon at time t (seconds): its outline, and a thinner sheen along its middle.
  function shapes(r, t) {
    var top = [], bottom = [], sheenTop = [], sheenBottom = [];
    for (var i = 0; i <= STEPS; i++) {
      var x = -150 + (W + 300) * i / STEPS;
      var y = r.y0 + (r.y1 - r.y0) * (x / W) +
        r.a * Math.sin(2 * Math.PI * x / r.l - r.s * t + r.o) +
        r.a2 * Math.sin(2 * Math.PI * x / r.l2 + r.s2 * t + r.o * 1.3);
      var fold = 1 - r.f + r.f * (0.5 + 0.5 * Math.sin(2 * Math.PI * x / r.lf - r.sf * t + r.o * 0.7));
      var half = r.h * fold / 2;
      top.push([x, y - half]);
      bottom.push([x, y + half]);
      sheenTop.push([x, y - half * 0.35 - half * 0.15]);
      sheenBottom.push([x, y + half * 0.05 - half * 0.15]);
    }
    function outline(a, b) {
      b = b.slice().reverse();
      return 'M' + a[0][0].toFixed(1) + ' ' + a[0][1].toFixed(1) + through(a) +
        ' L' + b[0][0].toFixed(1) + ' ' + b[0][1].toFixed(1) + through(b) + ' Z';
    }
    return [outline(top, bottom), outline(sheenTop, sheenBottom)];
  }

  function setUp() {
    var wall = document.querySelector('.desk-wall');
    var stage = document.querySelector('.stage');
    if (!wall || !stage || !window.requestAnimationFrame) return;

    var svg = el('svg');
    svg.setAttribute('viewBox', '0 0 ' + W + ' ' + H);
    svg.setAttribute('preserveAspectRatio', 'xMidYMid slice');
    svg.setAttribute('aria-hidden', 'true');
    var parts = RIBBONS.map(function (r) {
      var g = el('g', r.cls);
      var body = el('path', 'wall-body'), sheen = el('path', 'wall-sheen');
      g.appendChild(body);
      g.appendChild(sheen);
      svg.appendChild(g);
      return { r: r, body: body, sheen: sheen };
    });
    wall.appendChild(svg);
    wall.classList.add('has-ribbons');

    var still = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)');
    var clock = 0, last = 0, frame = 0;
    function draw() {
      for (var i = 0; i < parts.length; i++) {
        var d = shapes(parts[i].r, clock);
        parts[i].body.setAttribute('d', d[0]);
        parts[i].sheen.setAttribute('d', d[1]);
      }
    }
    // Only while the desktop is shown (narrow windows hide it) and the page is at the top.
    function moving() {
      return !document.hidden && wall.getClientRects().length > 0 && !stage.classList.contains('is-scrolled') &&
        !(still && still.matches);
    }
    function tick(now) {
      frame = 0;
      if (!moving()) { last = 0; return; }
      // Time only runs while it moves, so it carries on from where it stopped.
      if (last) {
        var dt = Math.min((now - last) / 1000, 0.1);
        if (dt < 1 / 31) { frame = requestAnimationFrame(tick); return; }
        clock += dt;
      }
      last = now;
      draw();
      frame = requestAnimationFrame(tick);
    }
    function wake() { if (!frame && moving()) frame = requestAnimationFrame(tick); }

    draw();
    new MutationObserver(wake).observe(stage, { attributes: true, attributeFilter: ['class'] });
    document.addEventListener('visibilitychange', wake);
    window.addEventListener('resize', wake);
    if (still && still.addEventListener) still.addEventListener('change', wake);
    wake();
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', setUp);
  else setUp();
})();
