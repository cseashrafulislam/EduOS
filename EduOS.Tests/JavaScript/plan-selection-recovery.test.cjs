'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const script = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/account/plan-selection.js'), 'utf8');

function element() {
    return {
        children: [], dataset: {}, handlers: {}, attributes: {},
        classList: { add() {}, remove() {}, toggle() {} },
        append(...children) { this.children.push(...children); },
        replaceChildren(...children) { this.children = children; },
        addEventListener(type, handler) { this.handlers[type] = handler; },
        setAttribute(key, value) { this.attributes[key] = value; },
        querySelector(selector) {
            return selector === 'button[data-plan-id]'
                ? this.children.find(child => child.dataset?.planId) : null;
        },
        querySelectorAll(selector) {
            return selector === '.plan-selection-card' ? this.children : [];
        },
        focus() {}
    };
}

async function boot(fetch) {
    const plansGrid = element();
    const billingToggle = element();
    const continueBtn = element();
    const alert = element();
    billingToggle.querySelectorAll = () => [];
    const nodes = {
        planSelectionStrings: { textContent: JSON.stringify({
            createFailed: 'Subscription could not be created.',
            networkError: 'Network error.', selectPlanTemplate: 'Select {name}',
            selected: 'Selected', continueLabel: 'Continue',
            periods: { '1': 'month', '4': 'year' }
        }) },
        plansGrid, billingToggle, continueBtn, alertContainer: alert,
        planDashboardLink: element()
    };
    const document = {
        ready: null,
        getElementById: id => nodes[id] || null,
        addEventListener(type, handler) {
            if (type === 'DOMContentLoaded') this.ready = handler;
        },
        createElement: () => element(),
        querySelector: () => ({ content: 'csrf-token' })
    };
    const redirects = [];
    const window = { location: {
        origin: 'https://eduos.example',
        assign: url => redirects.push(url)
    } };
    let nextKey = 1;
    vm.runInNewContext(script, {
        document, window, fetch,
        crypto: { randomUUID: () => 'request-' + nextKey++ },
        console
    }, { filename: 'plan-selection.js' });
    await document.ready();
    return { plansGrid, billingToggle, continueBtn, alert, redirects };
}

const plan = (id, name) => ({
    id, name, monthlyPrice: 1500, yearlyPrice: 14000,
    currencyCode: 'BDT', maxStudents: 300, maxTeachers: 40,
    maxCampuses: 1, features: []
});
const reply = (status, payload) => ({
    ok: status >= 200 && status < 300, status,
    json: async () => payload
});

test('Existing 409 validation is shown and a committed subscription reaches payment', async () => {
    let postCount = 0;
    const posts = [];
    const fetch = async (url, options = {}) => {
        if (url === '/api/subscription/current')
            return reply(200, { success: true, data: null });
        if (url === '/api/subscription-plans')
            return reply(200, { success: true, data: [plan(10, 'Basic')] });
        if (url === '/api/subscription') {
            posts.push(JSON.parse(options.body));
            return ++postCount === 1
                ? reply(409, { success: false,
                    message: 'Verify institution email before starting subscription.' })
                : reply(200, { success: true, data: { state: 7 } });
        }
        if (url === '/api/subscription/invoices/unpaid')
            return reply(503, { success: false });
        throw new Error('Unexpected API route ' + url);
    };
    const ui = await boot(fetch);
    await ui.plansGrid.children[0].children[1].handlers.click();
    await ui.continueBtn.handlers.click();
    assert.match(ui.alert.textContent, /Verify institution email/);
    assert.equal(ui.redirects.length, 0);
    await ui.continueBtn.handlers.click();
    assert.equal(posts.length, 2);
    assert.equal(posts[0].clientRequestId, posts[1].clientRequestId);
    assert.equal(posts[0].billingCycleCode, 'Monthly');
    assert.equal(ui.redirects[0], '/Account/Payment');
});

test('Changing plan or billing cycle invalidates the old retry key', async () => {
    const posts = [];
    const fetch = async (url, options = {}) => {
        if (url === '/api/subscription/current')
            return reply(200, { success: true, data: null });
        if (url === '/api/subscription-plans')
            return reply(200, { success: true, data: [plan(10, 'Basic'), plan(11, 'Pro')] });
        if (url === '/api/subscription') {
            posts.push(JSON.parse(options.body));
            return reply(400, { success: false, message: 'Request rejected.' });
        }
        throw new Error('Unexpected API route ' + url);
    };
    const ui = await boot(fetch);
    await ui.plansGrid.children[0].children[1].handlers.click();
    await ui.continueBtn.handlers.click();
    assert.equal(ui.alert.textContent, 'Request rejected.');
    await ui.plansGrid.children[1].children[1].handlers.click();
    assert.equal(ui.alert.textContent, '');
    await ui.continueBtn.handlers.click();
    await ui.billingToggle.handlers.click({
        target: { closest: () => ({ dataset: { cycle: '4' } }) }
    });
    await ui.continueBtn.handlers.click();
    assert.equal(posts.length, 3);
    assert.notEqual(posts[0].clientRequestId, posts[1].clientRequestId);
    assert.notEqual(posts[1].clientRequestId, posts[2].clientRequestId);
    assert.equal(posts[2].billingCycleCode, 'Yearly');
});
