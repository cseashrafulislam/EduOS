const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const preview = require('../../EduOS.App/wwwroot/js/platform/ai-voice-attendance-preview.js');

test('campus search is same-origin and bounded with no transcript disclosure', async () => {
    const calls = [];
    const fetcher = async (url, options) => {
        calls.push({ url, options });
        return { ok: true, json: async () => ({ campuses: [{ id: 12, name: 'Main', code: 'M' }], hasMore: false }) };
    };
    const result = await preview.findCampuses(fetcher, 'Savar & Dhaka');
    assert.equal(result.campuses[0].id, 12);
    assert.equal(calls.length, 1);
    assert.equal(calls[0].url, '/api/ai/voice/campuses?search=Savar%20%26%20Dhaka');
    assert.equal(calls[0].options.method, 'GET');
    assert.equal(calls[0].options.credentials, 'same-origin');
    assert.equal(calls[0].options.cache, 'no-store');
    assert.equal(JSON.stringify(calls[0]).includes('How many'), false);
    await assert.rejects(preview.findCampuses(fetcher, 'x'.repeat(81)), /Invalid campus search/);
    assert.equal(calls.length, 1);
});

test('question leaves browser only on explicit preview with antiforgery and verified campus', async () => {
    const calls = [];
    const fetcher = async (url, options) => {
        calls.push({ url, options });
        return { ok: true, json: async () => ({ decision: 'Ready', reportExecuted: false,
            campus: { id: 7, name: 'South' }, date: '2026-11-09', attendanceStatus: 'Present' }) };
    };
    const question = 'How many present attendance today?';
    const result = await preview.previewAttendance(fetcher, question, '7', 'csrf-valid');
    assert.equal(result.reportExecuted, false);
    assert.equal(calls.length, 1);
    assert.equal(calls[0].url, '/api/ai/voice/attendance/preview');
    assert.equal(calls[0].options.method, 'POST');
    assert.equal(calls[0].options.headers.RequestVerificationToken, 'csrf-valid');
    assert.deepEqual(JSON.parse(calls[0].options.body), { question, campusId: 7 });
    assert.equal(calls[0].options.credentials, 'same-origin');
    await assert.rejects(preview.previewAttendance(fetcher, question, 0, 'csrf-valid'), /Enter a question/);
    await assert.rejects(preview.previewAttendance(fetcher, question, 1, ''), /Enter a question/);
    await assert.rejects(preview.previewAttendance(fetcher, question.repeat(20), 1, 'token'), /Enter a question/);
    await assert.rejects(preview.previewAttendance(fetcher, question, '9007199254740993', 'token'), /Enter a question/);
    assert.equal(calls.length, 1);
});

test('unauthorized attendance preview fails without a fallback provider', async () => {
    const fetcher = async () => ({ ok: false, status: 403, json: async () => ({ message: 'internal' }) });
    await assert.rejects(preview.previewAttendance(fetcher,
        'How many absent attendance yesterday?', 1, 'csrf'), /Access denied/);
});

