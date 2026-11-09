const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const source = fs.readFileSync(path.join(__dirname, "../../src/DFLegacy.Server/wwwroot/admin/admin.js"), "utf8");

function gmPage() {
  const requests = [];
  const prompts = [];
  const elements = new Map();
  let confirmed = false;
  function node() {
    const listeners = {};
    return {
      children: [],
      querySelectorAll() { return []; },
      classList: { add() {}, remove() {}, toggle() {} },
      append(...children) { this.children.push(...children); },
      replaceChildren(...children) { this.children = children; },
      addEventListener(name, callback) { listeners[name] = callback; },
      async click() { if (!this.disabled) await listeners.click?.({ currentTarget: this }); },
      submit() { return listeners.submit?.({ preventDefault() {}, currentTarget: this }); }
    };
  }
  function element(selector) {
    if (!elements.has(selector)) elements.set(selector, node());
    return elements.get(selector);
  }
  const context = vm.createContext({
    document: { querySelector: element, querySelectorAll: () => [], createElement: node, addEventListener() {} },
    window: { confirm(message) { prompts.push(message); return confirmed; } },
    FormData: class {
      constructor(form) { this.values = form.values; }
      get(name) { return this.values[name] ?? null; }
    },
    setTimeout() {}, clearTimeout() {},
    async fetch(url, options) {
      requests.push([url, options.method]);
      return { status: 200, ok: true, headers: { get: () => "application/json" }, json: async () => [] };
    }
  });
  vm.runInContext(source, context);
  return { run: code => vm.runInContext(code, context), element, requests, prompts, confirm: () => { confirmed = true; } };
}

test("GM can confirm deletion of an offline character, with online deletion disabled", async () => {
  const { run, element, requests, prompts, confirm } = gmPage();
  run(`
    auth.role = ROLE_ADMIN;
    state.characters = [
      { id: "online-id", userName: "Example", name: "Online", level: 1, online: true },
      { id: "offline-id", userName: "Example", name: "Offline", level: 1, online: false }
    ];
    renderManagedCharacters("Example");
  `);
  const [online, offline] = element("#managedCharacterList").children.map(row => row.children.at(-1));
  assert.equal(online.disabled, true);
  await online.click();
  await offline.click();
  assert.deepEqual(requests, []);
  confirm();
  await offline.click();
  assert.match(prompts.at(-1), /Example.*Offline/);
  assert.deepEqual(requests[0], ["/api/admin/characters/offline-id", "DELETE"]);
});

test("invalid timed duration cannot become a permanent ban request", async () => {
  const { run, element, requests, prompts, confirm } = gmPage();
  element("#gmAccountDialog").dataset = { userName: "Example" };
  element("#gmBanForm").values = { mode: "timed", durationHours: "invalid", reason: "Violation" };
  confirm();
  run("bindAccountManagement()");
  await element("#gmBanForm").submit();
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(prompts, []);
  assert.deepEqual(requests, []);
  assert.match(element("#toast").textContent, /封禁时长/);
});
