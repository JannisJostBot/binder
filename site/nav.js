// Binder website: the header. On the home page the section link of the part you're reading gets the accent bar
// under it (aria-current="location"), as Windows' selector bars do; on phones the Menu flyout closes when you pick
// a link, press Esc or click elsewhere. Nothing is fetched; it only reacts to scrolling (IntersectionObserver),
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

    // The part of the home page being read: the one crossing a line a third of the way down the window.
    var links = document.querySelectorAll('.header-nav a[href^="#"]');
    if (!links.length || !('IntersectionObserver' in window)) return;
    // Each part to watch and its link; "How it works" covers all four steps of the desktop story.
    var parts = [], owners = [];
    for (var i = 0; i < links.length; i++) {
      var id = links[i].getAttribute('href').slice(1);
      var els = id === 'how' ? document.querySelectorAll('.story-step:not(.story-step-hero)') : [document.getElementById(id)];
      for (var j = 0; j < els.length; j++) if (els[j]) { parts.push(els[j]); owners.push(links[i]); }
    }
    var current = null;
    function mark(link) {
      if (link === current) return;
      if (current) current.removeAttribute('aria-current');
      if (link) link.setAttribute('aria-current', 'location');
      current = link;
    }
    var inView = [];
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        inView[parts.indexOf(entry.target)] = entry.isIntersecting;
      });
      var k = inView.indexOf(true);
      mark(k < 0 ? null : owners[k]);
    }, { rootMargin: '-33% 0px -66% 0px' });
    parts.forEach(function (p) { observer.observe(p); });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', setUp);
  else setUp();
})();
