'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root,p),'utf8');
const view = read('EduOS.App/Views/Account/Profile.cshtml');
const script = read('EduOS.App/wwwroot/js/account/profile.js');
const controller = read('EduOS.App/Controllers/AccountController.cs');
const {guideFor} = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');

test('profile page is server-first bilingual, keyboard accessible and read-gated', () => {
    assert.match(view,/CultureInfo\.CurrentUICulture\.Name == "bn-BD"/);
    for (const label of ['আমার প্রোফাইল','ব্যক্তিগত তথ্য','পাসওয়ার্ড পরিবর্তন',
        'রিকভারি কোড','পরিবর্তন সংরক্ষণ করুন']) assert(view.includes(label), label);
    for (const id of ['saveProfileBtn','changePasswordBtn','mfaDisableBtn',
        'mfaRegenerateBtn','sessionRevokeOthersBtn','sessionRevokeAllBtn'])
        assert(new RegExp('<button[^>]*id="'+id+'"[^>]*disabled').test(view), id+' must start disabled');
    assert.match(view,/aria-label="@\(profileBn \?/);
    assert.match(view,/id="alertContainer" aria-live="polite"/);
    assert.match(controller,/\[Authorize\]\s*\[HttpGet\]\s*\[ResponseCache\(NoStore = true, Location = ResponseCacheLocation.None\)\]\s*public IActionResult Profile\(\)/);
});
test('profile client rejects stale session reads and unsafe writes without exposing private server errors', () => {
    assert.match(script,/received !== expectedSession \|\| receivedTenant !== expectedTenant/);
    assert.match(script,/document\.body\?\.dataset\.authSessionReference !== expectedSession/);
    assert.match(script,/window\.EduOSSessionTransitionPending\?\.\(\) === true/);
    assert.match(script,/await window\.EduOSVerifySessionContext\(\)/);
    assert.match(script,/document\.getElementById\('eduosSessionShield'\)\?\.hidden !== true/);
    assert.match(script,/lockProfile\(\)/);
    assert.match(script,/profileOriginalTenant/);
    assert.match(script,/profileOriginalSession/);
    assert.match(script,/expectedTenant !== profileOriginalTenant/);
    assert.match(script,/button.disabled = loading \|\| profileLocked \|\| !profileReady/);
    assert.match(script,/profileReadGeneration/);
    assert.match(script,/sessionsReadGeneration/);
    assert.match(script,/mfaReadGeneration/);
    assert.doesNotMatch(script,/result\?\.message|response\?\.message|response\.message/);
    assert.match(script,/if \(!response\.ok\) throw new Error\(profileText\('Request failed\. Please retry\.'\)\)/);
});
test('profile has a reviewed bilingual contextual guide with actual security workflows', () => {
    const en = guideFor('/Account/Profile','en-BD');
    const bn = guideFor('/Account/Profile','bn-BD');
    assert.equal(en.version,1);
    assert.equal(bn.version,1);
    assert.equal(en.steps.length,4);
    assert.equal(bn.steps.length,4);
    assert.match(en.rules,/recovery codes/);
    assert.match(bn.recovery,/সেশন/);
    assert.deepEqual(en.related,bn.related);
});

test('profile write responses and denied security actions stay fail-closed', () => {
    assert.match(script,/Recheck every completed request, including successful writes/);
    assert.match(script,/await loadProfile\(\);\s*if \(profileLocked \|\| !profileReady\) return;/);
    assert.match(script,/document\.body\?\.dataset\.authTenantId !== expectedTenant/);
    assert.match(script,/revoke\.disabled = profileLocked \|\| !sessionsReady/);
    assert.doesNotMatch(script,/button\.disabled = false;/);
    const browser=read('tests/browser/account-profile.cjs');
    for(const check of ['switchedDuringSuccessfulWrite','deniedSecurityWrite',
        'mfaDisableBtn','mfaRegenerateBtn','sessionRevokeOthersBtn','sessionRevokeAllBtn',
        'PRIVATE_FOREIGN_TENANT_RECORD'])assert(browser.includes(check),check);
});

test('profile rejects a successful-looking response after JSON parsing or scope drift', () => {
    assert.match(script,/const payload = await response\.json\(\);/);
    assert.match(script,/if \(payload\?\.success !== true\)/);
    const tail=script.slice(script.indexOf('const payload = await response.json();'));
    assert.match(tail,/document\.body\?\.dataset\.authSessionReference !== expectedSession/);
    assert.match(tail,/document\.body\?\.dataset\.authTenantId !== expectedTenant/);
    assert.match(script,/typeof profile\.fullName !== 'string'/);
    assert.match(script,/typeof profile\.email !== 'string'/);
    const browser=read('tests/browser/account-profile.cjs');
    assert.match(browser,/async function switchedWhileParsingJson/);
    assert.match(browser,/async function falseSuccessRead/);
});

test('profile MFA and password actions are localized and toggles announce their state', () => {
    assert.match(view,/MFA চালু করুন" : "Set Up MFA"/);
    assert.match(view,/পাসওয়ার্ড পরিবর্তন" : "Change Password"\)/);
    assert.equal((view.match(/aria-pressed="false"/g)||[]).length,3);
    assert.equal((view.match(/data-hide-label=/g)||[]).length,3);
    assert.match(script,/button\.setAttribute\('aria-pressed', String\(showPassword\)\)/);
    assert.match(script,/button\.setAttribute\('aria-label', showPassword/);
});
