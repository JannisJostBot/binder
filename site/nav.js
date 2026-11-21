// Binder website: the header and the docs page's side navigation. On the home page the section link of the part
// you're reading gets the accent bar under it (aria-current="location"), as Windows' selector bars do; on the docs
// page "On this page" marks the part being read and opens only that part's list; on phones the Menu flyout closes when you pick a link, press Esc
// or click elsewhere. Nothing is fetched; it only reacts to scrolling (at most once a frame),
// clicks and keys.
(function () {
  'use strict';

  // docs.html: the side navigation ("On this page") marks the part being read, the one whose heading was passed
  // last by a line a quarter of the way down the window (the last one at the very bottom of the page). A link in a
  // sub-list also marks its parent (is-within). Same way as below: positions noted once, compared on scroll.
  function setUpToc() {
    var links = document.querySelectorAll('.docs-toc a[href^="#"]');
    if (!links.length) return;
    var marks = [], current = null, ticking = 0;
    var toc = document.querySelector('.docs-toc');
    var parts = toc.querySelectorAll(':scope > ol > li');
    toc.classList.add('is-folding');
    for (var i = 0; i < links.length; i++) {
      var target = document.getElementById(links[i].getAttribute('href').slice(1));
      if (target) marks.push({ link: links[i], target: target, top: 0 });
    }
    function place() {
      var y = window.scrollY || window.pageYOffset;
      marks.forEach(function (m) { m.top = m.target.getBoundingClientRect().top + y; });
    }
    function show(link) {
      if (link === current) return;
      if (current) {
        current.removeAttribute('aria-current');
        var was = parentOf(current);
        if (was) was.classList.remove('is-within');
      }
      if (link) {
        link.setAttribute('aria-current', 'location');
        var parent = parentOf(link);
        if (parent) parent.classList.add('is-within');
      }
      current = link;
      // Only the part being read shows its sections (CSS, wide windows only); the others list just their name.
      var part = link ? (parentOf(link) || link).parentNode : null;
      for (var j = 0; j < parts.length; j++) parts[j].classList.toggle('is-open', parts[j] === part);
    }
    function parentOf(link) {
      var item = link.parentNode.parentNode.closest('li');
      return item ? item.querySelector('a') : null;
    }
    function update() {
      ticking = 0;
      var y = window.scrollY || window.pageYOffset;
      var at = y + window.innerHeight / 4, found = null;
      for (var i = 0; i < marks.length; i++) if (marks[i].top <= at) found = marks[i].link;
      if (y + window.innerHeight >= document.documentElement.scrollHeight - 2 && marks.length) found = marks[marks.length - 1].link;
      show(found);
    }
    var pending = 0;
    window.addEventListener('resize', function () { if (!pending) pending = requestAnimationFrame(function () { pending = 0; place(); update(); }); });
    window.addEventListener('load', function () { place(); update(); });
    window.addEventListener('scroll', function () { if (!ticking) ticking = requestAnimationFrame(update); }, { passive: true });
    place();
    update();
  }

  function setUp() {
    setUpToc();
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
