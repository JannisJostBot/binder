// Binder website: hovering the launcher chips gathers them in the middle, where they become Binder's logo.
// Only with a mouse (devices that can hover) and when the visitor allows motion; otherwise nothing happens and the
// list stays as it is. This script only measures how far each chip is from the middle (--dx, --dy) and sets the
// class; styles.css does the moving. Nothing runs until the pointer enters the list.
(function () {
  'use strict';

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
        var li = chips[i];
        var dx = cx - (li.offsetLeft + li.offsetWidth / 2);
        var dy = cy - (li.offsetTop + li.offsetHeight / 2);
        li.style.setProperty('--dx', Math.round(dx) + 'px');
        li.style.setProperty('--dy', Math.round(dy) + 'px');
        li.style.setProperty('--i', String(i));
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
