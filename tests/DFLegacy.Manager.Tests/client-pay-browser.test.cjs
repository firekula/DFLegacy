const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const root = path.join(__dirname, '../../src/DFLegacy.Server/wwwroot/client');

function paymentPage(name, auth, testMode = false) {
  const nodes = new Map();
  const get = id => {
    if (!nodes.has(id)) nodes.set(id, { value: id === 'amount_value' ? '500' : '', style: {}, innerHTML: '' });
    return nodes.get(id);
  };
  const form = { elements: { account: { value: 'test-account' }, characterName: { value: '' } }, reset() {}, submit() { this.submitted = true; } };
  const context = vm.createContext({ window: { PayAuth: auth, DFLegacyClientPay: { testCeraEnabled: testMode } }, PayAuth: auth,
    document: { forms: { formMain: form, diamondForm: form }, getElementById: get } });
  const html = fs.readFileSync(path.join(root, name), 'latin1');
  for (const match of html.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/gi))
    if (match[1].trim()) vm.runInContext(match[1], context);
  return { context, get, html, form };
}

for (const name of ['client_pay.htm', 'client_pay_bank.htm']) {
  test(name + ': test mode posts a UTF-8 character form without authentication XHR', () => {
    const p = paymentPage(name, undefined, true);
    p.form.elements.account.value = '  火焰库拉  ';
    p.get('amount_value').value = '37';
    assert.equal(vm.runInContext('submitPayForm()', p.context), false);
    assert.equal(p.form.submitted, true);
    assert.equal(p.form.elements.characterName.value, '火焰库拉');
    assert.equal(p.form.action, '/api/client-pay/test-cera');
    assert.equal(p.form.method, 'post');
    assert.equal(p.form.target, 'payResult');
    assert.match(p.html, /accept-charset="UTF-8"/);
    assert.match(p.html, /name="payResult"/);
  });
  test(name + ': test mode rejects invalid quantities before submitting', () => {
    for (const amount of ['0', '-1', '1.5', '1000001', '12garbage', '']) {
      const p = paymentPage(name, undefined, true);
      p.get('amount_value').value = amount;
      vm.runInContext('submitPayForm()', p.context);
      assert.equal(p.form.submitted, undefined, amount);
    }
  });
}

for (const [name, submit, message] of [
  ['client_pay.htm', 'submitPayForm', 'pay_msg'],
  ['client_pay_bank.htm', 'submitPayForm', 'pay_msg'],
  ['client_diamond.htm', 'submitDiamondForm', 'diamond_msg']
]) {
  test(name + ': missing authentication script gives an inline error, not a script dialog', () => {
    const p = paymentPage(name);
    assert.equal(vm.runInContext(submit + '()', p.context), false);
    assert.match(p.get(message).innerHTML, /加载失败/);
  });
  test(name + ': successful and failed callbacks update the correct page message', () => {
    let callback;
    const p = paymentPage(name, { post: (_, body, done) => { callback = done; } });
    vm.runInContext(submit + '()', p.context);
    callback(true, 200);
    assert.match(p.get(message).innerHTML, /成功/);
    callback(false, 403);
    assert.match(p.get(message).innerHTML, /管理员/);
  });
}

test('layout follows the actual browser viewport without changing the fixed design canvas', () => {
  const handlers = {};
  const body = { style: {} };
  const document = { body, documentElement: { clientWidth: 1150, clientHeight: 790 }, readyState: 'complete' };
  const window = { innerWidth: 1150, innerHeight: 790, addEventListener: (name, fn) => handlers[name] = fn };
  const context = vm.createContext({ window, document, Math, isFinite });
  vm.runInContext(fs.readFileSync(path.join(root, 'client-layout.js'), 'utf8'), context);
  assert.equal(Number(body.style.zoom), 2.5);
  window.innerWidth = 460; window.innerHeight = 316;
  handlers.resize();
  assert.equal(Number(body.style.zoom), 1);
  window.innerWidth = 0; window.innerHeight = 0;
  document.documentElement.clientWidth = 0; document.documentElement.clientHeight = 0;
  handlers.resize();
  assert.equal(Number(body.style.zoom), 1);
});

test('legacy resize event path scales pages without addEventListener', () => {
  const handlers = {}, body = { style: {} };
  const document = { body, documentElement: { clientWidth: 575, clientHeight: 395 } };
  const window = { attachEvent: (name, fn) => handlers[name] = fn };
  vm.runInNewContext(fs.readFileSync(path.join(root, 'client-layout.js'), 'utf8'), { window, document, Math, isFinite });
  assert.equal(Number(body.style.zoom), 1.25);
  document.documentElement.clientWidth = 920; document.documentElement.clientHeight = 632;
  handlers.onresize();
  assert.equal(Number(body.style.zoom), 2);
});

test('all legacy client pages load the layout script and opt into the installed IE engine', () => {
  for (const name of fs.readdirSync(root).filter(name => /^client.*\.htm$/.test(name))) {
    const html = fs.readFileSync(path.join(root, name), 'latin1');
    assert.equal((html.match(/src="client-layout\.js"/g) || []).length, 1, name);
    assert.ok(html.indexOf('X-UA-Compatible') < html.indexOf('charset=gb2312'), name);
  }
  assert.ok([...fs.readFileSync(path.join(root, 'client-layout.js'))].every(byte => byte < 128));
});
