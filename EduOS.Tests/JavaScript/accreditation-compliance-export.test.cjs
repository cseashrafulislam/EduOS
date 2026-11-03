const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '..', '..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const js = read('EduOS.App/wwwroot/js/research/accreditation.js');
const view = read('EduOS.App/Views/Accreditation/Index.cshtml');
const api = read('EduOS.App/Controllers/Api/AccreditationApiController.cs');
const service = read('EduOS.Service/Services/Research/AccreditationService.cs');

test('as-of compliance evidence respects frozen publication versions and later withdrawal date', () => {
    assert.match(service, /p\.VersionNo = ev\.PublicationVersionNo/);
    assert.match(service, /p\.PublishedAt < \{3\}/);
    assert.match(service, /p\.State = 2 OR \(p\.State = 3 AND p\.UpdatedAt >= \{3\}\)/);
    assert.match(service, /ev\.CreatedAtUtc < \{3\} AND ev\.PublishedAtUtc < \{3\}/);
    assert.match(service, /a\.ApprovedAtUtc < \{3\}/);
});

test('gap CSV shares server-side scoped report and exports only aggregate audit fields', () => {
    assert.match(api, /HttpGet\("reports\/gaps\.csv"\)/);
    assert.match(api, /service\.ComplianceGapsAsync\(campusId, programId, asOf, renewWithinDays, ct\)/);
    assert.match(api, /X-EduOS-Session-Reference/);
    assert.match(api, /X-EduOS-Tenant-Id/);
    assert.match(api, /no-store, private/);
    assert.match(api, /=\+\-@\\t\\r\\n/);
    assert.doesNotMatch(api.slice(api.indexOf('public async Task<IActionResult> ComplianceGapsCsv')),
        /EvidenceSummary|DecisionReason|OriginalFileName|StorageKey/);
    assert.match(view, /id="accGapCsv"/);
    assert.match(js, /exportLink\.href = '\/api\/accreditation\/reports\/gaps\.csv\?'/);
    assert.match(js, /accGapCsv.*removeAttribute\('href'\)/);
});

function loadDownloadHarness({ alterAfterBlob = false, hasVerifier = true, differentHeader = false } = {}) {
    const start = js.indexOf('    async function downloadScopedCsv(');
    const end = js.indexOf('    for (const [id, file]', start);
    assert.ok(start >= 0 && end > start, 'real export helper must be found');
    const links = [];
    const state = { tenant: '23', session: 'S'.repeat(64), generation: 4, clears: 0, requests: [] };
    const context = {
        tenant: state.tenant, session: state.session, reportGeneration: state.generation,
        sessionValid: () => state.tenant === '23' && state.session === 'S'.repeat(64),
        clearReport: () => state.clears++, t: s => s,
        window: { EduOSVerifySessionContext: hasVerifier ? async () => {} : undefined },
        fetch: async url => {
            state.requests.push(url);
            return { ok: true, headers: { get: key => key === 'X-EduOS-Tenant-Id' ? '23' :
                key === 'X-EduOS-Session-Reference' ? differentHeader ? 'Z'.repeat(64) : 'S'.repeat(64) : null },
            blob: async () => {
                if (alterAfterBlob) state.session = 'changed';
                return { size: 23 };
            } };
        },
        URL: { createObjectURL: () => 'blob:checked', revokeObjectURL: () => {} },
        document: { createElement: () => ({ click: () => links.push('clicked'), remove() {} }),
            body: { appendChild() {} } },
        setTimeout: () => {}
    };
    vm.createContext(context);
    const download = vm.runInContext(`(${js.slice(start, end).trim()})`, context);
    return { download, state, links };
}

test('actual CSV fetch refuses a missing session verifier before sending data', async () => {
    const { download, state, links } = loadDownloadHarness({ hasVerifier: false });
    await assert.rejects(download('/api/accreditation/reports/gaps.csv', 'gaps.csv', 4));
    assert.equal(state.requests.length, 0);
    assert.equal(state.clears, 1);
    assert.deepEqual(links, []);
});

test('actual CSV fetch rejects wrong signed-in tenant/session response', async () => {
    const { download, state, links } = loadDownloadHarness({ differentHeader: true });
    await assert.rejects(download('/api/accreditation/reports/gaps.csv', 'gaps.csv', 4));
    assert.equal(state.requests.length, 1);
    assert.equal(state.clears, 1);
    assert.deepEqual(links, []);
});

test('actual CSV fetch discards old data after a session switch while blob is read', async () => {
    const { download, state, links } = loadDownloadHarness({ alterAfterBlob: true });
    await assert.rejects(download('/api/accreditation/reports/gaps.csv', 'gaps.csv', 4));
    assert.equal(state.clears, 1);
    assert.deepEqual(links, []);
});

test('actual CSV fetch downloads only when verified session and report generation remain current', async () => {
    const { download, links } = loadDownloadHarness();
    await download('/api/accreditation/reports/gaps.csv', 'gaps.csv', 4);
    assert.deepEqual(links, ['clicked']);
});
