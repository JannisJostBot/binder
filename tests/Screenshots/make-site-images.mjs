// Turns a run of the "Website screenshots" workflow into the website's pictures: the chosen views as AVIF and WebP
// in site/screenshots/ (light and dark), and site/og-image.png, the 1200x630 picture shown when a link to the site is
// shared. The page offers the AVIF first (about 40 % smaller than the WebP and closer to the capture) and the WebP to
// browsers without AVIF. Download the run's "site-screenshots" artifact (or its ci-results/screenshots-<branch> branch), then:
//
//   node tests/Screenshots/make-site-images.mjs <folder with light-shelf.png, dark-detail.png, ...>
//
// Needs Node and Playwright with Chromium (any version), the Inter Display font for the preview's text, avifenc
// from libavif 1.0 or later on the PATH (github.com/AOMediaCodec/libavif releases; apt install libavif-bin), and
// pngquant (pngquant.org; apt install pngquant) for the preview.
import { execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
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
// AVIF: from the same trimmed window, kept lossless until avifenc. Quality 80 with full colour resolution (4:4:4,
// so small text and the accent's edges stay sharp) and the slowest, smallest encoding: on these pictures that's
// ~40 % below the WebP and nearer the capture (2026-11-21: 206 KB for the eight against 350 KB, PSNR 51 against 39 dB).
const temp = mkdtempSync(join(tmpdir(), 'binder-shots-'));
const bytes = (dataUrl) => Buffer.from(dataUrl.split(',')[1], 'base64');
try {
  for (const theme of ['light', 'dark']) {
    for (const view of views) {
      const name = `${theme}-${view}`;
      const webp = join(out, `${name}.webp`);
      writeFileSync(webp, bytes(await trimmed(`${name}.png`, 'image/webp')));
      const png = join(temp, `${name}.png`);
      writeFileSync(png, bytes(await trimmed(`${name}.png`, 'image/png')));
      const avif = join(out, `${name}.avif`);
      execFileSync('avifenc', ['-q', '80', '--yuv', '444', '-s', '0', '-d', '8', png, avif], { stdio: 'ignore' });
      console.log(webp);
      console.log(avif);
    }
  }
} finally {
  rmSync(temp, { recursive: true, force: true });
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
// Captured lossless, then cut to at most 256 colours by pngquant (633 KB to 113 KB, 2026-11-23): the dither hides in
// the covers, the blue gradient stays smooth (a JPEG of the same size shows blocks there), and it stays a PNG, which
// every link preview takes. Should pngquant not reach quality 90, the lossless capture is kept.
const ogImage = join(root, 'site', 'og-image.png');
const ogTemp = mkdtempSync(join(tmpdir(), 'binder-og-'));
try {
  const lossless = join(ogTemp, 'og-image.png');
  await page.screenshot({ path: lossless, type: 'png' });
  try {
    execFileSync('pngquant', ['--quality', '90-100', '--speed', '1', '--strip', '--force', '--output', ogImage, lossless], { stdio: 'ignore' });
  } catch {
    console.warn('pngquant missing or below quality 90: og-image.png kept lossless');
    writeFileSync(ogImage, readFileSync(lossless));
  }
} finally {
  rmSync(ogTemp, { recursive: true, force: true });
}
console.log(ogImage);
await browser.close();
