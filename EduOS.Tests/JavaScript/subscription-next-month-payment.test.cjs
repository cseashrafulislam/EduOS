'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const paymentSource = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/account/payment.js'), 'utf8');
const billingSource = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/subscription-billing/index.js'), 'utf8');
const renewalSource = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.Service/Services/SaaS/SubscriptionService.Renewal.cs'), 'utf8');

function element() {
    return {
        children: [], handlers: {}, attributes: {}, disabled: false, hidden: false,
        classList: { add() {}, remove() {}, toggle() {} },
        append(...elements) { this.children.push(...elements); },
        replaceChildren(...elements) { this.children = elements; },
        addEventListener(name, fn) { this.handlers[name] = fn; },
        setAttribute(name, value) { this.attributes[name] = value; },
        removeAttribute(name) { delete this.attributes[name]; },
        querySelectorAll() { return []; },
        focus() {}
    };
}
async function openPayment(invoice, current) {
    const nodes = Object.fromEntries([
        'invoiceDetails', 'paymentMethods', 'manualSection', 'aamarpaySection',
        'payOnlineBtn', 'manualForm', 'depositSlipFile', 'onlineAmountLabel',
        'manualAmount', 'paymentStatus', 'alertContainer'
    ].map(id => [id, element()]));
    nodes.manualSection.hidden = true;
    nodes.aamarpaySection.hidden = true;
    nodes.paymentStrings = { textContent: JSON.stringify({
        invoiceTemplate: 'Invoice {number}', subscription: 'Subscription',
        totalDue: 'Total due', alreadyPaid: 'Invoice already paid',
        invoiceLoadFailed: 'Invoice is not payable',
        paymentVerified: 'Payment verified', processing: 'Processing',
        awaiting: 'Awaiting verification', reload: 'Reload'
    }) };
    const document = {
        ready: null, getElementById: id => nodes[id] || null,
        addEventListener(name, handler) { if (name === 'DOMContentLoaded') this.ready = handler; },
        createElement: () => element(), createDocumentFragment: () => element(),
        querySelector: () => null
    };
    const requests = [];
    const fetch = async url => {
        requests.push(url);
        if (url === '/api/subscription/invoices/77')
            return { ok: true, status: 200, json: async () => ({ success: true, data: invoice }) };
        if (url === '/api/subscription/current')
            return { ok: true, status: 200, json: async () => ({ success: true, data: current }) };
        if (url === '/api/subscription-payment/invoice/77')
            return { ok: true, status: 200, json: async () => ({ success: true, data: [] }) };
        if (url === '/api/onboarding/status')
            return { ok: true, status: 200, json: async () => ({
                success: true, data: { canEnterWorkspace: true, hasWorkspaceAccess: true }
            }) };
        throw Error('Unexpected request: ' + url);
    };
    const window = { location: {
        search: '?invoiceId=77',
        assign() {}, origin: 'https://eduos.example',
        reload() {}
    } };
    vm.runInNewContext(paymentSource, {
        document, window, fetch, URLSearchParams, URL, console
    }, { filename: 'payment.js' });
    await document.ready();
    return { nodes, requests };
}

function invoice(state, dueAmount) {
    return {
        id: 77, tenantSubscriptionId: 402, invoiceNumber: 'REN-TEST',
        subtotal: 1500, taxAmount: 0, totalAmount: 1500,
        paidAmount: 1500 - dueAmount, dueAmount, state,
        currencyCode: 'BDT', lines: [{ description: 'Basic / Monthly' }]
    };
}

test('A renewal invoice stays payable although the prior subscription is active', async () => {
    const { nodes, requests } = await openPayment(invoice(2, 1500), {
        id: 401, state: 2
    });
    assert.ok(requests.includes('/api/subscription/invoices/77'));
    assert.ok(requests.includes('/api/subscription-payment/invoice/77'));
    assert.ok(!requests.includes('/api/onboarding/status'),
        'the active older subscription must not suppress renewal payment');
    assert.equal(nodes.paymentStatus.className, undefined);
    assert.equal(nodes.manualAmount.value, '1500.00');
    assert.match(nodes.onlineAmountLabel.textContent, /1,500/);
});

test('A paid renewal invoice cannot be collected again based on the older active subscription', async () => {
    const { nodes, requests } = await openPayment(invoice(4, 0), {
        id: 401, state: 2
    });
    assert.ok(requests.includes('/api/subscription/current'));
    assert.ok(!requests.includes('/api/onboarding/status'));
    assert.equal(nodes.paymentStatus.className, 'payment-status-panel pending');
    assert.ok(nodes.paymentStatus.children.some(x => x.textContent === 'Invoice already paid'));
});

test('Paid original invoice still offers verified onboarding workspace access', async () => {
    const { nodes, requests } = await openPayment({
        ...invoice(4, 0), tenantSubscriptionId: 401
    }, { id: 401, state: 2 });
    assert.ok(requests.includes('/api/onboarding/status'));
    assert.equal(nodes.paymentStatus.className, 'payment-status-panel success');
});

test('Monthly advance renewal retains server-side one-successor guard', () => {
    assert.match(renewalSource, /\(\(automatic \|\| previous\.BillingCycleCode != "Monthly"\)/);
    assert.match(renewalSource, /x\.PreviousSubscriptionId == previous\.Id/);
    assert.match(renewalSource, /IsolationLevel\.Serializable/);
    assert.match(billingSource, /s\.billingCycleCode!=="Monthly"/);
    assert.match(billingSource, /i\.tenantSubscriptionId\)===Number\(u\.id\)/);
    assert.doesNotMatch(billingSource, /\.innerHTML/);
});
