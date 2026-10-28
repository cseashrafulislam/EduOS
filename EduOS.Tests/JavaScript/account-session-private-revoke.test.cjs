const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../');
const script = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/account/profile.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Account/Profile.cshtml'), 'utf8');
test('single-session revoke requires password for other devices and never renders session IDs', () => {
  const start = script.indexOf('async function loadActiveSessions()');
  assert.ok(start >= 0);
  const handler = script.slice(start);
  assert.match(handler, /if \(!session\.isCurrent && !currentPassword\)/);
  assert.match(handler, /currentPassword: session\.isCurrent \? null : currentPassword/);
  assert.match(handler, /sessionId: session\.sessionId/);
  assert.match(handler, /if \(session\.isCurrent\) window\.location\.assign/);
  assert.doesNotMatch(handler, /textContent\s*=\s*session\.sessionId/);
  assert.match(view, /data-password-required="@T\["SessionPasswordRequired"\]"/);
  assert.match(view, /<details class="position-relative">/);
});