test('shared dialog only sends the reviewed question after explicit click, and cancels stale sessions', async () => {
    function node() {
        const listeners = new Map();
        return {
            value: '', disabled: false, open: false, children: [], textContent: '',
            addEventListener(type, fn) { listeners.set(type, fn); },
            fire(type) { return listeners.get(type)?.({ preventDefault() {} }); },
            click() { return this.fire('click'); },
            append(child) { this.children.push(child); if (this.tag === 'select' && !this.value) this.value = child.value; },
            replaceChildren(...children) { this.children = children; if (this.tag === 'select') this.value = ''; }
        };
    }
    const ids = Object.fromEntries(['pageAssistantDialog', 'pageAssistantAiPanel',
        'pageAssistantQuery', 'pageAssistantCampusSearch', 'pageAssistantCampusFind',
        'pageAssistantCampusSelect', 'pageAssistantAiPreview', 'pageAssistantAiResult']
        .map(id => [id, node()]));
    ids.pageAssistantCampusSelect.tag = 'select';
    ids.pageAssistantCampusSelect.children.push({ value: '', textContent: 'Find a campus first' });
    ids.pageAssistantDialog.open = true;
    const doc = {
        documentElement: { lang: 'en-BD' },
        body: { dataset: { authTenantId: '7', authSessionReference: 's1' } },
        getElementById: id => ids[id],
        querySelector: () => ({ content: 'csrf' }),
        createElement: () => node()
    };
    const calls = [];
    const host = {
        addEventListener() {},
        fetch: async (url, options) => {
            calls.push({ url, options });
            return { ok: true, json: async () => url.endsWith('/campuses')
                ? { campuses: [{ id: 6, name: 'Central', code: 'C' }], hasMore: false }
                : { decision: 'Ready', reportExecuted: false, date: '2026-11-09',
                    campus: { id: 6, name: 'Central' }, attendanceStatus: 'Present' } };
        }
    };
    preview.mount(doc, host);
    ids.pageAssistantQuery.value = 'How many present attendance today?';
    assert.equal(calls.length, 0, 'the server is never called during mount or typing');
    ids.pageAssistantAiPanel.open = true;
    ids.pageAssistantAiPanel.fire('toggle');
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(calls.length, 1, 'opening the panel fetches only authorized campuses');
    assert.equal(calls[0].options.method, 'GET');
    assert.equal(ids.pageAssistantAiPreview.disabled, false);
    await ids.pageAssistantAiPreview.click();
    assert.equal(calls.length, 2);
    assert.equal(calls[1].options.method, 'POST');
    assert.match(ids.pageAssistantAiResult.children[0].textContent, /No count was run/);
    assert.equal(ids.pageAssistantAiResult.children[1].href, '/Attendance/Reports');

    ids.pageAssistantCampusFind.click();
    doc.body.dataset.authSessionReference = 'rotated';
    const previousCount = calls.length;
    await ids.pageAssistantAiPreview.click();
    assert.equal(calls.length, previousCount, 'no preview is issued after session rotation');
    ids.pageAssistantDialog.open = false;
    ids.pageAssistantDialog.fire('close');
    assert.equal(ids.pageAssistantAiResult.children.length, 0);
});

test('voice preview endpoint enforces existing tenant, module, permission, campus and timezone', () => {
    const source = fs.readFileSync(path.resolve(__dirname,
        '../../EduOS.App/Controllers/Api/AiVoiceAttendanceController.cs'), 'utf8');
    const layout = fs.readFileSync(path.resolve(__dirname,
        '../../EduOS.App/Views/Shared/_Layout.cshtml'), 'utf8');
    const frontend = fs.readFileSync(path.resolve(__dirname,
        '../../EduOS.App/wwwroot/js/platform/ai-voice-attendance-preview.js'), 'utf8');
    assert.match(source, /\[RequireModule\("ATTENDANCE"\)\]/);
    assert.match(source, /\[RequirePermission\(PermissionConstants\.Attendance\.View\)\]/);
    assert.match(source, /\[AutoValidateAntiforgeryToken\]/);
    assert.match(source, /\[EnableRateLimiting\("ApiPolicy"\)\]/);
    assert.match(source, /\[ResponseCache\(NoStore = true/);
    assert.match(source, /db\.CurrentTenantId == current\.TenantId/);
    assert.match(source, /a\.UserId == current\.UserId/);
    assert.match(source, /x\.CampusId == campus\.Id && x\.IsActive/);
    assert.match(source, /TimeZoneInfo\.ConvertTime\(clock\.GetUtcNow\(\), zone\)/);
    assert.match(source, /ReviewedAttendanceQueryCompiler\.Compile/);
    assert.doesNotMatch(source, /\.SaveChanges|\.ExecuteUpdate|\.ExecuteDelete|AuthorizedAttendanceReadExecutor|\.ReadAsync\(/);
    assert.match(layout, /id="pageAssistantAiPanel"/);
    assert.match(layout, /js\/platform\/ai-voice-attendance-preview\.js/);
    assert.doesNotMatch(frontend, /fetch\(['"]https?:\/\//);
    assert.doesNotMatch(frontend, /localStorage|sessionStorage|SpeechRecognition|\.submit\(|\.requestSubmit\(/);
});
