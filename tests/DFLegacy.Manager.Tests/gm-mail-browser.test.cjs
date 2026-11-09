const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { randomUUID } = require("node:crypto");

// Only the DOM boundary is simulated; the two production scripts build rows,
// bind events, resolve items, serialize requests, and manage pending operations.
class Element {
  constructor(tag = "div") {
    this.tagName = tag.toUpperCase(); this.children = []; this.listeners = {};
    this.value = ""; this.disabled = false; this.dataset = {}; this.attributes = {};
    this.className = ""; this.textContent = "";
    this.classList = {
      contains: value => this.className.split(/\s+/).includes(value),
      toggle: (value, force) => {
        const classes = new Set(this.className.split(/\s+/).filter(Boolean));
        (force ?? !classes.has(value)) ? classes.add(value) : classes.delete(value);
        this.className = [...classes].join(" ");
      },
      add: value => this.classList.toggle(value, true),
      remove: value => this.classList.toggle(value, false)
    };
  }
  append(...nodes) { for (const node of nodes) { this.children.push(node); node.parentElement = this; } }
  appendChild(node) { this.append(node); return node; }
  replaceChildren(...nodes) { this.children.forEach(node => node.parentElement = null); this.children = []; this.append(...nodes); }
  remove() { if (this.parentElement) this.parentElement.children = this.parentElement.children.filter(node => node !== this); this.parentElement = null; }
  setAttribute(name, value) { this.attributes[name] = String(value); }
  getAttribute(name) { return this.attributes[name] ?? null; }
  addEventListener(name, handler) { (this.listeners[name] ??= []).push(handler); }
  matches(selector) {
    const tag = selector.match(/^[a-z]+/i)?.[0];
    const id = selector.match(/#([\w-]+)/)?.[1];
    const classes = [...selector.matchAll(/\.([\w-]+)/g)].map(match => match[1]);
    const attrs = [...selector.matchAll(/\[([\w-]+)(?:="([^"]*)")?\]/g)];
    return (!tag || this.tagName === tag.toUpperCase()) && (!id || this.id === id)
      && classes.every(value => this.classList.contains(value))
      && attrs.every(([, name, value]) => value === undefined ? this[name] !== undefined : String(this[name]) === value);
  }
  querySelectorAll(selector) {
    return this.children.flatMap(node => [node, ...node.querySelectorAll("*")])
      .filter(node => selector.split(",").some(part => node.matches(part.trim())));
  }
  querySelector(selector) { return this.querySelectorAll(selector)[0] ?? null; }
  get options() { return this.children; }
  get blocked() { return this.disabled || (this.parentElement?.blocked ?? false); }
  async fire(type, extra = {}) {
    if (this.blocked && ["click", "input", "change"].includes(type)) return;
    const event = { target: this, preventDefault() {}, ...extra };
    for (let current = this; current; current = current.parentElement) {
      event.currentTarget = current;
      for (const handler of current.listeners[type] ?? []) await handler(event);
    }
  }
  click() { return this.fire("click"); }
  focus() {}
}

function mailPage(saved, fetcher) {
  const document = new Element("document"), requests = [], storage = new Map(saved ?? []), ready = [];
  document.createElement = tag => new Element(tag);
  document.addEventListener = (name, callback) => { if (name === "DOMContentLoaded") ready.push(callback); };
  function node(tag, id, parent = document, props = {}) {
    const element = Object.assign(new Element(tag), { id }, props); parent.append(element); return element;
  }
  const form = node("form", "mailForm"), fields = node("fieldset", "mailDraftFields", form);
  node("select", "mailScope", fields, { name: "scope", value: "single" });
  const characterField = node("label", "mailCharacterField", fields);
  node("select", "", characterField, { name: "characterId", value: "hero-1" });
  for (const [name, value] of [["sender", "GM"], ["text", "Reward"], ["gold", "50"]])
    node("input", "", fields, { name, value });
  node("div", "mailAttachmentRows", fields); node("p", "mailAttachmentCount", fields);
  node("button", "mailAddAttachment", fields, { type: "button" });
  node("button", "mailSubmit", form, { type: "submit" });
  for (const id of ["mailBatchPending", "mailPendingId", "mailBatchPreview", "mailBatchPreviewText", "mailBatchResult", "mailBatchSummary", "mailBatchId", "mailBatchFailures", "victoryOperation", "victoryBalance", "toast"])
    node("div", id);
  for (const id of ["mailBatchRecover", "mailBatchConfirm", "mailBatchCancel", "mailBatchRetry", "mailBatchNew"])
    node("button", id, document, { type: "button" });
  for (const id of ["directPopupForm", "directMessageForm", "broadcastPopupForm", "broadcastMessageForm", "dungeonForm", "victoryForm"])
    node("form", id);
  node("select", "", document.querySelector("#victoryForm"), { name: "characterId" });
  node("input", "", document.querySelector("#victoryForm"), { name: "amount" });
  const context = vm.createContext({ document, window: { confirm: () => true }, crypto: { randomUUID },
    sessionStorage: { getItem: key => storage.get(key), setItem: (key, value) => storage.set(key, value) },
    FormData: class {
      constructor(target) { this.entries = target.querySelectorAll("input, select, textarea").filter(item => item.name && !item.blocked).map(item => [item.name, item.value]); }
      get(name) { return this.entries.find(entry => entry[0] === name)?.[1] ?? null; }
    }, setTimeout() {}, clearTimeout() {},
    async fetch(url, options) {
      requests.push({ url, method: options?.method ?? "GET", body: options?.body && JSON.parse(options.body) });
      const body = await (fetcher?.(url, options) ?? (url.includes("/items?")
        ? [{ id: 101, name: "Potion", scriptKind: "Stackable", scriptPath: "stackable/potion.stk" },
          { id: 202, name: "Sword", scriptKind: "Equipment", scriptPath: "equipment/sword.equ" }]
        : url.endsWith("/preview") ? { status: "Prepared", targetCount: 2 } : {}));
      return { status: 200, ok: true, headers: { get: () => "application/json" }, json: async () => body };
    }
  });
  const run = code => vm.runInContext(code, context);
  for (const name of ["admin.js", "admin-rewards.js"])
    run(fs.readFileSync(path.join(__dirname, "../../src/DFLegacy.Server/wwwroot/admin", name), "utf8"));
  run('auth.role = ROLE_ADMIN; auth.userName = "GM"; state.characters = [{ id: "hero-1", name: "Hero" }]; bindForms();');
  // Initialize only this feature, avoiding unrelated login and server dashboards.
  if (run('typeof bindMailAttachments === "function"')) run("bindMailAttachments()");
  ready[1]();
  return { run, form, requests, storage, get: selector => document.querySelector(selector), rows: () => document.querySelectorAll(".mail-attachment-row") };
}

async function addItem(page, id, quantity = "1", kind = "1", ability = "0") {
  await page.get("#mailAddAttachment").click();
  const row = page.rows().at(-1);
  assert.ok(row, "adding an attachment creates an editable row");
  row.querySelector('[name="attachmentKind"]').value = kind;
  await row.querySelector('[name="attachmentKind"]').fire("change");
  row.querySelector(".item-search").value = id;
  row.querySelector('[name="quantity"]').value = quantity;
  row.querySelector('[name="avatarAbilityIndex"]').value = ability;
  return row;
}

test("multiple attachment rows submit independent items and quantities without legacy fields", async () => {
  const page = mailPage();
  await addItem(page, "101", "7"); await addItem(page, "202", "4", "2", "9");
  await page.form.fire("submit");
  const request = page.requests.find(item => item.url.endsWith("/mail"));
  assert.deepEqual(request?.body, { sender: "GM", text: "Reward", gold: 50, attachments: [
    { attachmentKind: 1, itemId: 101, quantity: 7, avatarAbilityIndex: 0 },
    { attachmentKind: 2, itemId: 202, quantity: 1, avatarAbilityIndex: 9 }
  ] });
});

test("removing rows preserves neighboring values and enforces the ten-slot management limit", async () => {
  const page = mailPage();
  const first = await addItem(page, "101", "3");
  await addItem(page, "202");
  await first.querySelector(".remove-attachment").click();
  assert.equal(page.rows().length, 1);
  assert.equal(page.rows()[0].querySelector(".item-search").value, "202");
  for (let index = 1; index < 11; index++) await page.get("#mailAddAttachment").click();
  assert.equal(page.rows().length, 10);
  assert.equal(page.get("#mailAddAttachment").disabled, true);
  await page.rows()[5].querySelector(".remove-attachment").click();
  assert.equal(page.get("#mailAddAttachment").disabled, false);
  assert.equal(page.rows()[5].querySelector("legend").textContent, "物品格 6");
});

test("text and gold mail submits an empty attachment list", async () => {
  const page = mailPage();
  await page.form.fire("submit");
  assert.deepEqual(page.requests.find(item => item.url.endsWith("/mail"))?.body,
    { sender: "GM", text: "Reward", gold: 50, attachments: [] });
});

test("prepared broadcast restores all slots after reload and keeps its original content locked", async () => {
  const page = mailPage();
  await addItem(page, "101", "8"); await addItem(page, "202", "1", "2", "9");
  page.get("#mailScope").value = "all";
  await page.form.fire("submit");
  assert.equal(page.get("#mailDraftFields").disabled, true);
  assert.equal(page.get("#mailSubmit").disabled, true);
  assert.match(page.get("#mailBatchPreviewText").textContent, /101.*8.*202.*1/s);
  const restored = mailPage(page.storage);
  assert.equal(restored.rows().length, 2);
  assert.equal(restored.rows()[0].querySelector(".item-search").value, "101");
  assert.equal(restored.rows()[0].querySelector('[name="quantity"]').value, "8");
  assert.equal(restored.rows()[1].querySelector('[name="avatarAbilityIndex"]').value, "9");
  assert.equal(restored.get("#mailDraftFields").disabled, true);
  await restored.get("#mailBatchRecover").click();
  assert.deepEqual(restored.requests.at(-1).body, page.requests.at(-1).body);
  await restored.get("#mailBatchCancel").click();
  assert.equal(restored.get("#mailDraftFields").disabled, false);
  assert.equal(restored.get("#mailSubmit").disabled, false);
});

test("legacy pending broadcasts recover their exact original request without losing the item", async () => {
  const mail = { sender: "GM", text: "Legacy", gold: 12, attachmentKind: 1, itemId: 101, quantity: 6, avatarAbilityIndex: 0 };
  const saved = new Map([["dflegacy.admin.rewards.GM", JSON.stringify({ version: 1, mail: {
    operationId: "legacy-batch", signature: JSON.stringify({ mail, retryOf: null }), mail, retryOf: null, phase: "prepared"
  } })]]);
  const page = mailPage(saved);
  assert.equal(page.rows().length, 1);
  assert.equal(page.rows()[0].querySelector(".item-search").value, "101");
  await page.get("#mailBatchRecover").click();
  assert.deepEqual(page.requests.at(-1).body, { operationId: "legacy-batch", mail, retryOf: null });
});

test("pending single sends reject duplicate submits until the request completes", async () => {
  let finish;
  const page = mailPage(undefined, () => new Promise(resolve => { finish = resolve; }));
  const first = page.form.fire("submit");
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(page.get("#mailSubmit").disabled, true);
  await page.form.fire("submit");
  assert.equal(page.requests.filter(item => item.url.endsWith("/mail")).length, 1);
  finish({}); await first;
  assert.equal(page.get("#mailSubmit").disabled, false);
});

test("each picker ignores stale results and changing a selected ID cannot send the old item", async () => {
  const replies = [];
  const page = mailPage(undefined, url => url.includes("/items?") ? new Promise(resolve => replies.push(resolve)) : {});
  const first = await addItem(page, "101"), second = await addItem(page, "202");
  // The fixture supports simple selectors, so find the query button within its row.
  const queryOne = first.querySelector(".search-row").querySelector("button");
  const queryTwo = second.querySelector(".search-row").querySelector("button");
  const old = queryOne.click(); await new Promise(resolve => setImmediate(resolve));
  first.querySelector(".item-search").value = "303";
  await first.querySelector(".item-search").fire("input");
  const current = queryOne.click(), other = queryTwo.click();
  await new Promise(resolve => setImmediate(resolve));
  replies[1]([{ id: 303, name: "<img src=x onerror=alert(1)>", scriptKind: "Stackable", scriptPath: "item303.stk" }]);
  replies[2]([{ id: 202, name: "Sword", scriptKind: "Equipment", scriptPath: "sword.equ" }]);
  await current; await other;
  replies[0]([{ id: 101, name: "Old", scriptKind: "Stackable", scriptPath: "old.stk" }]); await old;
  assert.equal(String(first.querySelector(".item-results").children[0].value), "303");
  assert.equal(String(second.querySelector(".item-results").children[0].value), "202");
  first.querySelector(".item-results").value = "303";
  await first.querySelector(".item-results").fire("change");
  assert.match(first.querySelector(".item-meta").textContent, /<img src=x/);
  assert.equal(first.querySelector(".item-meta").children.length, 0);
  first.querySelector(".item-search").value = "404";
  await first.querySelector(".item-search").fire("input");
  const submitted = page.form.fire("submit"); await new Promise(resolve => setImmediate(resolve));
  replies[3]([]); replies[4]([{ id: 202, scriptKind: "Equipment" }]); await submitted;
  assert.equal(page.requests.some(request => request.url.endsWith("/mail")), false);
  assert.match(page.get("#toast").textContent, /有效 PVF/);
  assert.equal(page.get("#mailDraftFields").disabled, false);
});

test("an invalid middle slot rejects the whole UI submission before any mail is sent", async () => {
  const page = mailPage();
  await addItem(page, "101", "2"); await addItem(page, "101", "1.5"); await addItem(page, "202");
  await page.form.fire("submit");
  assert.equal(page.requests.some(request => request.url.endsWith("/mail")), false);
  assert.match(page.get("#toast").textContent, /正整数/);
});

test("a send in flight cannot be recovered or cancelled until its response settles", async () => {
  let finish;
  const page = mailPage(undefined, url => url.endsWith("/send") ? new Promise(resolve => { finish = resolve; })
    : { status: "Prepared", targetCount: 2 });
  page.get("#mailScope").value = "all"; await page.form.fire("submit");
  const sending = page.get("#mailBatchConfirm").click();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(page.get("#mailBatchRecover").disabled, true);
  assert.equal(page.get("#mailBatchCancel").disabled, true);
  await page.get("#mailBatchRecover").click();
  await page.get("#mailBatchCancel").click();
  assert.equal(page.requests.filter(request => request.url.endsWith("/preview")).length, 1);
  finish({ status: "Completed", operationId: page.run("activeMailBatch.operationId"), targetCount: 2, sentCount: 2, failedCount: 0, skippedCount: 0, deliveries: [] });
  await sending;
  assert.equal(page.get("#mailDraftFields").disabled, true);
  await page.get("#mailBatchNew").click();
  assert.equal(page.get("#mailDraftFields").disabled, false);
});

test("ten identical item slots remain ten separate attachments in the request", async () => {
  const page = mailPage();
  for (let index = 0; index < 10; index++) await addItem(page, "101", String(index + 1));
  await page.form.fire("submit");
  const sent = page.requests.find(request => request.url.endsWith("/mail"));
  assert.equal(sent.body.attachments.length, 10);
  assert.deepEqual(sent.body.attachments.map(item => item.quantity), [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
  assert.equal(sent.body.gold, 50);
});

test("a preview error keeps the saved draft recoverable and can be cancelled for editing", async () => {
  const page = mailPage(undefined, () => { throw new Error("Preview unavailable"); });
  page.get("#mailScope").value = "all";
  await page.form.fire("submit");
  assert.equal(page.get("#mailDraftFields").disabled, true);
  assert.equal(page.get("#mailBatchPending").classList.contains("hidden"), false);
  assert.equal(page.get("#mailBatchCancel").disabled, false);
  assert.equal(page.get("#mailBatchConfirm").disabled, true);
  await page.get("#mailBatchCancel").click();
  assert.equal(page.get("#mailDraftFields").disabled, false);
  assert.equal(JSON.parse(page.storage.get("dflegacy.admin.rewards.GM")).mail, null);
});

test("an unknown send outcome remains locked and recovery uses the same batch identity", async () => {
  const page = mailPage(undefined, url => {
    if (url.endsWith("/send")) throw new Error("Connection lost");
    return { status: "Prepared", targetCount: 2 };
  });
  page.get("#mailScope").value = "all";
  await page.form.fire("submit");
  const preview = page.requests[0];
  await page.get("#mailBatchConfirm").click();
  assert.equal(page.get("#mailDraftFields").disabled, true);
  assert.equal(page.get("#mailBatchCancel").disabled, true);
  assert.equal(page.get("#mailBatchConfirm").disabled, true);
  assert.equal(page.get("#mailBatchRecover").disabled, false);
  await page.get("#mailBatchNew").click();
  assert.equal(page.run("activeMailBatch.operationId"), preview.body.operationId);
  await page.get("#mailBatchRecover").click();
  assert.deepEqual(page.requests.at(-1).body, preview.body);
  assert.equal(page.requests.filter(request => request.url.endsWith("/send")).length, 1);
  assert.equal(page.get("#mailBatchConfirm").disabled, false);
});
