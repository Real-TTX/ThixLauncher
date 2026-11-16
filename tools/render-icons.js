// Renders the ThixLauncher logo (branding/*.svg) into every file the app needs, with the installed Microsoft Edge (headless).
//
//   cd tools && npm install && node render-icons.js
//
// Detail levels (see branding/README.md):
//   16-32 px   logo-small.svg  (four thick tiles, full-bleed)
//   48+ px     logo-icon.svg   (four tiles on the blue tile of the family, the fourth starts)
//   tray       logo-tray.svg   (one colour; macOS template image)
//   full       logo-full.svg   (icon + word mark)
// Output:
//   branding/png/*                             every size as PNG (the macOS packaging script builds the .icns from them)
//   src/ThixLauncher/Assets/thixlauncher.ico   Windows program and window icon (16-256)
//   src/ThixLauncher/Assets/*.png              window logo, tray icons
'use strict';

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright-core');

const root = path.resolve(__dirname, '..');
const branding = path.join(root, 'branding');
const pngDir = path.join(branding, 'png');
const appAssets = path.join(root, 'src', 'ThixLauncher', 'Assets');

const svg = (name) => fs.readFileSync(path.join(branding, name), 'utf8');

// An SVG drawn at a size, optionally scaled inside the canvas (macOS icons keep a margin around the tile).
function page(source, size, scale = 1) {
  const inner = size * scale;
  const offset = (size - inner) / 2;
  const sized = source
    .replace(/<svg([^>]*?)\swidth="[^"]*"/, '<svg$1')
    .replace(/<svg([^>]*?)\sheight="[^"]*"/, '<svg$1')
    .replace('<svg', `<svg width="${inner}" height="${inner}"`);
  return `<!doctype html><html><head><style>html,body{margin:0;padding:0;background:transparent}` +
    `div{position:absolute;left:${offset}px;top:${offset}px;width:${inner}px;height:${inner}px}svg{display:block}</style></head>` +
    `<body><div>${sized}</div></body></html>`;
}

async function render(browser, source, size, scale = 1) {
  const context = await browser.newContext({ viewport: { width: size, height: size }, deviceScaleFactor: 1 });
  const tab = await context.newPage();
  await tab.setContent(page(source, size, scale));
  const bytes = await tab.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
  await context.close();
  return bytes;
}

// ICO with PNG entries (Windows Vista and later).
function ico(images) {
  const header = Buffer.alloc(6 + 16 * images.length);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(images.length, 4);
  let offset = header.length;
  images.forEach(({ size, bytes }, i) => {
    const at = 6 + 16 * i;
    header.writeUInt8(size >= 256 ? 0 : size, at);
    header.writeUInt8(size >= 256 ? 0 : size, at + 1);
    header.writeUInt8(0, at + 2);
    header.writeUInt8(0, at + 3);
    header.writeUInt16LE(1, at + 4);
    header.writeUInt16LE(32, at + 6);
    header.writeUInt32LE(bytes.length, at + 8);
    header.writeUInt32LE(offset, at + 12);
    offset += bytes.length;
  });
  return Buffer.concat([header, ...images.map((image) => image.bytes)]);
}

(async () => {
  for (const dir of [pngDir, appAssets]) fs.mkdirSync(dir, { recursive: true });
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    const small = svg('logo-small.svg');
    const icon = svg('logo-icon.svg');
    const tray = svg('logo-tray.svg');
    const forSize = (size) => (size <= 32 ? small : icon);

    // App icon in every size.
    const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256, 512, 1024];
    const bySize = {};
    for (const size of sizes) {
      bySize[size] = await render(browser, forSize(size), size);
      fs.writeFileSync(path.join(pngDir, `thixlauncher-${size}.png`), bySize[size]);
    }

    // macOS app icon: the tile keeps Apple's margin.
    for (const size of [16, 32, 64, 128, 256, 512, 1024]) {
      const bytes = await render(browser, forSize(size), size, 0.92);
      fs.writeFileSync(path.join(pngDir, `thixlauncher-mac-${size}.png`), bytes);
    }

    // Windows program icon, the window logo and the mark the tray icons are drawn from.
    const icoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
    fs.writeFileSync(path.join(appAssets, 'thixlauncher.ico'), ico(icoSizes.map((size) => ({ size, bytes: bySize[size] }))));
    fs.writeFileSync(path.join(appAssets, 'logo-256.png'), bySize[256]);
    fs.writeFileSync(path.join(appAssets, 'logo-64.png'), bySize[64]);

    // Tray: Windows draws the colour icon with the dot of the state (MatTrayIcons), macOS the template glyph (22 pt menu bar).
    const trayBytes = await render(browser, tray, 44, 0.82);
    fs.writeFileSync(path.join(appAssets, 'tray-macos.png'), trayBytes);
    fs.writeFileSync(path.join(pngDir, 'thixlauncher-tray-44.png'), trayBytes);

    // The full logo (README, about pages).
    const context = await browser.newContext({ viewport: { width: 900, height: 200 } });
    const tab = await context.newPage();
    await tab.setContent(`<!doctype html><html><body style="margin:0;background:transparent">${svg('logo-full.svg')}</body></html>`);
    fs.writeFileSync(path.join(pngDir, 'thixlauncher-full.png'),
      await tab.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: 900, height: 200 } }));
    await context.close();

    console.log('Icons written to', pngDir, appAssets);
  } finally {
    await browser.close();
  }
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
