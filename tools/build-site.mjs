// Builds the GitHub Pages site: site/ as is, plus config.json made from screens.json.
// A screens.json the frames cannot use fails the build, so Pages keeps the last good site.
// Usage: MZK_API_URL=https://frame-mzk.<account>.workers.dev node tools/build-site.mjs [outDir]
import { createHash } from 'node:crypto';
import { cpSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { parseJsonc, validateConfig } from '../site/schedule.js';

const outDir = process.argv[2] || '_site';
const siteDir = 'site';

let screens;
try {
  screens = parseJsonc(readFileSync('screens.json', 'utf8'));
} catch (error) {
  console.error(`screens.json is not valid JSON: ${error.message}`);
  process.exit(1);
}

const errors = validateConfig(screens);
const mzkApi = (process.env.MZK_API_URL || '').trim();
if (!mzkApi && Object.values(screens.screens || {}).some((screen) => screen && screen.type === 'departures')) {
  errors.push('A "departures" screen needs the MZK Worker: set the repo variable MZK_API_URL (Settings → Variables).');
}
if (errors.length > 0) {
  console.error('screens.json has errors:');
  for (const error of errors) console.error(`  - ${error}`);
  process.exit(1);
}

// The version covers the page code only. A frame reloads when it changes, not on a screens.json edit.
const hash = createHash('sha256');
for (const name of readdirSync(siteDir).sort()) hash.update(name).update(readFileSync(join(siteDir, name)));
const version = hash.digest('hex').slice(0, 12);

rmSync(outDir, { recursive: true, force: true });
mkdirSync(outDir, { recursive: true });
cpSync(siteDir, outDir, { recursive: true });

// Versioned script URLs: a reload after a deploy must not take frame.js from the WebView cache.
function rewrite(name, pairs) {
  const path = join(outDir, name);
  let text = readFileSync(path, 'utf8');
  for (const [from, to] of pairs) {
    if (!text.includes(from)) throw new Error(`${name} no longer contains ${from}; update tools/build-site.mjs`);
    text = text.replaceAll(from, to);
  }
  writeFileSync(path, text);
}
rewrite('index.html', [['src="frame.js"', `src="frame.js?v=${version}"`], ['href="frame.css"', `href="frame.css?v=${version}"`]]);
rewrite('stops.html', [['src="stops.js"', `src="stops.js?v=${version}"`]]);
rewrite('frame.js', [["from './schedule.js'", `from './schedule.js?v=${version}'`], ["const CODE_VERSION = 'dev';", `const CODE_VERSION = '${version}';`]]);

writeFileSync(
  join(outDir, 'config.json'),
  JSON.stringify({ version, mzkApi, screens: screens.screens, frames: screens.frames }),
);

const frameIds = Object.keys(screens.frames);
console.log(`Built ${outDir}: version ${version}, frames: ${frameIds.join(', ')}, MZK API: ${mzkApi || '(not set)'}`);
