const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const script = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/assessment/mark-reviews.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/AssessmentAdministration/MarkReviews.cshtml'), 'utf8');
const layout = fs.readFileSync(path.join(root, 'EduOS.App/Views/Shared/_Layout.cshtml'), 'utf8');
const { guideFor } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');

test('marks review uses authorized scoped selector, rowversion and anti-forgery', () => {
    assert.match(script, /scopes\/page/);
    assert.match(script, /assessmentRowVersion: review\.assessmentRowVersion/);
    assert.match(script, /RequestVerificationToken: token/);
    assert.match(script, /clientRequestId: request\.clientRequestId/);
    assert.match(script, /encodeURIComponent\(id\)/);
});

test('lost acknowledgement preserves the same review key and frozen decision payload', () => {
    assert.match(script, /state\.pendingRequest = request/);
    assert.match(script, /request\.assessmentRowVersion/);
    assert.match(script, /request\.reason \|\| null/);
    assert.match(script, /error\.transportFailure = true/);
    assert.match(script, /error\.transportFailure = response\.ok/);
    assert.match(script, /if \(error\.transportFailure\) \{/);
    assert.match(script, /Retry this decision with the same request key/);
    assert.match(script, /state\.pendingRequest = null; notice\('Review decision recorded\.'\)/);
});
test('review screen has contextual help and safe history rendering', () => {
    assert.doesNotMatch(view, /aria-label="Marks review help"/);
    assert.match(layout, /id="pageContextHelp"/);
    assert.match(guideFor('/AssessmentAdministration/MarkReviews', 'en-BD').rules, /Submitter and reviewer must differ/);
    assert.match(script, /td\.textContent =/);
    assert.match(script, /replaceChildren\(\)/);
    assert.match(script, /history\.items/);
    assert.match(script, /\/history\?/);
    assert.match(view, /markReviewHistoryPrev/);
    assert.match(view, /markReviewHistoryNext/);
});


test('history is keyset-paged, filtered and exports only the authorized page', () => {
    assert.match(script, /history\.cursors\[history\.page\]/);
    assert.match(script, /params\.set\('beforeId'/);
    assert.match(script, /params\.set\('decision'/);
    assert.match(script, /history\.items\.map\(/);
    assert.match(script, /history\.sequence/);
    assert.match(view, /CSV \(current page\)/);
});

test('finalized publications disable ordinary review actions without hiding history', () => {
    assert.match(script, /review\.isFinalized/);
    assert.match(script, /approved amendment required/);
    assert.match(script, /review\?\.canSubmit/);
    assert.match(script, /review\?\.canReview/);
});

test('a lost POST acknowledgement replays its original request key and payload', async () => {
    const elements = new Map();
    const element = id => {
        if (!elements.has(id)) {
            elements.set(id, {
                id, value: '', textContent: '', disabled: false, children: [], options: [],
                handlers: {},
                addEventListener(name, callback) { this.handlers[name] = callback; },
                replaceChildren(...children) {
                    this.children = children;
                    if (this.id === 'markReviewSubject') this.options = children;
                },
                append(...children) { this.children.push(...children); },
                appendChild(child) { this.children.push(child); },
                add(option) { this.options.push(option); },
                focus() {}
            });
        }
        return elements.get(id);
    };
    const rootElement = element('markReviewWorkspace');
    rootElement.dataset = { canReview: 'false' };
    rootElement.querySelector = () => ({ value: 'csrf-token' });
    const document = {
        getElementById: element,
        createElement: tag => ({ tag, children: [], textContent: '',
            append(...children) { this.children.push(...children); },
            appendChild(child) { this.children.push(child); } })
    };
    const payloads = [];
    let confirms = 0;
    let keys = 0;
    const success = data => ({ ok: true, json: async () => ({ success: true, data }) });
    const fetch = async (url, options) => {
        if (url.includes('/scopes/page?')) return success({ totalCount: 1, items: [{
            assessmentName: 'Exam', academicBatchName: 'Batch', subjectName: 'Math', assessmentSubjectId: 42
        }] });
        if (url.includes('/history?')) return success({ items: [], hasMore: false });
        if (url.endsWith('/decision')) {
            payloads.push(JSON.parse(options.body));
            if (payloads.length === 1) throw new Error('Server committed but response was lost');
            return success({ canSubmit: false, assessmentRowVersion: 'version-2',
                currentDecision: 1, recentHistory: [], historyTotal: 1 });
        }
        if (url.endsWith('/42')) return success({ canSubmit: true,
            assessmentRowVersion: 'version-1', currentDecision: null, recentHistory: [] });
        throw new Error('Unexpected HTTP route: ' + url);
    };
    vm.runInNewContext(script, { document, fetch, URLSearchParams,
        Option: class { constructor(text, value) { this.text = text; this.value = value; } },
        window: { confirm: () => { confirms++; return true; } },
        crypto: { randomUUID: () => 'key-' + ++keys } });
    const settle = async () => { for (let i = 0; i < 8; i++) await new Promise(setImmediate); };
    await settle();
    element('markReviewSubject').value = '42';
    element('markReviewSubject').handlers.change();
    await settle();
    element('markReviewSubmit').handlers.click();
    await settle();
    assert.equal(payloads.length, 1);
    assert.equal(element('markReviewSubmit').disabled, false);
    assert.match(element('markReviewNotice').textContent, /same request key/);
    element('markReviewSubmit').handlers.click();
    await settle();
    assert.equal(payloads.length, 2);
    assert.deepEqual(payloads[0], payloads[1]);
    assert.equal(payloads[0].clientRequestId, 'key-1');
    assert.equal(payloads[0].assessmentRowVersion, 'version-1');
    assert.equal(confirms, 1, 'replay should not reconfirm a committed decision');
    assert.equal(keys, 1, 'replay must not allocate a new idempotency key');
    assert.match(element('markReviewNotice').textContent, /Review decision recorded/);
});
