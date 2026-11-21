// Draws the made-up games' artwork for the website screenshots (tests/screenshots.ps1): for each game a Steam-style
// cover (600x900 JPEG), banner (1920x620 JPEG) and logo (transparent PNG), the same games and colours as the
// website's drawn shelf (site/styles.css, .g1 to .g12). The output is committed in tests/Screenshots/art/, so CI
// needs nothing of this; run it again only to change the art:
//
//   node tests/Screenshots/make-art.mjs
//
// Needs Node and Playwright with Chromium (any version), and the Inter Display font for the titles.
import { createRequire } from 'node:module';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// Through require, so a globally installed Playwright is found too (NODE_PATH).
const { chromium } = createRequire(import.meta.url)('playwright');

const out = join(dirname(fileURLToPath(import.meta.url)), 'art');
mkdirSync(out, { recursive: true });

// Seeded random numbers, so the stars and sparkles are the same on every run.
function rng(seed) {
  let s = seed >>> 0;
  return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
}
function stars(w, h, n, seed, maxY = 1, colour = '#FFFFFF') {
  const r = rng(seed);
  let s = '';
  for (let i = 0; i < n; i++) {
    const x = r() * w, y = r() * h * maxY, size = 0.6 + r() * r() * 2.6, o = 0.35 + r() * 0.65;
    s += `<circle cx="${x.toFixed(1)}" cy="${y.toFixed(1)}" r="${(size * Math.max(w, h) / 900).toFixed(2)}" fill="${colour}" opacity="${o.toFixed(2)}"/>`;
  }
  return s;
}

