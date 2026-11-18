// Binder website: the header. On the home page the section link of the part you're reading gets the accent bar
// under it (aria-current="location"), as Windows' selector bars do; on phones the Menu flyout closes when you pick
// a link, press Esc or click elsewhere. Nothing is fetched; it only reacts to scrolling (at most once a frame),
// clicks and keys.
(function () {
  'use strict';

  function setUp() {
    var menu = document.querySelector('.header-menu');
    if (menu) {
      menu.addEventListener('click', function (e) {
        if (e.target.closest('a')) menu.open = false;
      });
      document.addEventListener('click', function (e) {
        if (menu.open && !menu.contains(e.target)) menu.open = false;
      });
      document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && menu.open) {
          menu.open = false;
          menu.querySelector('summary').focus();
        }
      });
    }

    // The part of the home page being read: the one crossing a line a third of the way down the window. Where
    // each part is on the page is noted once (and again when the window changes size); scrolling compares the
    // scroll position with that, at most once a frame (an IntersectionObserver's margins don't work in a frame
    // from another site, as in a preview).
    var links = document.querySelectorAll('.header-nav a[href^="#"]');
    if (!links.length) return;
    // Each part to watch and its link; "How it works" covers all four steps of the desktop story.
    var parts = [], owners = [];
    for (var i = 0; i < links.length; i++) {
      var id = links[i].getAttribute('href').slice(1);
      var els = id === 'how' ? document.querySelectorAll('.story-step:not(.story-step-hero)') : [document.getElementById(id)];
      for (var j = 0; j < els.length; j++) if (els[j]) { parts.push(els[j]); owners.push(links[i]); }
    }
    var current = null, spans = [], ticking = 0;
    function mark(link) {
      if (link === current) return;
      if (current) current.removeAttribute('aria-current');
      if (link) link.setAttribute('aria-current', 'location');
      current = link;
    }
    function place() {
      var y = window.scrollY || window.pageYOffset;
      spans = parts.map(function (p) { var r = p.getBoundingClientRect(); return [r.top + y, r.bottom + y]; });
    }
    function update() {
      ticking = 0;
      var at = (window.scrollY || window.pageYOffset) + window.innerHeight / 3, found = null;
      for (var i = 0; i < spans.length && !found; i++) if (at >= spans[i][0] && at < spans[i][1]) found = owners[i];
      mark(found);
    }
    var pending = 0;
    window.addEventListener('resize', function () { if (!pending) pending = requestAnimationFrame(function () { pending = 0; place(); update(); }); });
    window.addEventListener('load', function () { place(); update(); });
    window.addEventListener('scroll', function () { if (!ticking) ticking = requestAnimationFrame(update); }, { passive: true });
    place();
    update();
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', setUp);
  else setUp();
})();
