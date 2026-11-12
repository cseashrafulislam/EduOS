'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const payment = read('EduOS.App/wwwroot/js/account/payment.js');
const view = read('EduOS.App/Views/Account/Payment.cshtml');
const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
const guard = read('EduOS.App/Middleware/OnboardingGuardMiddleware.cs');
const controller = read('EduOS.App/Controllers/Api/OnboardingController.cs');
const service = read('EduOS.Service/Services/Tenants/OnboardingService.cs');
const policy = read('EduOS.Core/Common/OnboardingWorkspacePolicy.cs');
const languageScript = read('EduOS.App/wwwroot/js/platform/language-selector.js');

function createNode(tag = 'div') {
    return {
        tag, children: [], listeners: {}, className: '', dataset: {},
        value: '', disabled: false, textContent: '', hidden: false,
        classList: { add() {}, remove() {} },
        addEventListener(type, handler) { this.listeners[type] = handler; },
        replaceChildren(...children) { this.children = [...children]; },
        append(...children) { this.children.push(...children); },
        removeAttribute() {}, setAttribute() {},
        focus() {}, querySelectorAll() { return []; }
    };
}
function harness(access = {}, approved = true) {
    const ids = ['paymentStrings', 'paymentStatus', 'invoiceDetails', 'paymentMethods',
        'manualSection', 'aamarpaySection', 'manualForm', 'payOnlineBtn',
        'depositSlipFile', 'alertContainer'];
    const nodes = new Map(ids.map(x => [x, createNode()]));
    nodes.get('paymentStrings').textContent = JSON.stringify({
        isBangla: false, culture: 'en-BD',
        paymentVerified: 'Payment verified; finish setup later.',
        openWorkspace: 'Open EduOS', nextSetup: 'Continue setup',
        activationFailed: 'Activation failed', noInvoice: 'Invoice missing'
    });
    const token = createNode('input');
    token.value = 'csrf-token';
    const document = {
        getElementById(id) { return nodes.get(id) || null; },
        createElement: createNode,
        createDocumentFragment: () => createNode(),
        querySelector: () => token,
        addEventListener(type, handler) { if (type === 'DOMContentLoaded') this.ready = handler; }
    };
    const calls = [], locations = [];
    async function fetch(url, options = {}) {
        calls.push({ url, options });
        if (url === '/api/onboarding/status')
            return { ok: true, status: 200, json: async () => ({
                success: true, data: { currentStage: access.currentStage || 4,
                    canEnterWorkspace: !!access.canEnter, hasWorkspaceAccess: !!access.hasAccess }
            }) };
        if (url === '/api/onboarding/enter-workspace')
            return { ok: approved, status: approved ? 200 : 409,
                json: async () => ({ success: approved,
                    message: approved ? 'Activated' : 'Subscription not active' }) };
        throw new Error('Unexpected URL: ' + url);
    }
    const window = {
        location: { search: '', assign: x => locations.push(x), reload() {} }
    };
    const context = { document, window, fetch, URLSearchParams, console, Intl, Date, Number };
    const source = payment.replace(/\}\)\(\);\s*$/, 'globalThis.__payment = { initialize };})();');
    assert.notEqual(source, payment);
    vm.runInNewContext(source, context);
    return { nodes, calls, locations, ready: () => document.ready(), approved };
}
test('approved subscription without invoice renders working Dashboard and Setup actions', async () => {
    const h = harness({ canEnter: true });
    await h.ready();
    const panel = h.nodes.get('paymentStatus');
    assert.equal(panel.children.length, 2);
    assert.match(panel.children[0].textContent, /Payment verified/);
    const [dashboard] = panel.children[1].children;
    await dashboard.listeners.click();
    assert.deepEqual(h.locations, ['/Dashboard/Index']);
    assert.equal(h.calls.filter(x => x.url === '/api/onboarding/enter-workspace').length, 1);
    const request = h.calls.find(x => x.url === '/api/onboarding/enter-workspace');
    assert.equal(request.options.method, 'POST');
    assert.equal(request.options.headers.RequestVerificationToken, 'csrf-token');
});
test('previously activated subscription enters ERP without another write', async () => {
    const h = harness({ hasAccess: true, currentStage: 6 });
    await h.ready();
    await h.nodes.get('paymentStatus').children[1].children[0].listeners.click();
    assert.deepEqual(h.locations, ['/Dashboard/Index']);
    assert.equal(h.calls.filter(x => x.options.method === 'POST').length, 0);
});
test('unverified or pending payment cannot display or invoke activation', async () => {
    const h = harness({});
    await h.ready();
    assert.equal(h.nodes.get('paymentStatus').children.length, 0);
    assert.equal(h.calls.filter(x => x.options.method === 'POST').length, 0);
    assert.deepEqual(h.locations, []);
});
test('rejected activation leaves the user on the payment page and shows a reason', async () => {
    const h = harness({ canEnter: true }, false);
    await h.ready();
    await h.nodes.get('paymentStatus').children[1].children[0].listeners.click();
    assert.deepEqual(h.locations, []);
    assert.equal(h.nodes.get('alertContainer').className, 'alert alert-danger');
});
test('server-only activation retains unfinished setup; suspended/expired subscriptions cannot enter', () => {
    assert.match(controller, /\[HttpPost\("enter-workspace"\)\]/);
    assert.match(service, /EnterWorkspaceAfterPaymentAsync/);
    assert.match(service, /OnboardingWorkspacePolicy.CanActivate/);
    assert.match(service, /tenant.OnboardingStage == OnboardingStage.Payment/);
    assert.doesNotMatch(service.slice(service.indexOf('EnterWorkspaceAfterPaymentAsync'),
        service.indexOf('public async Task<ApiResponse<bool>> AdvanceToStageAsync')), /OnboardingCompletedAt\s*=/);
    assert.match(policy, /subscription.State is SubscriptionState.Active or SubscriptionState.Trial/);
    assert.match(policy, /subscription.StartsAt <= utcNow && subscription.EndsAt > utcNow/);
    assert.match(guard, /OnboardingWorkspacePolicy.CanUse/);
    assert.match(view, /openWorkspace/);
});
test('language switch uses an independent CSRF-protected route during onboarding', () => {
    assert.match(layout, /form action="\/localization\/set-language" method="post"/);
    assert.match(layout, /@Html.AntiForgeryToken\(\)/);
    assert.match(layout, /js\/platform\/language-selector\.js/);
    assert.match(guard, /"\/localization\/set-language"/);
    assert.match(languageScript, /selector.addEventListener\('change'/);
    assert.match(languageScript, /form.requestSubmit\(\)/);
});
