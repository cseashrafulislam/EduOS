const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

function browser(payload) {
    const nodes = {};
    const element = (tag = 'div') => ({ tag, children: [], handlers: {}, textContent: '', value: '',
        className: '', disabled: false, href: '',
        appendChild(child) { this.children.push(child); return child; },
        replaceChildren(...children) { this.children = children; },
        addEventListener(name, handler) { this.handlers[name] = handler; },
        fire(name) { this.handlers[name]?.({ preventDefault() {} }); }
    });
    for (const id of ['centralWorkCenter', 'cwcAlert', 'cwcFilters', 'cwcCampus', 'cwcRows',
        'cwcPrevious', 'cwcNext', 'cwcPage']) nodes[id] = element();
    const calls = [];
    const document = { documentElement: { lang: 'en-BD' },
        getElementById: id => nodes[id], createElement: element };
    const window = { addEventListener() {} };
    vm.runInNewContext(read('EduOS.App/wwwroot/js/platform/central-work-center.js'), {
        document, window, AbortController, URLSearchParams, Date, Number, String, Object,
        fetch: async (url, options) => {
            calls.push({ url: String(url), options });
            return { ok: true, json: async () => ({ success: true, data: payload }) };
        }
    }, { timeout: 1000 });
    return { nodes, calls };
}

test('Central Work Center uses server paging and text-only rendering for untrusted references', async () => {
    const b = browser({ inventoryIncluded: true, facilitiesIncluded: true,
        pending: { totalCount: 1, page: 1, pageSize: 25, items: [{
            source: 'ProcurementRequisition', sourceId: 1,
            reference: '<img src=x onerror=alert(1)>', campusId: 10,
            state: 'Submitted', submittedAtUtc: '2026-11-01T09:00:00Z',
            workspaceUrl: '/ProcurementRequisitions/Index'
        }] } });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(b.calls.length, 1);
    assert.match(b.calls[0].url, /page=1/);
    assert.match(b.calls[0].url, /pageSize=25/);
    assert.equal(b.calls[0].options.credentials, 'same-origin');
    assert.equal(b.calls[0].options.cache, 'no-store');
    const row = b.nodes.cwcRows.children[0];
    assert.equal(row.children[1].textContent, '<img src=x onerror=alert(1)>');
    assert.equal(row.children[5].children[0].href, '/ProcurementRequisitions/Index?workId=1');
    assert.equal(b.nodes.cwcNext.disabled, true);
    b.nodes.cwcCampus.value = '10';
    b.nodes.cwcFilters.fire('submit');
    await new Promise(resolve => setImmediate(resolve));
    assert.match(b.calls[1].url, /campusId=10/);
});

test('Work Center refuses an arbitrary server-supplied navigation URL', async () => {
    const b = browser({ pending: { totalCount: 1, page: 1, pageSize: 25,
        items: [{ source: 'FacilityVisitorPolicy', reference: 'RetentionDays', campusId: 10,
            state: 'Submitted', submittedAtUtc: '2026-11-01T09:00:00Z',
            workspaceUrl: '//evil.example' }] } });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(b.nodes.cwcRows.children[0].children[5].children.length, 0);
});

test('Finance approval opens only a validated journal reference and labels institution scope', async () => {
    const reference = '4da4b719-cb40-4436-8f54-cb2e5536bd70';
    const item = { source: 'AccountingJournal', sourceId: 101, reference: 'JV-101',
        campusId: null, state: 'Submitted', sourceReference: reference,
        submittedAtUtc: '2026-11-05T10:01:00Z', workspaceUrl: '/AccountingJournals/Index' };
    const b = browser({ pending: { totalCount: 1, page: 1, pageSize: 25, items: [item] } });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(b.nodes.cwcRows.children[0].children[2].textContent, 'Institution-wide');
    assert.equal(b.nodes.cwcRows.children[0].children[5].children[0].href,
        '/AccountingJournals/Index?reference=' + reference);
    const untrusted = browser({ pending: { totalCount: 1, page: 1, pageSize: 25,
        items: [{ ...item, sourceReference: '../bad?x=1' }] } });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(untrusted.nodes.cwcRows.children[0].children[5].children.length, 0);
});

test('Cross-module API and shared page manual keep domain approvals and entitlement checks', () => {
    const service = read('EduOS.Service/Services/Platform/CentralWorkCenterService.cs');
    const api = read('EduOS.App/Controllers/Api/CentralWorkCenterApiController.cs');
    const view = read('EduOS.App/Views/CentralWorkCenter/Index.cshtml');
    const { guideFor } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');
    assert.match(service, /IsCurrentTenantModuleAvailableAsync\("INVENTORY"\)/);
    assert.match(service, /IsCurrentTenantModuleAvailableAsync\("FACILITIES"\)/);
    assert.match(service, /IsCurrentTenantModuleAvailableAsync\("FINANCE"\)/);
    assert.match(service, /j\.CreatedBy != user\.UserId/);
    assert.match(service, /j\.CampusId == null/);
    assert.match(service, /JournalState\.Submitted/);
    assert.match(service, /RequestedByUserId != user\.UserId/);
    assert.match(service, /CreatedBy != user\.UserId/);
    assert.match(service, /visibleCampuses\.Contains/);
    assert.equal((service.match(/!campusId\.HasValue \|\| [ropx]\.CampusId == campusId\.Value/g) || []).length, 6,
        "All three queue sources and all three detail rechecks must honor the selected campus");
    assert.match(service, /db\.CurrentTenantId != user\.TenantId/);
    assert.doesNotMatch(service, /SaveChangesAsync|ExecuteUpdateAsync|ExecuteDeleteAsync/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin,Principal,InventoryManager"\)/);
    assert.match(api, /EnableRateLimiting\("ApiPolicy"\)/);
    assert.match(view, /central-work-center\.js/);
    for (const lang of ['en-BD', 'bn-BD']) {
        const manual = guideFor('/CentralWorkCenter/Index', lang);
        assert.ok(manual?.steps?.length >= 3 && manual.rules && manual.recovery);
    }
});
