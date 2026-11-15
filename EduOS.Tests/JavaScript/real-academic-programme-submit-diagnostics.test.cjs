'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
test('Real academic Programme QA checks native validity and observes actual POST before persistence', () => {
    const script = fs.readFileSync(path.resolve(__dirname,
        '../../tests/browser/real-academic-foundation.cjs'), 'utf8');
    assert(script.includes('form.checkValidity()'));
    assert(script.includes('invalidFields'));
    assert(script.includes('submitOwnsForm'));
    assert(script.includes('sameSession'));
    assert(script.includes('formSubmitObserved'));
    assert(script.includes('programmePostDispatched'));
    assert(script.includes('programmePostFailed'));
    assert(script.includes('[QA request] Programme POST dispatched'));
    assert(script.includes("'/api/academic-setup/programmes'"));
    assert(script.includes('await page.locator'));
    assert(script.includes('Programme POST did not return a response within 12s'));
    assert(script.includes("const program = await waitForCatalogItem(page, 'programs', programName);"));
});
