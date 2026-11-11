const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const script = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/students/progressions.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/StudentProgressions/Index.cshtml'), 'utf8');

test('progression requires a rendered matching preview and explicit confirmation', () => {
    assert.match(view, /id="promotionPreview"/);
    assert.match(view, /id="promotionConfirm"[^>]*type="checkbox"/);
    assert.match(view, /for="promotionConfirm"/);
    assert.match(script, /state\.previewFingerprint !== fingerprint/);
    assert.match(script, /showPreview\(selected, roll, fingerprint\)/);
    assert.match(script, /if \(!el\('promotionConfirm'\)\.checked\)/);
    assert.match(script, /el\('promotionConfirm'\)\.focus\(\)/);
    assert.match(script, /\['promotionTarget', 'promotionRoll', 'promotionNote'\]/);
    assert.match(script, /addEventListener\('input', invalidatePreview\)/);
    assert.match(script, /addEventListener\('change', invalidatePreview\)/);
});

test('canonical progression idempotency and row versions remain intact', () => {
    assert.match(script, /request\.clientRequestId = state\.requestId/);
    assert.match(script, /studentRowVersion: state\.options\.studentRowVersion/);
    assert.match(script, /sourceEnrollmentRowVersion: state\.options\.sourceEnrollmentRowVersion/);
    assert.match(script, /state\.saving = true/);
    assert.match(script, /state\.options = null/);
    assert.match(script, /\[1, 2\]\.includes\(selected\.decision\)/);
    assert.match(script, /invalidatePreview\(\)/);
    assert.doesNotMatch(script, /localStorage|sessionStorage/);
});
