// Binder website: after a download starts, a dialog says what to expect before running the setup. The setup isn't
// code-signed yet, so Microsoft Defender SmartScreen or an antivirus may warn about it; the dialog shows that
// version's VirusTotal result and says not to install Binder if that isn't acceptable.
//
// Each setup link carries its version and scan as data attributes (data-version, and data-flagged, data-engines,
// data-scanned, data-sha256 once VirusTotal has scanned it): written into releases.html by
// scripts/site-releases.ps1, and kept on index.html's link by release.js. The click itself is left alone, so the
// download starts as usual; without this script there's simply no dialog. Nothing is fetched; text goes in with
// textContent only.
(function () {
  'use strict';

  function formatDate(iso) {
    var d = new Date(iso + 'T00:00:00Z');
    if (isNaN(d.getTime())) return iso;
    try {
      return new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }).format(d);
    } catch (e) {
      return iso;
    }
  }

  function setUp() {
    var dialog = document.getElementById('download-dialog');
    if (!dialog || typeof dialog.showModal !== 'function') return;
    function part(name) { return dialog.querySelector('[data-part="' + name + '"]'); }

    function fill(link) {
      var version = link.getAttribute('data-version') || '';
      if (!/^\d{1,5}\.\d{1,5}\.\d{1,5}$/.test(version)) return false;
      part('version').textContent = version;
      var flagged = parseInt(link.getAttribute('data-flagged'), 10);
      var engines = parseInt(link.getAttribute('data-engines'), 10);
      var scanned = link.getAttribute('data-scanned') || '';
      var sha = link.getAttribute('data-sha256') || '';
      var title = part('scan-title'), detail = part('scan-detail'), hash = part('scan-hash');
      detail.textContent = '';
      if (flagged >= 0 && engines > 0 && flagged <= engines && /^20\d\d-[01]\d-[0-3]\d$/.test(scanned) && /^[0-9a-f]{64}$/.test(sha)) {
        title.textContent = 'VirusTotal: ' + flagged + ' of ' + engines + ' antivirus engines flagged this installer';
        detail.appendChild(document.createTextNode('Scanned on '));
        var time = document.createElement('time');
        time.dateTime = scanned;
        time.textContent = formatDate(scanned);
        detail.appendChild(time);
        detail.appendChild(document.createTextNode(' · '));
        var report = document.createElement('a');
        report.href = 'https://www.virustotal.com/gui/file/' + sha;
        report.textContent = 'Full report';
        detail.appendChild(report);
        hash.textContent = sha;
        hash.parentNode.hidden = false;
      } else {
        title.textContent = 'VirusTotal: version ' + version + ' hasn\'t been scanned';
        detail.textContent = 'There\'s no VirusTotal result for this installer.';
        hash.parentNode.hidden = true;
      }
      return true;
    }

    document.addEventListener('click', function (e) {
      var link = e.target.closest && e.target.closest('a[data-version]');
      if (!link || dialog.open) return;
      if (!fill(link)) return;
      // After the click has gone through, so the download starts first.
      setTimeout(function () { if (!dialog.open) dialog.showModal(); }, 0);
    });
    var close = dialog.querySelector('[data-part="close"]');
    if (close) close.addEventListener('click', function () { dialog.close(); });
    // A click on the dimmed page around the dialog closes it too.
    dialog.addEventListener('click', function (e) { if (e.target === dialog) dialog.close(); });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', setUp);
  else setUp();
})();
