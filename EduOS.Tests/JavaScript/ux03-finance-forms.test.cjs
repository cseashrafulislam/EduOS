'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/finance/fees.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/FeeOperations/Index.cshtml'), 'utf8');

test('each Finance form loads the shared guard before the existing fee implementation', () => {
    assert.match(view, /platform\/form-guard\.js"[\s\S]*finance\/fees\.js"/);
    for (const id of ['feeStructureForm', 'feeStructureCloseForm', 'feePaymentForm'])
        assert.ok(source.includes(`globalThis.EduOSFormGuard?.create(e('${id}'))`));
});

test('editing and resetting a fee structure cannot silently discard unsaved lines', () => {
    assert.match(source, /feeStructureReset'\)\.addEventListener\('click',[\s\S]*structureGuard\.confirmDiscard\(\)/);
    assert.match(source, /async function loadStructure[\s\S]*structureGuard\.confirmDiscard\(\)/);
    assert.match(source, /previousDraft!==globalThis\.EduOSFormGuard\?\.fingerprint/);
    assert.match(source, /function resetStructureForm\(\)[\s\S]*structureGuard\?\.markClean\(\)/);
    assert.match(source, /state\.savingStructure=true;[\s\S]*feeStructureForm'\)\.inert=true/);
    assert.match(source, /state\.structureRefreshFailed/);
});

test('closing a billing structure keeps any separate unsaved editor intact', () => {
    const handler = source.slice(source.indexOf("if(e('feeStructureCloseForm'))"), source.indexOf('// One scoped entry view'));
    assert.match(handler, /state\.closingStructure\|\|!ev\.currentTarget\.reportValidity\(\)/);
    assert.match(handler, /closureGuard\.confirmDiscard\(\)/);
    assert.match(handler, /state\.closingStructure=true/);
    assert.match(handler, /await register\(\)/);
    assert.doesNotMatch(handler, /await options\(\)/, 'options() replaces unsaved fee-line inputs');
    assert.match(handler, /Future billing was closed, but the register did not refresh/, 'acknowledged closure must not be reported as a failed write');
});

test('cash payment draft protects amount and invoice selection while server verifies collection', () => {
    assert.match(view, /id="feePaymentCancel"/);
    assert.match(source, /paymentGuard&&!paymentGuard\.confirmDiscard\(\)\)return;[\s\S]*state\.invoice=x/);
    assert.match(source, /feeStudentSearch'\)\.value=lastSearchValue;return/);
    assert.match(source, /feeStudent'\)\.value=state\.student\|\|'';return/);
    assert.match(source, /paymentBusy=true;[\s\S]*feePaymentForm'\)\.inert=true/);
    assert.match(source, /payments\/preflight/);
    assert.match(source, /preview\.invoices\[0\]\.invoiceReference!==invoice\.reference/);
    assert.match(source, /clientRequestId=stable\('payment',JSON\.stringify\(body\)\)/);
    assert.match(source, /feePaymentCancel'\)\?\.addEventListener\('click',[\s\S]*paymentGuard\.confirmDiscard\(\)/);
});

test('invoice generation still requires current preview, confirmation and single-flight state', () => {
    assert.match(source, /state\.invoicePreviewKey!==JSON\.stringify\(body\)/);
    assert.match(source, /if\(!confirm\(t\('Generate monthly invoices/);
    assert.match(source, /state\.billingBusy=true;[\s\S]*feeInvoiceForm'\)\.inert=true/);
    assert.match(source, /finally\{state\.billingBusy=false;e\('feeInvoiceForm'\)\.inert=false;\}/);
    assert.match(source, /for\(const id of \['feeBatch','feeInvoiceStructure','feeMonth','feeBillingYear','feeDueDate'\]\)/);
});
