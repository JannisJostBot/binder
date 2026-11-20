// Turns a run of the "Website screenshots" workflow into the website's pictures: the chosen views as WebP in
// site/screenshots/ (light and dark), and site/og-image.png, the 1200x630 picture shown when a link to the site is
// shared. Download the run's "site-screenshots" artifact (or its ci-results/screenshots-<branch> branch), then:
//
//   node tests/Screenshots/make-site-images.mjs <folder with light-shelf.png, dark-detail.png, ...>
//
// Needs Node and Playwright with Chromium (any version), and the Inter Display font for the preview's text.
import { createRequire } from 'node:module';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const { chromium } = createRequire(import.meta.url)('playwright');
const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const shots = resolve(process.argv[2] ?? '');
const out = join(root, 'site', 'screenshots');
mkdirSync(out, { recursive: true });

// The views on the website (index.html, "What it looks like").
const views = ['welcome-back', 'detail', 'free-up-space', 'game-mode'];
const dataUrl = (file) => `data:image/png;base64,${readFileSync(join(shots, file)).toString('base64')}`;

const browser = await chromium.launch();
const page = await browser.newPage();

// Each window as the page shows it: without the 1 px border Windows draws around every window (a light line that
// stands out on the dark ones; the page draws its own shadow), its corners rounded again at 8 px like the page's
// cards, as WebP (or PNG for the preview below), through the browser's canvas.
await page.setContent('<!doctype html><body></body>');
async function trimmed(file, type) {
  return page.evaluate(async ([src, type]) => {
    const img = new Image();
    img.src = src;
    await img.decode();
    const w = img.naturalWidth - 2, h = img.naturalHeight - 2;
    const c = document.createElement('canvas');
    c.width = w; c.height = h;
    const g = c.getContext('2d');
    g.beginPath();
    g.roundRect(0, 0, w, h, 8);
    g.clip();
    g.drawImage(img, -1, -1);
    return c.toDataURL(type, 0.9);
  }, [dataUrl(file), type]);
}
for (const theme of ['light', 'dark']) {
  for (const view of views) {
    const webp = await trimmed(`${theme}-${view}.png`, 'image/webp');
    const file = join(out, `${theme}-${view}.webp`);
    writeFileSync(file, Buffer.from(webp.split(',')[1], 'base64'));
    console.log(file);
  }
}

// The preview: the website's download-panel blue, the logo and name on the left, two windows on the right.
const logo = readFileSync(join(root, 'site', 'logo.svg'), 'utf8');
await page.setViewportSize({ width: 1200, height: 630 });
await page.setContent(`<!doctype html><html><head><style>
  html, body { margin: 0; width: 1200px; height: 630px; overflow: hidden; }
  body { background: radial-gradient(120% 120% at 85% 10%, #0C4A85 0, #06345F 45%, #041E38 100%);
    font-family: 'Inter Display', 'Inter', sans-serif; color: #FFFFFF; position: relative; }
  .text { position: absolute; left: 72px; top: 150px; width: 470px; }
  .brand { display: flex; align-items: center; gap: 18px; font-size: 64px; font-weight: 700; letter-spacing: -0.01em; }
  .brand svg { width: 76px; height: 76px; }
  h1 { margin: 34px 0 0; font-size: 40px; line-height: 1.18; font-weight: 600; letter-spacing: -0.01em; }
  p { margin: 22px 0 0; font-size: 24px; color: #B9D5F0; }
  img { position: absolute; width: 360px; border-radius: 8px; box-shadow: 0 24px 60px rgba(0, 0, 0, 0.45); }
  .back { left: 600px; top: 70px; }
  .front { left: 790px; top: 120px; }
</style></head><body>
  <div class="text">
    <div class="brand">${logo.replace(/<\?xml[^>]*>/, '')}<span>Binder</span></div>
    <h1>A game launcher that stays out of your way</h1>
    <p>Free for Windows 10 and 11</p>
  </div>
  <img class="back" src="${await trimmed('light-detail.png', 'image/png')}">
  <img class="front" src="${await trimmed('dark-welcome-back.png', 'image/png')}">
</body></html>`);
await page.evaluate(() => Promise.all([document.fonts.ready, ...[...document.images].map((i) => i.decode())]));
await page.screenshot({ path: join(root, 'site', 'og-image.png'), type: 'png' });
console.log(join(root, 'site', 'og-image.png'));
await browser.close();
