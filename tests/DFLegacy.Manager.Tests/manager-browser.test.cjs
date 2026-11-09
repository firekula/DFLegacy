const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

async function page() {
  const elements = new Map(), storage = new Map();
  function element(selector) {
    if (!elements.has(selector)) elements.set(selector, { hidden: false, files: [], checked: true, textContent: '',
      addEventListener() {}, removeAttribute(name) { delete this[name]; }, classList: { toggle() {} },
      click() { this.clicks = (this.clicks || 0) + 1; } });
    return elements.get(selector);
  }
  const response = body => ({ ok: true, status: 200, text: async () => JSON.stringify(body) });
  const context = vm.createContext({ console, URLSearchParams, AbortSignal, Date, Promise,
    document: { querySelector: element, querySelectorAll: () => [] },
    location: { hash: '', pathname: '/', search: '' }, history: { replaceState() {} },
    sessionStorage: { getItem: key => storage.get(key) || null, setItem: (k, v) => storage.set(k, v), removeItem: k => storage.delete(k) },
    setInterval() {}, setTimeout, clearTimeout,
    fetch: async () => response({ required: true }) });
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../../src/DFLegacy.Manager/wwwroot/manager.js'), 'utf8'), context);
  await new Promise(resolve => setImmediate(resolve));
  return { context, element, response, storage, run: code => vm.runInContext(code, context) };
}

test('log polling never overlaps, renders results and recovers after errors', async () => {
  const p = await page();
  p.run('loggedIn = true; currentView = "logs"');
  let resolve, calls = 0;
  p.context.fetch = () => { calls++; return new Promise(r => resolve = r); };
  const first = p.run('refreshLogs()');
  await p.run('refreshLogs()');
  assert.equal(calls, 1);
  resolve(p.response([{ id: 100, time: '2026-11-02T00:00:00Z', source: 'export', text: 'compression complete' }]));
  await first;
  assert.match(p.element('#logOutput').textContent, /compression complete/);
  p.context.fetch = async () => { throw new Error('connection timed out'); };
  await p.run('refreshLogs()');
  assert.match(p.element('#logStatus').textContent, /connection timed out/);
  p.context.fetch = async () => p.response([]);
  await p.run('refreshLogs()');
  assert.match(p.element('#logStatus').textContent, /日志已连接/);
});

test('status polling never accumulates requests while a response is pending', async () => {
  const p = await page(); p.run('loggedIn = true');
  let reject, calls = 0;
  p.context.fetch = () => { calls++; return new Promise((_, r) => reject = r); };
  const first = p.run('refreshStatus()');
  await p.run('refreshStatus()');
  assert.equal(calls, 1);
  reject(new Error('offline')); await assert.rejects(first, /offline/);
  assert.equal(p.run('statusPending'), false);
});

test('dashboard opens dedicated panel roots and hides disabled panels', async () => {
  const p = await page(); p.run('loggedIn = true');
  const status = { server: { state: 'running' }, accountServiceUrl: 'http://game.example:8081', gmPanelUrl: 'http://game.example:18081/', playerPanelUrl: 'http://game.example:18082/' };
  p.context.fetch = async url => p.response(url === '/api/status' ? status : { available: false });
  await p.run('refreshStatus()');
  assert.equal(p.element('#gmLink').href, status.gmPanelUrl);
  assert.equal(p.element('#playerLink').href, status.playerPanelUrl);
  status.playerPanelUrl = null;
  await p.run('refreshStatus()');
  assert.equal(p.element('#playerLink').hidden, true);
  assert.equal(p.element('#playerLink').href, undefined);
});

test('completed generation hands off a direct download without buffering ZIP in JavaScript', async () => {
  const p = await page(); p.run('loggedIn = true');
  const requests = [];
  p.context.fetch = async url => {
    requests.push(url);
    if (url === '/api/exports') return p.response({ id: 'job123' });
    if (url === '/api/exports/job123') {
      p.run('loggedIn = false'); // Do not run unrelated dashboard refresh in this unit test.
      return p.response({ state: 'ready', size: 80740352, progress: { stage: 'ready', totalBytes: 80740352, completedBytes: 80740352, message: 'ready' } });
    }
    throw new Error('Unexpected request ' + url);
  };
  await p.run('generateClient()');
  assert.equal(p.element('#exportDownload').href, '/api/exports/job123/download');
  assert.equal(p.element('#exportDownload').clicks, 1);
  assert.match(p.element('#exportProgressText').textContent, /浏览器下载/);
  assert.deepEqual(requests, ['/api/exports', '/api/exports/job123']);
  assert.equal(p.storage.size, 0);
});

test('transient job polling failure preserves the ID for a cheap retry', async () => {
  const p = await page(); p.run('loggedIn = true'); p.storage.set('dflegacy-export-job', 'existing');
  p.context.fetch = async url => { p.run('loggedIn = false'); throw new Error('timeout'); };
  await p.run('generateClient()');
  assert.equal(p.storage.get('dflegacy-export-job'), 'existing');
  assert.match(p.element('#exportProgressText').textContent, /timeout/);
});

test('server operations get a longer deadline than polling requests', async () => {
  const p = await page();
  let signal;
  p.context.fetch = async (_, options) => { signal = options.signal; return p.response({}); };
  await p.run('post("/api/server/restart")');
  // AbortSignal.timeout is replaced with a recorder to check deadlines without waiting.
  const durations = [];
  p.context.AbortSignal = { timeout: ms => { durations.push(ms); return signal; } };
  await p.run('post("/api/server/restart")');
  await p.run('request("/api/status")');
  assert.deepEqual(durations, [120000, 15000]);
});
