// Binder website: the download section follows the latest release on GitHub.
//
// index.html holds the last release's values (download link, "Version · size", VirusTotal box), so the page is
// complete without this script. When the page opens, this asks GitHub's API once for the latest release of this
// repository and, if it checks out, puts in its values. Nothing else is fetched, nothing is sent about the visitor
// (no cookies, no referrer), and the page's Content-Security-Policy allows no other address.
//
// What the API returns is treated as untrusted: values are matched against strict patterns, links are built here
// from those values (never taken from the response), and text goes in with textContent only. Anything unexpected
// leaves the page as it is.
(function () {
  'use strict';

  var REPO = 'JannisJostBot/binder';
  var API = 'https://api.github.com/repos/' + REPO + '/releases/latest';

  function byId(id) { return document.getElementById(id); }

  function formatDate(iso) {
    var d = new Date(iso + 'T00:00:00Z');
    if (isNaN(d.getTime())) return iso;
    try {
      return new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }).format(d);
    } catch (e) {
      return iso;
    }
  }

  // The release notes' VirusTotal section, as .github/workflows/virustotal.yml writes it between its markers.
  function readScan(body, setupName) {
    var m = /<!-- virustotal -->([\s\S]*?)<!-- \/virustotal -->/.exec(body);
    if (!m) return null;
    var section = m[1];
    if (section.indexOf('`' + setupName + '`') < 0) return null;
    var counts = /\b(\d{1,3}) of (\d{1,3}) engines flagged\b/.exec(section);
    var date = /\b(20\d\d-[01]\d-[0-3]\d)\b/.exec(section);
    var hash = /SHA-256 `([0-9a-f]{64})`/.exec(section);
    if (!counts || !date || !hash) return null;
    var flagged = parseInt(counts[1], 10), engines = parseInt(counts[2], 10);
    if (engines < 1 || flagged > engines) return null;
    return { flagged: flagged, engines: engines, date: date[1], sha256: hash[1] };
  }

  function apply(release) {
    if (!release || release.draft || release.prerelease || typeof release.tag_name !== 'string') return;
    var tag = /^v((0|[1-9]\d{0,4})\.(0|[1-9]\d{0,4})\.(0|[1-9]\d{0,4}))$/.exec(release.tag_name);
    if (!tag) return;
    var version = tag[1];
    var setupName = 'BinderSetup-' + version + '.exe';
    var downloadUrl = 'https://github.com/' + REPO + '/releases/download/v' + version + '/' + setupName;

    // A finished release: the setup and the signed manifest are both up.
    var assets = Array.isArray(release.assets) ? release.assets : [];
    var setup = null, names = {};
    assets.forEach(function (a) {
      if (!a || typeof a.name !== 'string') return;
      names[a.name] = true;
      if (a.name === setupName) setup = a;
    });
    if (!setup || !names['release.json'] || !names['release.json.sig']) return;
    if (setup.browser_download_url !== downloadUrl) return;
    if (typeof setup.size !== 'number' || setup.size < 1 || setup.size > 1024 * 1048576) return;

    var link = byId('download-link');
    var meta = byId('download-version');
    if (link) link.href = downloadUrl;
    if (meta) meta.textContent = 'Version ' + version + ' · ' + Math.max(1, Math.round(setup.size / 1048576)) + ' MB';

    var box = byId('scan');
    if (!box) return;
    var scan = readScan(typeof release.body === 'string' ? release.body : '', setupName);
    // GitHub's own hash of the file, where it gives one, must be the one that was scanned.
    var digest = typeof setup.digest === 'string' ? /^sha256:([0-9a-f]{64})$/.exec(setup.digest) : null;
    if (scan && digest && digest[1] !== scan.sha256) scan = null;

    var title = byId('scan-title'), detail = byId('scan-detail'), hash = byId('scan-hash');
    if (!title || !detail || !hash) return;
    if (!scan) {
      // No result for this version yet: say so rather than show an older installer's result.
      title.textContent = 'VirusTotal: version ' + version + ' hasn\'t been scanned yet';
      detail.textContent = 'The result is added to the release notes when the scan has finished.';
      hash.parentNode.hidden = true;
      return;
    }

    title.textContent = 'VirusTotal: ' + scan.flagged + ' of ' + scan.engines + ' antivirus engines flagged this installer';
    detail.textContent = '';
    if (scan.flagged > 0) detail.appendChild(document.createTextNode('Installers that aren\'t code-signed yet are sometimes flagged. '));
    detail.appendChild(document.createTextNode('Scanned on '));
    var time = document.createElement('time');
    time.dateTime = scan.date;
    time.textContent = formatDate(scan.date);
    detail.appendChild(time);
    detail.appendChild(document.createTextNode(' · '));
    var report = document.createElement('a');
    report.href = 'https://www.virustotal.com/gui/file/' + scan.sha256;
    report.textContent = 'Full report';
    detail.appendChild(report);
    hash.textContent = scan.sha256;
    hash.parentNode.hidden = false;
  }

  // One request per tab every 10 minutes at most (GitHub allows 60 an hour per address without an account).
  var CACHE = 'binder-release', CACHE_MS = 10 * 60 * 1000;
  function cached() {
    try {
      var c = JSON.parse(sessionStorage.getItem(CACHE) || 'null');
      return c && typeof c.at === 'number' && Date.now() - c.at < CACHE_MS ? c.release : null;
    } catch (e) { return null; }
  }
  function remember(release) {
    try { if (release) sessionStorage.setItem(CACHE, JSON.stringify({ at: Date.now(), release: release })); } catch (e) { }
    return release;
  }

  function load() {
    var c = cached();
    if (c) { apply(c); return; }
    if (!window.fetch || !window.Promise) return;
    fetch(API, {
      headers: { 'Accept': 'application/vnd.github+json' },
      credentials: 'omit',
      referrerPolicy: 'no-referrer',
      cache: 'no-cache'
    })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(remember)
      .then(apply)
      .catch(function () { /* offline or rate-limited: keep the page's own values */ });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', load);
  else load();
})();
