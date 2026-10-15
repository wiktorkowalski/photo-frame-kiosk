// Local preview: serves _site on :8080 and runs the MZK Worker's fetch handler on :8787.
// Open http://localhost:8080/?frame=dxmart&mzk=http://localhost:8787
import { readFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { extname, join, normalize } from 'node:path';
import worker from '../worker/src/index.js';

const SITE_PORT = 8080;
const WORKER_PORT = 8787;
const SITE_DIR = '_site';
const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json' };

createServer(async (request, response) => {
  const path = normalize(new URL(request.url, 'http://localhost').pathname).replace(/^(\.\.[/\\])+/, '');
  const file = join(SITE_DIR, path.endsWith('/') ? `${path}index.html` : path);
  try {
    const body = await readFile(file);
    response.writeHead(200, { 'Content-Type': TYPES[extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-cache' });
    response.end(body);
  } catch {
    response.writeHead(404).end('not found');
  }
}).listen(SITE_PORT, () => console.log(`site   http://localhost:${SITE_PORT}/?frame=dxmart&mzk=http://localhost:${WORKER_PORT}`));

createServer(async (request, response) => {
  const headers = Object.entries(request.headers).filter(([, value]) => typeof value === 'string');
  const result = await worker.fetch(new Request(new URL(request.url, `http://localhost:${WORKER_PORT}`), { method: request.method, headers }));
  response.writeHead(result.status, Object.fromEntries(result.headers));
  response.end(Buffer.from(await result.arrayBuffer()));
}).listen(WORKER_PORT, () => console.log(`worker http://localhost:${WORKER_PORT}/departures?stops=11`));
