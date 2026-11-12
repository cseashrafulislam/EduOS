'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const source = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/facilities/costs.js'), 'utf8');
const start = source.indexOf('    async function mutate(action, path, requestBody, callback) {');
const end = source.indexOf('    function hideDecision()', start);
assert.ok(start >= 0 && end > start, 'canonical facility cost mutation handler exists');
const factory = new Function('api', 'opId', 'attempts', 'notice', 't', 'state',
  'let busy = false; let locked = false;\n' + source.slice(start, end) +
  '\nreturn { mutate, getBusy: () => busy };');

function harness(api) {
  const attempts = new Map();
  const messages = [];
  const t = english => english;
  const opId = (action, payload) => {
    const signature = action + JSON.stringify(payload);
    if (!attempts.has(signature)) attempts.set(signature, 'stable-command-id');
    return { signature, id: attempts.get(signature) };
  };
  const instance = factory(api, opId, attempts, (message, success) => messages.push({ message, success }), t, {});
  return { ...instance, attempts, messages };
}

test('committed vendor write remains successful when subsequent refresh fails', async () => {
  let writes = 0;
  const h = harness(async () => { writes++; return { reference: 'bill-1' }; });
  await h.mutate('post', '/vendor/post', { rowVersion: 'v1' }, async () => { throw new Error('refresh offline'); });
  assert.equal(writes, 1);
  assert.equal(h.attempts.size, 0, 'committed command id must be cleared');
  assert.equal(h.messages.at(-1).success, true);
  assert.match(h.messages.at(-1).message, /do not submit the transaction again/i);
  assert.equal(h.getBusy(), false);
});

test('failed write retains idempotency key and reports failure', async () => {
  const h = harness(async () => { throw new Error('write rejected'); });
  await h.mutate('post', '/vendor/post', { rowVersion: 'v1' }, async () => {});
  assert.equal(h.attempts.size, 1);
  assert.equal(h.messages.at(-1).success, undefined);
  assert.match(h.messages.at(-1).message, /write rejected/);
  assert.equal(h.getBusy(), false);
});

test('duplicate concurrent mutation is blocked while first request is pending', async () => {
  let release;
  let writes = 0;
  const h = harness(async () => { writes++; await new Promise(resolve => { release = resolve; }); return {}; });
  const first = h.mutate('create', '/vendor', { amount: 12 }, async () => {});
  await h.mutate('create', '/vendor', { amount: 12 }, async () => {});
  assert.equal(writes, 1);
  release();
  await first;
  assert.equal(h.getBusy(), false);
});

test('stale vendor and labour source responses compare live search text', () => {
  assert.match(source, /generation !== vendorChoicesGeneration[^;]*fcVendorSearch[^;]*!== query/);
  assert.match(source, /generation !== labourChoicesGeneration[^;]*fcLabourSearch[^;]*!== query/);
});
