'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const script = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/wwwroot/js/alumni/career-opportunities.js'), 'utf8');

function harness(admin) {
    class Element {
        constructor(tag = 'DIV') {
            this.tagName = tag;
            this.value = '';
            this.disabled = false;
            this.children = [];
            this.listeners = {};
            this.dataset = {};
            this.classList = { add() {}, remove() {} };
            this.textContent = '';
        }
        addEventListener(name, callback) { this.listeners[name] = callback; }
        appendChild(child) {
            this.children.push(child);
            if (this.tagName === 'SELECT' && this.children.length === 1)
                this.value = child.value;
            return child;
        }
        replaceChildren(...children) {
            this.children = children;
            if (this.tagName === 'SELECT') this.value = '';
        }
        setAttribute() {}
        removeAttribute() {}
        querySelectorAll() { return []; }
        reportValidity() { return true; }
    }
    const names = [
        'careerEmployer', 'careerOpportunitySave', 'careerEmployerSearch',
        'careerOpportunityForm', 'careerOpportunityTitle',
        'careerOpportunityDescription', 'careerOpportunityKind',
        'careerOpportunityDeadline', 'careerOpportunityStatus',
        'careerOpportunityRows', 'careerOpportunityRefresh',
        'careerOpportunityPrev', 'careerOpportunityNext',
        'careerOpportunityPage', 'careerOpportunityNotice',
        'careerMyRows', 'careerMyPage', 'careerMyPrev', 'careerMyNext',
        'careerMyRefresh'
    ];
    const elements = Object.fromEntries(names.map(name => [name, new Element()]));
    elements.careerEmployer.tagName = 'SELECT';
    elements.careerOpportunityKind.value = '1';
    elements.careerOpportunityStatus.value = '0';
    elements.careerOpportunityTitle.value = 'Synthetic internship';
    elements.careerOpportunityDeadline.value = '2026-11-30T17:00';
    elements.careerOpportunityForm.querySelectorAll = () => [
        elements.careerEmployer, elements.careerOpportunityTitle,
        elements.careerOpportunityDescription, elements.careerOpportunityKind,
        elements.careerOpportunityDeadline, elements.careerOpportunitySave
    ];
    const root = new Element();
    root.dataset.admin = String(admin);
    root.querySelectorAll = () => [];
    root.querySelector = () => ({ value: 'synthetic-csrf' });
    const reference = 'A'.repeat(64), tenant = '12', shield = { hidden: true };
    const document = {
        body: { dataset: { authSessionReference: reference, authTenantId: tenant } },
        documentElement: { lang: 'en-BD' },
        getElementById: id => id === 'careerOpportunityWorkspace' ? root :
            id === 'eduosSessionShield' ? shield : elements[id],
        createElement: tag => new Element(tag.toUpperCase())
    };
    const window = {
        EduOSSessionTransitionPending: () => false,
        EduOSVerifySessionContext: async () => {
            shield.hidden = document.body.dataset.authTenantId === tenant;
        }
    };
    const requests = [], timers = [];
    const fetch = (url, options) => new Promise(resolve => {
        requests.push({ url, options, resolve });
    });
    let sequence = 0;
    vm.runInNewContext(script, {
        document, window, fetch, crypto: { randomUUID: () => 'synthetic-' + ++sequence },
        setTimeout: callback => { timers.push(callback); return timers.length; },
        clearTimeout() {}, confirm: () => true
    });
    const respond = (request, data, status = 200, responseReference = reference, responseTenant = tenant) =>
        request.resolve({
            ok: status < 400, status,
            headers: { get(name) {
                return name === 'X-EduOS-Session-Reference' ? responseReference :
                    name === 'X-EduOS-Tenant-Id' ? responseTenant : null;
            } },
            json: async () => status < 400
                ? { success: true, data } : { success: false, message: 'Synthetic error' }
        });
    const settle = async () => {
        for (let i = 0; i < 40; i++) await Promise.resolve();
    };
    return { elements, requests, timers, respond, settle, document, shield };
}

