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
writeFileSync(
  join(outDir, 'config.json'),
  JSON.stringify({ version, mzkApi: process.env.MZK_API_URL || '', screens: screens.screens, frames: screens.frames }),
);

const frameIds = Object.keys(screens.frames);
console.log(`Built ${outDir}: version ${version}, frames: ${frameIds.join(', ')}, MZK API: ${process.env.MZK_API_URL || '(not set)'}`);