// Each scene draws into a w x h box; covers are 600x900, banners 1920x620. `u` is a unit that scales with the box.
const games = [
  {
    id: 'starfall-drift', name: 'Starfall Drift', font: 800, spacing: 0.08,
    scene: (w, h, u) => {
      const cx = w * 0.62, cy = h * (w > h ? 0.48 : 0.36), pr = u * 0.17;
      return `
      <defs>
        <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#070B24"/><stop offset="0.6" stop-color="#1E1645"/><stop offset="1" stop-color="#3B2160"/></linearGradient>
        <radialGradient id="planet" cx="0.35" cy="0.3" r="0.8"><stop offset="0" stop-color="#FFE2B0"/><stop offset="0.45" stop-color="#D9893F"/><stop offset="1" stop-color="#5A2418"/></radialGradient>
        <radialGradient id="neb" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#8A4FD8" stop-opacity="0.55"/><stop offset="1" stop-color="#8A4FD8" stop-opacity="0"/></radialGradient>
        <radialGradient id="neb2" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#2E7BD6" stop-opacity="0.45"/><stop offset="1" stop-color="#2E7BD6" stop-opacity="0"/></radialGradient>
      </defs>
      <rect width="${w}" height="${h}" fill="url(#sky)"/>
      <ellipse cx="${w * 0.25}" cy="${h * 0.62}" rx="${u * 0.6}" ry="${u * 0.32}" fill="url(#neb)"/>
      <ellipse cx="${w * 0.8}" cy="${h * 0.18}" rx="${u * 0.45}" ry="${u * 0.25}" fill="url(#neb2)"/>
      ${stars(w, h, 220, 11)}
      <g transform="rotate(-18 ${cx} ${cy})">
        <path d="M ${cx - pr * 2.1} ${cy} A ${pr * 2.1} ${pr * 0.5} 0 0 1 ${cx + pr * 2.1} ${cy}" fill="none" stroke="#F2C78C" stroke-opacity="0.55" stroke-width="${u * 0.022}"/>
      </g>
      <circle cx="${cx}" cy="${cy}" r="${pr}" fill="url(#planet)"/>
      <path d="M ${cx - pr * 0.9} ${cy - pr * 0.25} Q ${cx} ${cy - pr * 0.45} ${cx + pr * 0.95} ${cy - pr * 0.1}" stroke="#FFF1D0" stroke-opacity="0.25" stroke-width="${u * 0.012}" fill="none"/>
      <path d="M ${cx - pr * 0.95} ${cy + pr * 0.2} Q ${cx} ${cy + pr * 0.05} ${cx + pr * 0.9} ${cy + pr * 0.35}" stroke="#5A2418" stroke-opacity="0.35" stroke-width="${u * 0.016}" fill="none"/>
      <g transform="rotate(-18 ${cx} ${cy})">
        <path d="M ${cx + pr * 2.1} ${cy} A ${pr * 2.1} ${pr * 0.5} 0 0 1 ${cx - pr * 2.1} ${cy}" fill="none" stroke="#F2C78C" stroke-opacity="0.9" stroke-width="${u * 0.022}"/>
      </g>
      <g transform="translate(${w * 0.24} ${h * (w > h ? 0.3 : 0.24)}) rotate(28)">
        <path d="M 0 0 L ${-u * 0.42} ${-u * 0.02} L ${-u * 0.42} ${u * 0.02} Z" fill="#BFD9FF" opacity="0.5"/>
        <circle r="${u * 0.012}" fill="#FFFFFF"/>
      </g>`;
    },
  },
  {
    id: 'ember-keep', name: 'Ember Keep', font: 900, spacing: 0.04, serif: true,
    scene: (w, h, u) => {
      const base = h * 0.78, kx = w * 0.5, k = u * 0.0042;
      return `
      <defs>
        <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3A1530"/><stop offset="0.45" stop-color="#C2482E"/><stop offset="0.75" stop-color="#F59E4B"/><stop offset="1" stop-color="#FFD08A"/></linearGradient>
        <radialGradient id="sun" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#FFF1C0"/><stop offset="0.35" stop-color="#FFC46E" stop-opacity="0.9"/><stop offset="1" stop-color="#FF9A4A" stop-opacity="0"/></radialGradient>
      </defs>
      <rect width="${w}" height="${h}" fill="url(#sky)"/>
      <circle cx="${kx}" cy="${base - u * 0.32}" r="${u * 0.5}" fill="url(#sun)"/>
      ${stars(w, h, 40, 22, 0.25, '#FFE0C8')}
      <path d="M 0 ${base + u * 0.02} Q ${w * 0.2} ${base - u * 0.12} ${w * 0.38} ${base - u * 0.04} Q ${w * 0.5} ${base - u * 0.1} ${w * 0.64} ${base - u * 0.03} Q ${w * 0.82} ${base - u * 0.16} ${w} ${base - u * 0.02} L ${w} ${h} L 0 ${h} Z" fill="#5A2228"/>
      <g fill="#24100E" transform="translate(${kx} ${base}) scale(${k})">
        <path d="M -70 0 V -120 h 12 v -14 h 12 v 14 h 10 v -14 h 12 v 14 h 12 V 0 Z"/>
        <path d="M 12 0 V -120 h 12 v -14 h 12 v 14 h 10 v -14 h 12 v 14 h 12 V 0 Z"/>
        <path d="M -34 0 V -200 h 14 v -18 h 12 v 18 h 16 v -18 h 12 v 18 h 14 V 0 Z"/>
        <path d="M -110 0 V -70 h 50 V 0 Z M 60 0 V -70 h 50 V 0 Z"/>
        <path d="M -150 30 Q 0 -10 150 30 V 60 H -150 Z"/>
      </g>
      <g fill="#FFC46E" transform="translate(${kx} ${base}) scale(${k})">
        <rect x="-5" y="-170" width="10" height="18" rx="5"/><rect x="-52" y="-96" width="8" height="14" rx="4"/><rect x="30" y="-96" width="8" height="14" rx="4"/>
        <path d="M -12 0 v -26 a 12 12 0 0 1 24 0 v 26 Z" fill="#FF9A4A"/>
      </g>
      <path d="M 0 ${h} L 0 ${base + u * 0.1} Q ${w * 0.3} ${base + u * 0.02} ${w * 0.5} ${base + u * 0.12} Q ${w * 0.75} ${base + u * 0.04} ${w} ${base + u * 0.12} L ${w} ${h} Z" fill="#1A0A0A"/>`;
    },
  },
  {
    id: 'tidebreak', name: 'Tidebreak', font: 900, spacing: 0.02,
    scene: (w, h, u) => `
      <defs>
        <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#BDEBFF"/><stop offset="1" stop-color="#6EC3EE"/></linearGradient>
        <linearGradient id="sea" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1C7BC0"/><stop offset="1" stop-color="#0A2E52"/></linearGradient>
        <linearGradient id="wave" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#3FA7E0"/><stop offset="1" stop-color="#0E4C80"/></linearGradient>
      </defs>
      <rect width="${w}" height="${h}" fill="url(#sky)"/>
      <circle cx="${w * 0.78}" cy="${h * 0.16}" r="${u * 0.07}" fill="#FFFBEA"/>
      <ellipse cx="${w * 0.25}" cy="${h * 0.14}" rx="${u * 0.16}" ry="${u * 0.035}" fill="#FFFFFF" opacity="0.8"/>
      <rect y="${h * 0.5}" width="${w}" height="${h * 0.5}" fill="url(#sea)"/>
      <path d="M ${-w * 0.05} ${h * 0.9} C ${w * 0.15} ${h * 0.55} ${w * 0.35} ${h * 0.32} ${w * 0.62} ${h * 0.34} C ${w * 0.86} ${h * 0.36} ${w * 0.92} ${h * 0.5} ${w * 0.76} ${h * 0.56} C ${w * 0.66} ${h * 0.6} ${w * 0.6} ${h * 0.5} ${w * 0.66} ${h * 0.46} C ${w * 0.5} ${h * 0.44} ${w * 0.4} ${h * 0.62} ${w * 0.45} ${h * 0.95} L ${-w * 0.05} ${h} Z" fill="url(#wave)"/>
      <path d="M ${w * 0.12} ${h * 0.66} C ${w * 0.26} ${h * 0.44} ${w * 0.44} ${h * 0.34} ${w * 0.62} ${h * 0.345} C ${w * 0.84} ${h * 0.36} ${w * 0.9} ${h * 0.5} ${w * 0.76} ${h * 0.555}" fill="none" stroke="#E8F7FF" stroke-width="${u * 0.03}" stroke-linecap="round"/>
      ${stars(w, h * 0.3, 60, 33, 1, '#E8F7FF').replaceAll('cy="', 'data-y="').replace(/data-y="([\d.]+)"/g, (m, y) => `cy="${(+y + h * 0.36).toFixed(1)}"`)}
      <path d="M 0 ${h * 0.86} Q ${w * 0.25} ${h * 0.82} ${w * 0.5} ${h * 0.87} T ${w} ${h * 0.86} V ${h} H 0 Z" fill="#082440" opacity="0.8"/>`,
  },
  {
    id: 'neon-circuit', name: 'Neon Circuit', font: 900, spacing: 0.1, italic: true,
    scene: (w, h, u) => {
      const hz = h * 0.56, sy = hz - u * 0.02, sr = u * 0.26;
      let grid = '';
      for (let i = -12; i <= 12; i++) grid += `<line x1="${w / 2 + i * u * 0.02}" y1="${hz}" x2="${w / 2 + i * u * 0.4}" y2="${h}" stroke="#FF6FD0" stroke-opacity="0.6" stroke-width="${u * 0.004}"/>`;
      for (let j = 1; j < 12; j++) { const y = hz + (h - hz) * Math.pow(j / 11, 2.2); grid += `<line x1="0" y1="${y}" x2="${w}" y2="${y}" stroke="#FF6FD0" stroke-opacity="0.55" stroke-width="${u * 0.004}"/>`; }
      let bands = '';
      for (let i = 0; i < 6; i++) { const y = sy - sr * 0.1 + i * sr * 0.16; bands += `<rect x="${w / 2 - sr}" y="${y}" width="${sr * 2}" height="${sr * (0.03 + i * 0.012)}" fill="#1A0B3A"/>`; }
      return `
      <defs>
        <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#0D0626"/><stop offset="0.7" stop-color="#4A1466"/><stop offset="1" stop-color="#8A2378"/></linearGradient>
        <linearGradient id="sun" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFE27A"/><stop offset="0.6" stop-color="#FF7A8A"/><stop offset="1" stop-color="#FF3FA0"/></linearGradient>
        <linearGradient id="ground" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2A0A44"/><stop offset="1" stop-color="#0A0218"/></linearGradient>
        <clipPath id="top"><rect width="${w}" height="${hz}"/></clipPath>
      </defs>
      <rect width="${w}" height="${h}" fill="url(#sky)"/>
      ${stars(w, h, 90, 44, 0.45)}
      <g clip-path="url(#top)"><circle cx="${w / 2}" cy="${sy}" r="${sr}" fill="url(#sun)"/>${bands}</g>
      <path d="M 0 ${hz} L ${w * 0.18} ${hz - u * 0.06} L ${w * 0.3} ${hz - u * 0.02} L ${w * 0.4} ${hz - u * 0.08} L ${w * 0.46} ${hz} Z M ${w * 0.56} ${hz} L ${w * 0.66} ${hz - u * 0.1} L ${w * 0.76} ${hz - u * 0.03} L ${w * 0.88} ${hz - u * 0.07} L ${w} ${hz} Z" fill="#2A0D4A"/>
      <rect y="${hz}" width="${w}" height="${h - hz}" fill="url(#ground)"/>
      ${grid}
      <path d="M ${w / 2 - u * 0.012} ${hz} L ${w / 2 - u * 0.2} ${h} L ${w / 2 + u * 0.2} ${h} L ${w / 2 + u * 0.012} ${hz} Z" fill="#12052A"/>
      <path d="M ${w / 2} ${hz} L ${w / 2} ${h}" stroke="#FFE27A" stroke-width="${u * 0.008}" stroke-dasharray="${u * 0.03} ${u * 0.03}"/>
      <line x1="0" y1="${hz}" x2="${w}" y2="${hz}" stroke="#FF9AD8" stroke-width="${u * 0.006}"/>`;
    },
  },
  {
    id: 'frostline', name: 'Frostline', font: 800, spacing: 0.22,
    scene: (w, h, u) => `
      <defs>
        <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#9FD3F5"/><stop offset="1" stop-color="#E6F5FF"/></linearGradient>
        <linearGradient id="far" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#B8D6EE"/><stop offset="1" stop-color="#8DB4D8"/></linearGradient>
        <linearGradient id="near" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#FFFFFF"/><stop offset="1" stop-color="#C9E2F5"/></linearGradient>
      </defs>
      <rect width="${w}" height="${h}" fill="url(#sky)"/>
      <circle cx="${w * 0.24}" cy="${h * 0.2}" r="${u * 0.06}" fill="#FFFFFF" opacity="0.9"/>
      <path d="M 0 ${h * 0.62} L ${w * 0.16} ${h * 0.44} L ${w * 0.26} ${h * 0.52} L ${w * 0.44} ${h * 0.3} L ${w * 0.6} ${h * 0.5} L ${w * 0.76} ${h * 0.38} L ${w} ${h * 0.58} V ${h} H 0 Z" fill="url(#far)"/>
      <path d="M ${w * 0.44} ${h * 0.3} L ${w * 0.5} ${h * 0.38} L ${w * 0.46} ${h * 0.37} L ${w * 0.43} ${h * 0.4} L ${w * 0.4} ${h * 0.35} Z M ${w * 0.76} ${h * 0.38} L ${w * 0.81} ${h * 0.43} L ${w * 0.77} ${h * 0.42} L ${w * 0.73} ${h * 0.42} Z" fill="#FFFFFF"/>
      <path d="M ${-w * 0.1} ${h * 0.86} L ${w * 0.3} ${h * 0.5} L ${w * 0.5} ${h * 0.68} L ${w * 0.66} ${h * 0.56} L ${w * 1.1} ${h * 0.9} V ${h} H ${-w * 0.1} Z" fill="url(#near)"/>
      <path d="M ${w * 0.3} ${h * 0.5} L ${w * 0.36} ${h * 0.62} L ${w * 0.3} ${h * 0.6} L ${w * 0.26} ${h * 0.66} L ${w * 0.22} ${h * 0.6} Z M ${w * 0.66} ${h * 0.56} L ${w * 0.72} ${h * 0.64} L ${w * 0.68} ${h * 0.63} L ${w * 0.62} ${h * 0.66} L ${w * 0.6} ${h * 0.6} Z" fill="#9CC6E6"/>
      <path d="M 0 ${h * 0.9} Q ${w * 0.5} ${h * 0.84} ${w} ${h * 0.92} V ${h} H 0 Z" fill="#F4FAFF"/>
      ${stars(w, h, 70, 55, 1, '#FFFFFF')}`,
  },
  {
    id: 'hollow-pines', name: 'Hollow Pines', font: 700, spacing: 0.06, serif: true,
    scene: (w, h, u) => {
      const tree = (x, base, s, fill) => `<path d="M ${x} ${base - s} L ${x + s * 0.18} ${base - s * 0.7} H ${x + s * 0.09} L ${x + s * 0.27} ${base - s * 0.4} H ${x + s * 0.14} L ${x + s * 0.34} ${base - s * 0.08} H ${x - s * 0.34} L ${x - s * 0.14} ${base - s * 0.4} H ${x - s * 0.27} L ${x - s * 0.09} ${base - s * 0.7} H ${x - s * 0.18} Z" fill="${fill}"/><rect x="${x - s * 0.03}" y="${base - s * 0.09}" width="${s * 0.06}" height="${s * 0.09}" fill="${fill}"/>`;
      const r = rng(66);
      let far = '', mid = '', near = '';
      for (let x = -0.05; x < 1.05; x += 0.07) far += tree(w * (x + r() * 0.03), h * 0.72, u * (0.22 + r() * 0.08), '#7FA98A');
      for (let x = -0.05; x < 1.05; x += 0.11) mid += tree(w * (x + r() * 0.04), h * 0.84, u * (0.32 + r() * 0.1), '#2E6A42');
      for (let x = -0.1; x < 1.1; x += 0.22) near += tree(w * (x + r() * 0.05), h * 1.02, u * (0.5 + r() * 0.15), '#123222');
      return `
      <defs>
        <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#F4E7C4"/><stop offset="0.6" stop-color="#C9DDB4"/><stop offset="1" stop-color="#8DB48A"/></linearGradient>
        <radialGradient id="sun" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#FFF8E0"/><stop offset="1" stop-color="#FFF8E0" stop-opacity="0"/></radialGradient>
      </defs>
      <rect width="${w}" height="${h}" fill="url(#sky)"/>
      <circle cx="${w * 0.7}" cy="${h * 0.3}" r="${u * 0.22}" fill="url(#sun)"/>
      <circle cx="${w * 0.7}" cy="${h * 0.3}" r="${u * 0.05}" fill="#FFFBEA"/>
      ${far}<rect y="${h * 0.71}" width="${w}" height="${h * 0.3}" fill="#7FA98A"/>
      ${mid}<rect y="${h * 0.83}" width="${w}" height="${h * 0.2}" fill="#2E6A42"/>
      ${near}`;
    },
  },
  {
    id: 'iron-courier', name: 'Iron Courier', font: 900, spacing: 0.06,
    scene: (w, h, u) => {
      let stripes = '';
      const sh = u * 0.09, y0 = h * (w > h ? 0.68 : 0.62);
      for (let x = -sh * 2; x < w + sh * 2; x += sh * 1.6) stripes += `<path d="M ${x} ${y0} h ${sh * 0.8} l ${-sh} ${sh} h ${-sh * 0.8} Z" fill="#2E3239"/>`;
      let rivets = '';
      for (let x = u * 0.06; x < w; x += u * 0.12) rivets += `<circle cx="${x}" cy="${h * 0.12}" r="${u * 0.01}" fill="#8E96A3"/><circle cx="${x}" cy="${y0 - u * 0.06}" r="${u * 0.01}" fill="#8E96A3"/>`;
      const bx = w * 0.5, by = h * (w > h ? 0.38 : 0.36), bs = u * 0.24;
      return `
      <defs>
        <linearGradient id="steel" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#5C636E"/><stop offset="1" stop-color="#262A31"/></linearGradient>
        <linearGradient id="box" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#F7C04A"/><stop offset="1" stop-color="#D98A1C"/></linearGradient>
      </defs>
      <rect width="${w}" height="${h}" fill="url(#steel)"/>
      ${Array.from({ length: 14 }, (_, i) => `<line x1="0" y1="${h * i / 14}" x2="${w}" y2="${h * i / 14 + h * 0.02}" stroke="#FFFFFF" stroke-opacity="0.03" stroke-width="${u * 0.02}"/>`).join('')}
      ${rivets}
      <g transform="translate(${bx} ${by})">
        <path d="M 0 ${-bs} L ${bs * 0.9} ${-bs * 0.5} V ${bs * 0.55} L 0 ${bs * 1.05} L ${-bs * 0.9} ${bs * 0.55} V ${-bs * 0.5} Z" fill="url(#box)"/>
        <path d="M 0 ${-bs} L ${bs * 0.9} ${-bs * 0.5} L 0 0 L ${-bs * 0.9} ${-bs * 0.5} Z" fill="#FFD77A"/>
        <path d="M 0 0 V ${bs * 1.05}" stroke="#9A5A10" stroke-width="${u * 0.006}"/>
        <path d="M ${-bs * 0.45} ${-bs * 0.75} L ${bs * 0.45} ${-bs * 0.25} V ${bs * 0.15}" fill="none" stroke="#2E3239" stroke-width="${u * 0.03}"/>
        <path d="M ${-bs * 0.7} ${bs * 0.05} l ${bs * 0.35} ${bs * 0.2} v ${bs * 0.22} l ${-bs * 0.35} ${-bs * 0.2} Z" fill="#2E3239" opacity="0.6"/>
      </g>
      <rect y="${y0}" width="${w}" height="${sh}" fill="#F0B429"/>
      ${stripes}
      <rect y="${y0 + sh}" width="${w}" height="${h - y0 - sh}" fill="#1E2127"/>
      <rect y="${y0 + sh + u * 0.04}" width="${w}" height="${u * 0.025}" fill="#F08A24"/>`;
    },
  },
  {
    id: 'glowmoth', name: 'Glowmoth', font: 700, spacing: 0.14,
    scene: (w, h, u) => {
      const cx = w * 0.5, cy = h * (w > h ? 0.46 : 0.4), s = u * 0.0032;
      return `
      <defs>
        <radialGradient id="bg" cx="0.5" cy="0.42" r="0.75"><stop offset="0" stop-color="#1B5560"/><stop offset="0.6" stop-color="#0B2A31"/><stop offset="1" stop-color="#040F12"/></radialGradient>
        <radialGradient id="glow" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#FFF6C8"/><stop offset="0.2" stop-color="#FFE282" stop-opacity="0.8"/><stop offset="1" stop-color="#FFE282" stop-opacity="0"/></radialGradient>
        <linearGradient id="wing" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#B4FFE6" stop-opacity="0.75"/><stop offset="1" stop-color="#3FBFA0" stop-opacity="0.25"/></linearGradient>
      </defs>
      <rect width="${w}" height="${h}" fill="url(#bg)"/>
      ${stars(w, h, 70, 77, 1, '#B4FFE6')}
      <circle cx="${cx}" cy="${cy}" r="${u * 0.42}" fill="url(#glow)"/>
      <g transform="translate(${cx} ${cy}) scale(${s})">
        <path d="M 0 0 C -40 -90 -150 -100 -150 -30 C -150 20 -60 30 0 0 Z" fill="url(#wing)" stroke="#D8FFF2" stroke-width="2"/>
        <path d="M 0 0 C 40 -90 150 -100 150 -30 C 150 20 60 30 0 0 Z" fill="url(#wing)" stroke="#D8FFF2" stroke-width="2"/>
        <path d="M 0 0 C -30 40 -90 80 -100 50 C -110 20 -50 10 0 0 Z" fill="url(#wing)" opacity="0.8"/>
        <path d="M 0 0 C 30 40 90 80 100 50 C 110 20 50 10 0 0 Z" fill="url(#wing)" opacity="0.8"/>
        <circle cx="-85" cy="-45" r="14" fill="none" stroke="#FFF6C8" stroke-width="3" opacity="0.7"/>
        <circle cx="85" cy="-45" r="14" fill="none" stroke="#FFF6C8" stroke-width="3" opacity="0.7"/>
        <ellipse rx="9" ry="42" cy="8" fill="#FFF6C8"/>
        <path d="M -4 -32 C -14 -60 -30 -70 -40 -72 M 4 -32 C 14 -60 30 -70 40 -72" stroke="#FFF6C8" stroke-width="3" fill="none" stroke-linecap="round"/>
      </g>
      <path d="M 0 ${h} V ${h * 0.86} C ${w * 0.2} ${h * 0.8} ${w * 0.3} ${h * 0.92} ${w * 0.5} ${h * 0.88} C ${w * 0.7} ${h * 0.84} ${w * 0.85} ${h * 0.9} ${w} ${h * 0.85} V ${h} Z" fill="#03090B"/>`;
    },
  },
  {
    id: 'dune-runners', name: 'Dune Runners', font: 900, spacing: 0.05, italic: true,
    scene: (w, h, u) => `
      <defs>
        <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#F5C77A"/><stop offset="0.6" stop-color="#FBE3B0"/><stop offset="1" stop-color="#FFF0D0"/></linearGradient>
        <linearGradient id="d1" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#EE9F4E"/><stop offset="1" stop-color="#C97A30"/></linearGradient>
        <linearGradient id="d2" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#D98A3A"/><stop offset="1" stop-color="#9C5A20"/></linearGradient>
        <linearGradient id="d3" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#B86E26"/><stop offset="1" stop-color="#6E3A12"/></linearGradient>
      </defs>
      <rect width="${w}" height="${h}" fill="url(#sky)"/>
      <circle cx="${w * 0.68}" cy="${h * 0.38}" r="${u * 0.13}" fill="#FFF6DD"/>
      <path d="M 0 ${h * 0.6} C ${w * 0.25} ${h * 0.5} ${w * 0.45} ${h * 0.56} ${w * 0.6} ${h * 0.6} C ${w * 0.8} ${h * 0.65} ${w * 0.9} ${h * 0.52} ${w} ${h * 0.54} V ${h} H 0 Z" fill="url(#d1)"/>
      <path d="M 0 ${h * 0.74} C ${w * 0.3} ${h * 0.62} ${w * 0.5} ${h * 0.7} ${w * 0.7} ${h * 0.78} C ${w * 0.85} ${h * 0.84} ${w * 0.95} ${h * 0.74} ${w} ${h * 0.72} V ${h} H 0 Z" fill="url(#d2)"/>
      <path d="M 0 ${h * 0.86} C ${w * 0.2} ${h * 0.8} ${w * 0.5} ${h * 0.84} ${w} ${h * 0.94} V ${h} H 0 Z" fill="url(#d3)"/>
      <g transform="translate(${w * 0.3} ${h * 0.66})">
        <path d="M ${-u * 0.06} 0 L ${u * 0.07} 0 L ${u * 0.05} ${-u * 0.025} L ${-u * 0.03} ${-u * 0.03} Z" fill="#3A2010"/>
        <circle cx="${-u * 0.04}" cy="${u * 0.005}" r="${u * 0.014}" fill="#2A1408"/><circle cx="${u * 0.05}" cy="${u * 0.005}" r="${u * 0.014}" fill="#2A1408"/>
        <path d="M ${-u * 0.07} 0 C ${-u * 0.2} ${-u * 0.01} ${-u * 0.3} ${u * 0.02} ${-u * 0.4} ${u * 0.04}" stroke="#FBE3B0" stroke-opacity="0.7" stroke-width="${u * 0.015}" fill="none" stroke-linecap="round"/>
      </g>`,
  },
  {
    id: 'skyward-isles', name: 'Skyward Isles', font: 800, spacing: 0.04,
    scene: (w, h, u) => {
      const isle = (x, y, s, o = 1) => `<g transform="translate(${x} ${y}) scale(${s})" opacity="${o}">
        <path d="M -100 0 C -100 -16 100 -16 100 0 C 100 10 -100 10 -100 0 Z" fill="#4FA35A"/>
        <path d="M -96 4 L 0 120 L 96 4 C 60 14 -60 14 -96 4 Z" fill="#7A5638"/>
        <path d="M -40 30 L 0 120 L -10 40 Z" fill="#5C3F27"/>
        <path d="M 30 -10 C 30 -60 70 -60 70 -14" fill="#2F7A3D"/><rect x="47" y="-20" width="6" height="16" fill="#5C3F27"/>
        <path d="M -60 -8 C -60 -40 -30 -40 -30 -10" fill="#3B8C48"/>
        <path d="M 10 100 C 12 130 6 150 14 170" stroke="#9ED8FF" stroke-width="5" fill="none" opacity="0.7"/></g>`;
      return `
      <defs><linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#5BAFE8"/><stop offset="1" stop-color="#CDEBFF"/></linearGradient></defs>
      <rect width="${w}" height="${h}" fill="url(#sky)"/>
      <ellipse cx="${w * 0.2}" cy="${h * 0.16}" rx="${u * 0.2}" ry="${u * 0.05}" fill="#FFFFFF" opacity="0.9"/>
      <ellipse cx="${w * 0.85}" cy="${h * 0.7}" rx="${u * 0.26}" ry="${u * 0.06}" fill="#FFFFFF" opacity="0.85"/>
      ${isle(w * 0.82, h * 0.26, u * 0.0016, 0.6)}
      ${isle(w * 0.18, h * 0.55, u * 0.0012, 0.5)}
      ${isle(w * 0.52, h * 0.42, u * 0.0034)}
      <ellipse cx="${w * 0.4}" cy="${h * 0.88}" rx="${u * 0.4}" ry="${u * 0.08}" fill="#FFFFFF" opacity="0.95"/>
      <ellipse cx="${w * 0.9}" cy="${h * 0.94}" rx="${u * 0.3}" ry="${u * 0.07}" fill="#FFFFFF" opacity="0.9"/>`;
    },
  },
  {
    id: 'rift-tactics', name: 'Rift Tactics', font: 900, spacing: 0.18,
    scene: (w, h, u) => `
      <defs>
        <linearGradient id="l" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#2A2A36"/><stop offset="1" stop-color="#121218"/></linearGradient>
        <linearGradient id="r" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#7A1E2A"/><stop offset="1" stop-color="#2A0A10"/></linearGradient>
        <radialGradient id="glow" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#FF5A4E" stop-opacity="0.6"/><stop offset="1" stop-color="#FF5A4E" stop-opacity="0"/></radialGradient>
      </defs>
      <rect width="${w}" height="${h}" fill="url(#r)"/>
      <path d="M 0 0 H ${w * 0.62} L ${w * 0.48} ${h * 0.38} L ${w * 0.56} ${h * 0.42} L ${w * 0.36} ${h} H 0 Z" fill="url(#l)"/>
      <ellipse cx="${w * 0.5}" cy="${h * 0.45}" rx="${u * 0.22}" ry="${u * 0.6}" fill="url(#glow)" transform="rotate(20 ${w * 0.5} ${h * 0.45})"/>
      <path d="M ${w * 0.62} 0 L ${w * 0.48} ${h * 0.38} L ${w * 0.56} ${h * 0.42} L ${w * 0.36} ${h}" fill="none" stroke="#FF5A4E" stroke-width="${u * 0.02}" stroke-linejoin="round"/>
      <path d="M ${w * 0.62} 0 L ${w * 0.48} ${h * 0.38} L ${w * 0.56} ${h * 0.42} L ${w * 0.36} ${h}" fill="none" stroke="#FFD2C8" stroke-width="${u * 0.006}" stroke-linejoin="round"/>
      ${Array.from({ length: 6 }, (_, i) => `<rect x="${w * (0.12 + (i % 3) * 0.08)}" y="${h * (0.66 + Math.floor(i / 3) * 0.06)}" width="${u * 0.05}" height="${u * 0.05}" fill="none" stroke="#8A8AA0" stroke-opacity="0.5" stroke-width="${u * 0.004}"/>`).join('')}
      ${Array.from({ length: 6 }, (_, i) => `<rect x="${w * (0.68 + (i % 3) * 0.08)}" y="${h * (0.2 + Math.floor(i / 3) * 0.06)}" width="${u * 0.05}" height="${u * 0.05}" fill="#FF5A4E" fill-opacity="${0.15 + (i % 2) * 0.25}"/>`).join('')}`,
  },
  {
    id: 'little-lantern', name: 'Little Lantern', font: 700, spacing: 0.04, serif: true,
    scene: (w, h, u) => {
      const cx = w * 0.5, cy = h * (w > h ? 0.44 : 0.42), s = u * 0.0042;
      return `
      <defs>
        <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1C1438"/><stop offset="1" stop-color="#4A2D5E"/></linearGradient>
        <radialGradient id="glow" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#FFC46E" stop-opacity="0.75"/><stop offset="1" stop-color="#FFC46E" stop-opacity="0"/></radialGradient>
        <linearGradient id="lamp" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFE7A6"/><stop offset="1" stop-color="#F28A2E"/></linearGradient>
      </defs>
      <rect width="${w}" height="${h}" fill="url(#sky)"/>
      ${stars(w, h, 120, 99, 0.7)}
      <circle cx="${w * 0.8}" cy="${h * 0.14}" r="${u * 0.05}" fill="#FFF3D6"/><circle cx="${w * 0.8 + u * 0.02}" cy="${h * 0.14 - u * 0.012}" r="${u * 0.045}" fill="#231842"/>
      <circle cx="${cx}" cy="${cy}" r="${u * 0.4}" fill="url(#glow)"/>
      <g transform="translate(${cx} ${cy}) scale(${s})">
        <path d="M -14 -64 C -14 -86 14 -86 14 -64" fill="none" stroke="#3A2414" stroke-width="6"/>
        <rect x="-26" y="-64" width="52" height="14" rx="5" fill="#3A2414"/>
        <path d="M -24 -50 H 24 L 20 40 H -20 Z" fill="url(#lamp)" stroke="#3A2414" stroke-width="5"/>
        <path d="M 0 -48 V 38 M -12 -50 L -10 40 M 12 -50 L 10 40" stroke="#3A2414" stroke-width="2.5" opacity="0.55"/>
        <ellipse cx="0" cy="-4" rx="8" ry="14" fill="#FFF6DD"/>
        <rect x="-26" y="40" width="52" height="12" rx="4" fill="#3A2414"/>
      </g>
      <path d="M 0 ${h} V ${h * 0.82} C ${w * 0.25} ${h * 0.76} ${w * 0.4} ${h * 0.86} ${w * 0.6} ${h * 0.8} C ${w * 0.8} ${h * 0.74} ${w * 0.9} ${h * 0.8} ${w} ${h * 0.78} V ${h} Z" fill="#140C26"/>
      <path d="M ${w * 0.12} ${h * 0.8} l ${u * 0.04} ${-u * 0.12} l ${u * 0.04} ${u * 0.12} Z M ${w * 0.82} ${h * 0.78} l ${u * 0.05} ${-u * 0.16} l ${u * 0.05} ${u * 0.16} Z" fill="#140C26"/>`;
    },
  },
];