test('Career employer search invalidates stale options and guards draft submission', async () => {
    const h = harness(true), e = h.elements;
    const initialEmployer = h.requests.find(r => r.url.includes('/employers?'));
    const initialList = h.requests.find(r => r.url.includes('/opportunities?'));
    assert.ok(initialEmployer && initialList);
    e.careerEmployerSearch.value = 'new employer';
    e.careerEmployerSearch.listeners.input();
    assert.equal(e.careerOpportunitySave.disabled, true);
    assert.equal(e.careerEmployer.children.length, 0);
    h.respond(initialEmployer, { items: [{ reference: 'old', name: 'Old', campusId: 1 }] });
    h.respond(initialList, { items: [], totalCount: 0 });
    await h.settle();
    assert.equal(e.careerEmployer.children.length, 0, 'stale search must not repopulate select');
    e.careerOpportunityForm.listeners.submit({
        preventDefault() {}, currentTarget: e.careerOpportunityForm
    });
    assert.equal(h.requests.filter(r => r.options.method === 'POST').length, 0,
        'Enter-submit must not bypass pending search');
    h.timers.shift()();
    const updated = h.requests.find(r => r.url.includes('/employers?') && r !== initialEmployer);
    assert.ok(updated);
    h.respond(updated, { items: [{ reference: 'new', name: 'New', campusId: 1 }] });
    await h.settle();
    assert.equal(e.careerEmployer.value, 'new');
    assert.equal(e.careerOpportunitySave.disabled, false);
    const submit = () => e.careerOpportunityForm.listeners.submit({
        preventDefault() {}, currentTarget: e.careerOpportunityForm
    });
    submit();
    submit();
    // Writes now revalidate the live cookie session asynchronously before fetch.
    await h.settle();
    const posts = h.requests.filter(r => r.options.method === 'POST');
    assert.equal(posts.length, 1, 'double-submit must create one request');
    assert.equal(JSON.parse(posts[0].options.body).employerReference, 'new');
    assert.equal(e.careerOpportunityTitle.disabled, true,
        'form must remain frozen while request is in flight');
    h.respond(posts[0], {});
    await h.settle();
    const reload = h.requests.filter(r => r.url.includes('/opportunities?')).at(-1);
    if (reload !== initialList) h.respond(reload, { items: [], totalCount: 0 });
    await h.settle();
    assert.equal(e.careerOpportunityTitle.disabled, false);
    assert.equal(e.careerOpportunitySave.disabled, false);
});

test('Student opportunity list stays read-only when identity lookup fails', async () => {
    const h = harness(false);
    const identity = h.requests.find(r => r.url.includes('/students/me'));
    const applications = h.requests.find(r => r.url.includes('/applications?'));
    assert.ok(identity && applications);
    h.respond(identity, null, 503);
    h.respond(applications, { items: [], totalCount: 0 });
    await h.settle();
    const opportunities = h.requests.find(r => r.url.includes('/opportunities?'));
    assert.ok(opportunities, 'read-only opportunities must still load');
    h.respond(opportunities, { items: [{
        reference: 'synthetic', title: 'Synthetic role', employerName: 'Example',
        kind: 1, status: 2, applyUntilUtc: '2026-11-30T00:00:00Z',
        canApply: true, campusId: 1
    }], totalCount: 1 });
    await h.settle();
    const row = h.elements.careerOpportunityRows.children[0];
    assert.ok(row);
    assert.equal(row.children[5].children.length, 0,
        'no Apply action without verified student identity');
});
test('Career rejects stale successful JSON before parsing private student identities', async () => {
    const h = harness(false);
    const identity = h.requests.find(r => r.url.includes('/students/me'));
    const applications = h.requests.find(r => r.url.includes('/applications?'));
    h.respond(identity, [{ studentReference: 'private-student', campusId: 1 }],
        200, 'B'.repeat(64), '99');
    h.respond(applications, { items: [], totalCount: 0 });
    await h.settle();
    assert.equal(h.elements.careerOpportunityRows.children.length, 0);
    assert.equal(h.elements.careerMyRows.children.length, 0);
    assert.equal(h.elements.careerOpportunityRefresh.disabled, true);
    assert.match(h.elements.careerOpportunityNotice.textContent, /session changed/i);
    assert.equal(h.requests.some(r => r.url.includes('/opportunities?')), false);
});

test('Career invalidates in-flight identity after an institution switch', async () => {
    const h = harness(false);
    const identity = h.requests.find(r => r.url.includes('/students/me'));
    const applications = h.requests.find(r => r.url.includes('/applications?'));
    h.document.body.dataset.authTenantId = '99';
    h.respond(identity, [{ studentReference: 'old-tenant', campusId: 1 }]);
    h.respond(applications, { items: [], totalCount: 0 });
    await h.settle();
    assert.equal(h.elements.careerOpportunityRows.children.length, 0);
    assert.equal(h.elements.careerOpportunityRefresh.disabled, true);
    assert.equal(h.requests.some(r => r.url.includes('/opportunities?')), false);
});
