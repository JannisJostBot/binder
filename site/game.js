// Binder website: a small hidden game behind the Play button on the desktop story's shelf ("Continue playing").
// It tells Binder's own story: the launch toast counts down, the shelf slides away, the made-up games' covers drift up the desktop
// for 20 seconds (click or tap to catch them), then the shelf comes back with "Welcome back" and the score.
// Nothing is fetched or stored. Nothing runs until Play is pressed, and its timers stop when the round ends.
// With reduced motion the covers don't move: they appear in place for a moment and fade.
(function () {
  'use strict';

  var ROUND_MS = 20000;
  var COUNTDOWN = 3;
  var GAMES = ['Starfall Drift', 'Ember Keep', 'Tidebreak', 'Neon Circuit', 'Frostline', 'Hollow Pines',
    'Iron Courier', 'Glowmoth', 'Dune Runners', 'Skyward Isles', 'Rift Tactics', 'Little Lantern'];

  var stage, panel, play, label, title, layer, score, live, game;
  var timers = [];
  var caught = 0, shown = 0, playing = false;
  var still = false;

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text) e.textContent = text;
    return e;
  }

  function later(fn, ms) { timers.push(setTimeout(fn, ms)); }

  function clearTimers() {
    timers.forEach(clearTimeout);
    timers = [];
  }

  function seconds(ms) { return Math.round(ms / 1000); }

  // Binder's launch toast, inside the shelf: "Starting Starfall Drift", 3, 2, 1.
  function toast(then) {
    var box = el('div', 'mock-toast');
    var text = el('span', 'mock-toast-text', 'Starting ' + game);
    var count = el('span', 'mock-toast-count', String(COUNTDOWN));
    box.appendChild(text);
    box.appendChild(count);
    panel.appendChild(box);
    for (var i = 1; i < COUNTDOWN; i++) {
      (function (n) { later(function () { count.textContent = String(COUNTDOWN - n); }, n * 700); })(i);
    }
    later(function () {
      box.remove();
      then();
    }, COUNTDOWN * 700);
  }

  function spawn() {
    if (!playing) return;
    var g = Math.floor(Math.random() * GAMES.length);
    var cover = el('button', 'mock-fly g' + (g + 1));
    cover.type = 'button';
    cover.setAttribute('aria-label', 'Catch ' + GAMES[g]);
    cover.appendChild(el('b', null, GAMES[g]));
    // Across the desktop left of where the shelf sits; in a narrow picture, the whole width.
    var width = layer.clientWidth;
    var room = width > 600 ? width - 380 : width - 80;
    cover.style.setProperty('--x', Math.round(10 + Math.random() * Math.max(room, 40)) + 'px');
    cover.style.setProperty('--y', Math.round(10 + Math.random() * Math.max(layer.clientHeight - 110, 20)) + 'px');
    cover.style.setProperty('--d', (still ? 1.8 : 3.4 + Math.random() * 2.2).toFixed(2) + 's');
    cover.style.setProperty('--r', Math.round(Math.random() * 24 - 12) + 'deg');
    cover.addEventListener('click', function () {
      if (cover.classList.contains('caught') || !playing) return;
      cover.classList.add('caught');
      caught++;
      score.textContent = caught + ' caught';
      later(function () { cover.remove(); }, 250);
    });
    cover.addEventListener('animationend', function (e) {
      if (e.animationName !== 'caught-pop') cover.remove();
    });
    layer.appendChild(cover);
    shown++;
    later(spawn, still ? 900 : 650 + Math.random() * 500);
  }

  function start() {
    if (playing) return;
    clearTimers();
    still = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    caught = 0;
    shown = 0;
    play.disabled = true;
    toast(function () {
      playing = true;
      stage.classList.add('playing');
      layer = el('div', 'mock-game');
      score = el('span', 'mock-score', '0 caught');
      layer.appendChild(score);
      stage.appendChild(layer);
      live.textContent = 'Catch the covers as they drift up. 20 seconds.';
      spawn();
      later(end, ROUND_MS);
    });
  }

  function end() {
    playing = false;
    clearTimers();
    if (layer) layer.remove();
    layer = null;
    stage.classList.remove('playing');
    // The top slot's "Welcome back" moment, as after a session in the app.
    label.textContent = 'Welcome back · Played ' + seconds(ROUND_MS) + ' s';
    title.textContent = caught + ' of ' + shown + ' covers caught';
    play.textContent = 'Play again';
    play.disabled = false;
    live.textContent = 'Welcome back. ' + caught + ' of ' + shown + ' covers caught.';
  }

  function init() {
    stage = document.querySelector('.stage');
    panel = stage && stage.querySelector('.mock-panel');
    play = document.getElementById('mock-play');
    var text = stage && stage.querySelector('.mock-continue-text');
    if (!stage || !panel || !play || !text) return;
    label = text.querySelector('span');
    title = text.querySelector('strong');
    game = title.textContent;
    live = el('span', 'mock-live');
    live.setAttribute('aria-live', 'polite');
    // Outside the picture, so screen readers hear it.
    stage.parentNode.insertBefore(live, stage.nextSibling);
    play.addEventListener('click', start);
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && playing) end();
    });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