function titleSvg(g, w, size, fill = '#FFFFFF') {
  const family = g.serif ? "'DejaVu Serif', 'Liberation Serif', serif" : "'Inter Display', 'Inter', sans-serif";
  const style = g.italic ? 'italic' : 'normal';
  return `<text x="${w / 2}" text-anchor="middle" font-family="${family}" font-weight="${g.font}" font-style="${style}"
    font-size="${size}" letter-spacing="${(g.spacing * size).toFixed(1)}" fill="${fill}">${g.name.toUpperCase()}</text>`;
}

function page(w, h, body, transparent = false) {
  return `<!doctype html><html><head><style>html,body{margin:0;background:${transparent ? 'transparent' : '#000'}}svg{display:block}</style></head>
<body><svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}">${body}</svg></body></html>`;
}

// Cover: the scene, a soft shade behind the title, and the title in two lines at most.
function cover(g) {
  const w = 600, h = 900, u = 900;
  const words = g.name.toUpperCase().split(' ');
  const size = g.name.length > 11 ? 66 : 76;
  const lines = words.length > 1 ? words : [g.name.toUpperCase()];
  const top = h - 70 - (lines.length - 1) * size * 1.02;
  const text = lines.map((line, i) => titleSvg({ ...g, name: line }, w, size).replace('<text ', `<text y="${top + i * size * 1.02}" `)).join('');
  return page(w, h, `${g.scene(w, h, u)}
    <defs><linearGradient id="shade" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#000" stop-opacity="0"/><stop offset="1" stop-color="#000" stop-opacity="0.55"/></linearGradient>
    <filter id="ts" x="-10%" y="-30%" width="120%" height="160%"><feDropShadow dx="0" dy="3" stdDeviation="5" flood-color="#000" flood-opacity="0.55"/></filter></defs>
    <rect y="${h * 0.62}" width="${w}" height="${h * 0.38}" fill="url(#shade)"/>
    <g filter="url(#ts)">${text}</g>`);
}

