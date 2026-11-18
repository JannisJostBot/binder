// Binder website: "How much it uses" draws a bar under each figure, to scale against the open panel. The figures
// are written into index.html for each release (scripts/site-numbers.ps1); this reads them once when the page opens
// and sets each bar's width (--w). Without it, styles.css keeps fixed proportions. Nothing is fetched.
(function () {
  'use strict';

  function setUp() {
    var figures = document.querySelectorAll('.usage [data-mb]');
    var values = [], largest = 0;
    for (var i = 0; i < figures.length; i++) {
      // "49", "2.5"; a range such as "130–230" counts by its first number.
      var value = parseFloat(figures[i].textContent.replace(/[^0-9.]+/g, ' ').trim());
      values.push(value);
      if (value > largest) largest = value;
    }
    if (!(largest > 0)) return;
    for (var j = 0; j < figures.length; j++) {
      var row = figures[j].closest('.usage-item');
      if (row && values[j] >= 0) row.style.setProperty('--w', (values[j] / largest * 100).toFixed(2) + '%');
    }
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', setUp);
  else setUp();
})();
