// Binder website: the product shot's taskbar clock shows the visitor's own time and date, in their browser's
// format, and the shelf's greeting follows the time as in the app (Views/Format.cs: morning from 5, afternoon
// from 12, evening from 18). Nothing is fetched. It wakes once a minute, on the minute, and not while the tab is
// hidden. Without it, the page keeps the times written in index.html.
(function () {
  'use strict';

  var timer = 0;

  function greeting(hour) {
    if (hour >= 5 && hour < 12) return 'Good morning';
    if (hour >= 12 && hour < 18) return 'Good afternoon';
    return 'Good evening';
  }

  function update() {
    var clock = document.getElementById('mock-clock');
    var hello = document.getElementById('mock-greeting');
    if (!clock) return;
    var now = new Date();
    var time, date;
    try {
      time = now.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
      date = now.toLocaleDateString(undefined, { day: '2-digit', month: '2-digit', year: 'numeric' });
    } catch (e) {
      return;
    }
    clock.textContent = '';
    clock.appendChild(document.createTextNode(time));
    clock.appendChild(document.createElement('br'));
    clock.appendChild(document.createTextNode(date));
    if (hello) hello.textContent = greeting(now.getHours());
  }

  function schedule() {
    clearTimeout(timer);
    if (document.hidden) return;
    update();
    var now = new Date();
    timer = setTimeout(schedule, (60 - now.getSeconds()) * 1000 - now.getMilliseconds() + 50);
  }

  document.addEventListener('visibilitychange', schedule);
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', schedule);
  else schedule();
})();