// Banner (Steam's "hero"): the scene only; Binder puts the logo over it.
function hero(g) {
  const w = 1920, h = 620;
  return page(w, h, g.scene(w, h, 720));
}

// Logo: the title on a transparent background, with a shadow so it reads on any banner; cropped to the text.
function logo(g) {
  const w = 2000, size = 120, h = 200;
  return page(w, h, `<defs><filter id="ts" x="-10%" y="-30%" width="120%" height="160%"><feDropShadow dx="0" dy="4" stdDeviation="6" flood-color="#000" flood-opacity="0.7"/></filter></defs>
    <g filter="url(#ts)">${titleSvg(g, w, size).replace('<text ', `<text y="${h / 2 + size * 0.36}" `)}</g>`, true);
}

const browser = await chromium.launch();
const pageObj = await browser.newPage();
async function shoot(html, w, h, file, type) {
  await pageObj.setViewportSize({ width: w, height: h });
  await pageObj.setContent(html);
  await pageObj.evaluate(() => document.fonts.ready);
  let clip = { x: 0, y: 0, width: w, height: h };
  if (type === 'png') {
    // The text's box with room for its shadow.
    const b = await pageObj.evaluate(() => { const r = document.querySelector('text').getBBox(); return { x: r.x, y: r.y, width: r.width, height: r.height }; });
    const pad = 20;
    clip = { x: Math.floor(b.x - pad), y: Math.floor(b.y - pad / 2), width: Math.ceil(b.width + pad * 2), height: Math.ceil(b.height + pad * 1.5) };
  }
  const options = { path: join(out, file), clip };
  if (type === 'jpeg') Object.assign(options, { type: 'jpeg', quality: 90 });
  else Object.assign(options, { type: 'png', omitBackground: true });
  await pageObj.screenshot(options);
}
const index = [];
for (const g of games) {
  await shoot(cover(g), 600, 900, `${g.id}-cover.jpg`, 'jpeg');
  await shoot(hero(g), 1920, 620, `${g.id}-hero.jpg`, 'jpeg');
  await shoot(logo(g), 2000, 200, `${g.id}-logo.png`, 'png');
  index.push(g.id);
  console.log(g.id);
}
await browser.close();
writeFileSync(join(out, 'README.md'), `Made-up games' artwork for the website screenshots, drawn by \`../make-art.mjs\` (run it again to change
them). Covers 600x900, banners 1920x620, logos with a transparent background. Part of Binder, under the same license.
`);
