const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const { guideFor, coverage } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');
const sidebar = require('../../EduOS.App/wwwroot/js/sidebar-navigation.js');

const routes = {
    HostelOperations: ['Applications', 'Allocations', 'Setup', 'Reports'],
    TransportOperations: ['Setup', 'Assignments', 'Reports'],
    InventoryMovements: ['Transactions', 'Adjustments', 'StockCounts', 'Reports']
};

test('focused optional-domain workspaces retain trusted Index view, module gates and role-checked GETs', () => {
    for (const [controllerName, actions] of Object.entries(routes)) {
        const controller = read(`EduOS.App/Controllers/${controllerName}Controller.cs`);
        const view = read(`EduOS.App/Views/${controllerName}/Index.cshtml`);
        assert.match(controller, /public IActionResult Index\(\) => View\(\);/);
        assert.match(controller, /TaskPage\(/);
        assert.match(controller, /return View\("Index"\);/);
        assert.match(view, /data-task-page="@taskPage"/);
        for (const action of actions) {
            assert.match(controller, new RegExp(`public IActionResult ${action}\\(\\)`));
            assert.match(controller, new RegExp(`public IActionResult ${action}\\(\\)[^;]*?TaskPage\\(`));
        }
        const module = controllerName === 'HostelOperations' ? 'HOSTEL' : controllerName === 'TransportOperations' ? 'TRANSPORT' : 'INVENTORY';
        assert.ok(controller.includes(`RequireModule("${module}")`));
    }
    assert.match(read('EduOS.App/Controllers/HostelOperationsController.cs'), /Authorize\(Roles = "TenantAdmin,Principal,HostelWarden"\)/);
    assert.match(read('EduOS.App/Controllers/TransportOperationsController.cs'), /Authorize\(Roles = "TenantAdmin,Principal,TransportManager"\)/);
    assert.match(read('EduOS.App/Controllers/InventoryMovementsController.cs'), /Authorize\(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper"\)/);
});

test('focused operation routes are server-rendered and script-gated, while legacy Index preserves all panels', () => {
    const css = read('EduOS.App/wwwroot/css/site.css');
    assert.doesNotMatch(css, /#hostelWorkspace\[data-task-page="applications"\] > :not/);
    assert.doesNotMatch(css, /#transportWorkspace\[data-task-page="reports"\] > :not/);
    assert.doesNotMatch(css, /#inventoryMovements\[data-task-page="counts"\] > \[data-inventory-area\]/);
    const hostel = read('EduOS.App/Views/HostelOperations/Index.cshtml');
    const transport = read('EduOS.App/Views/TransportOperations/Index.cshtml');
    const inventory = read('EduOS.App/Views/InventoryMovements/Index.cshtml');
    for (const scope of ['Applications', 'Allocations', 'Setup', 'Reports']) {
        assert.match(hostel, new RegExp(`var show${scope} = taskPage is "all" or "${scope.toLowerCase()}"`));
    }
    for (const scope of ['Setup', 'Assignments', 'Reports']) {
        assert.match(transport, new RegExp(`var show${scope} = taskPage is "all" or "${scope.toLowerCase()}"`));
    }
    for (const [scope, url] of [['Transactions', 'transactions'], ['Adjustments', 'adjustments'], ['Counts', 'counts'], ['Reports', 'reports']]) {
        assert.match(inventory, new RegExp(`var show${scope} = taskPage is "all" or "${url}"`));
    }
    for (const id of ['hostelAllocationForm', 'hostelActiveRows', 'hostelRoomCreatePanel', 'hostelResidentRosterPanel']) {
        const inPartial = id === 'hostelResidentRosterPanel';
        assert.ok((inPartial ? read('EduOS.App/Views/HostelOperations/_ResidentRoster.cshtml') : hostel).includes(`id="${id}"`));
    }
    for (const id of ['transportAssignmentForm', 'transportRouteSetupPanel', 'transportActivePanel']) assert.ok(transport.includes(`id="${id}"`));
    for (const group of ['transactions', 'adjustments', 'counts', 'reports']) assert.ok(inventory.includes(`data-inventory-area="${group}"`));
    assert.match(inventory, /js\/inventory\/movements\.js/);
    assert.match(hostel, /js\/hostel-operations\/index\.js/);
    assert.match(transport, /js\/transport-operations\/index\.js/);
    assert.match(hostel, /@if \(canManage && showReports\)\s*\{\s*<script src="~\/js\/hostel-operations\/capacity\.js"/);
    assert.match(transport, /@if \(canManage && showAssignments\)\s*\{[\s\S]*?<script src="~\/js\/transport-operations\/vehicle-cases\.js"/);
    assert.match(transport, /@if \(canManage && showReports\)\s*\{[\s\S]*?<script src="~\/js\/transport-operations\/vehicle-safety-impact\.js"/);
    assert.match(transport, /@if \(User\.IsInRole\("TenantAdmin"\) && showReports\)/);
    assert.match(transport, /@if \(canManage && taskPage == "all"\)[\s\S]*?id="transportCatalogPanel"/);
    assert.match(inventory, /@if \(showTransactions\)[\s\S]*?PartialAsync\("_Ledger"\)/);
    assert.match(transport, /@if \(canManage && showAssignments\)[\s\S]*?id="transportAssignmentPanel"/);
    const transportScript = read('EduOS.App/wwwroot/js/transport-operations/index.js');
    assert.match(transportScript, /if \(el\('transportRouteList'\)\) renderList\('transportRouteList'/);
    assert.match(transportScript, /if \(el\('transportVehicleList'\)\) renderList\('transportVehicleList'/);
    assert.match(transportScript, /if \(el\('transportMine'\)\) loadMine\(\)/);
});

test('inventory entry script initializes only scoped modules, including read-only and legacy modes', () => {
    const source = read('EduOS.App/wwwroot/js/inventory/movements.js')
        .split('\n').filter(line => !line.startsWith('import ')).join('\n');
    const functions = {
        initLedger: 'ledger', initReservations: 'reservations', initReservationCoverage: 'coverage',
        initBalance: 'balance', initReorder: 'reorder', initStockPosition: 'position',
        initItemHistory: 'history', initLotAging: 'aging', initPhysicalCounts: 'counts',
        initForm: 'form', initAdjustments: 'adjustments'
    };
    const scenarios = [
        ['transactions', true, ['ledger', 'balance', 'form']],
        ['transactions', false, ['ledger', 'balance']],
        ['adjustments', true, ['adjustments']],
        ['adjustments', false, []],
        ['counts', false, ['counts']],
        ['reports', false, ['reservations', 'coverage', 'reorder', 'position', 'history', 'aging']],
        ['all', true, ['ledger', 'balance', 'reservations', 'coverage', 'reorder', 'position', 'history', 'aging', 'counts', 'form', 'adjustments']]
    ];
    for (const [scope, create, expected] of scenarios) {
        const calls = [];
        const context = { root: { dataset: { taskPage: scope, create: String(create) } }, notice() {} };
        for (const [fn, label] of Object.entries(functions)) {
            context[fn] = (...args) => {
                calls.push(label);
                if (label === 'ledger') return async () => {};
                if (label === 'counts') assert.equal(typeof args[0], 'function');
                return Promise.resolve();
            };
        }
        vm.runInNewContext(source, context, { filename: 'inventory/movements.js' });
        assert.deepEqual(calls, expected, `${scope} (create=${create})`);
    }
});

test('authorized sidebar organizes optional module destinations under correct 3rd-level type', () => {
    const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
    for (const [type, actions] of Object.entries(routes)) {
        assert.ok(layout.includes(`"/${type}/" +`), type);
        for (const action of actions) assert.ok(layout.includes(`Action: "${action}"`));
    }
    assert.equal(sidebar.categoryFor('management', 'campus', '/HostelOperations/Setup'), 'settings');
    assert.equal(sidebar.categoryFor('management', 'campus', '/HostelOperations/Reports'), 'reports');
    assert.equal(sidebar.categoryFor('management', 'campus', '/TransportOperations/Assignments'), 'operations');
    assert.equal(sidebar.categoryFor('management', 'inventory', '/InventoryMovements/StockCounts'), 'operations');
    assert.equal(sidebar.categoryFor('management', 'inventory', '/InventoryMovements/Reports'), 'reports');
    assert.equal(sidebar.categoryFor('management', 'library', '/Library/Branches'), 'settings');
    assert.equal(sidebar.categoryFor('management', 'library', '/Library/Overdue'), 'reports');
    assert.equal(sidebar.categoryFor('academics', 'learning', '/Learning/CourseContent'), 'settings');
    assert.equal(sidebar.categoryFor('academics', 'learning', '/Learning/Certificates'), 'reports');
    assert.equal(sidebar.navigationLabelFor('/Library/Liabilities', 'bn-BD'), 'গ্রন্থাগারের দায়');
});

test('all new operation, library and LMS manuals are distinct validated English/Bangla guides', () => {
    const all = [
        ...Object.entries(routes).flatMap(([prefix, actions]) => actions.map(action => `/${prefix}/${action}`)),
        ...['Branches', 'Reservations', 'Overdue', 'Conditions', 'Integrity', 'Liabilities'].map(action => `/Library/${action}`),
        ...['Quizzes', 'LiveClasses', 'CourseContent', 'AssignmentFiles', 'Certificates'].map(action => `/Learning/${action}`)
    ];
    assert.deepEqual(coverage(), { routes: 131, en: 131, bn: 131 });
    for (const route of all) {
        const en = guideFor(route, 'en-BD');
        const bn = guideFor(route, 'bn-BD');
        assert.ok(en && bn, `${route}: missing manual`);
        assert.ok(en.steps.length >= 2, route);
        assert.equal(en.steps.length, bn.steps.length);
        assert.notEqual(en.title, bn.title);
        assert.notEqual(en.example, bn.example);
        assert.deepEqual(en.related.map(link => link.path), bn.related.map(link => link.path));
    }
});

test('theme/onboarding remain the one protected implementation and mobile/accessibility rules are scoped', () => {
    const workspace = read('EduOS.App/wwwroot/js/platform/workspace-preferences.js');
    const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
    const css = read('EduOS.App/wwwroot/css/site.css');
    assert.match(workspace, /data-custom-color-picker/);
    assert.match(layout, /id="pageContextHelp"/);
    assert.match(css, /#hostelWorkspace :is\(button, a, input, select, textarea\):focus-visible/);
    assert.match(css, /@media \(max-width: 575\.98px\)/);
    assert.match(read('EduOS.App/Views/Account/_OnboardingLayout.cshtml'), /onboarding-phase/);
});

test('Bangla static task-label enhancement leaves values, child controls and English untouched', () => {
    const script = read('EduOS.App/wwwroot/js/platform/task-workspace-locales.js');
    const makeNode = (text, children = []) => ({
        textContent: text, children, attrs: {},
        getAttribute(name) { return this.attrs[name] ?? null; },
        setAttribute(name, value) { this.attrs[name] = value; }
    });
    const label = makeNode('Find student');
    const button = makeNode('Save');
    const controlLabel = makeNode('Status', [makeNode('checkbox')]);
    const input = makeNode(''); input.attrs.placeholder = 'Search';
    const rootElement = { querySelectorAll(selector) {
        return selector.includes('placeholder') ? [input] : [label, button, controlLabel];
    } };
    const run = lang => vm.runInNewContext(script, { document: {
        documentElement: { lang }, querySelector: () => rootElement
    } });
    run('en-BD');
    assert.equal(label.textContent, 'Find student');
    run('bn-BD');
    assert.equal(label.textContent, 'শিক্ষার্থী খুঁজুন');
    assert.equal(button.textContent, 'সংরক্ষণ');
    assert.equal(controlLabel.textContent, 'Status');
    assert.equal(input.attrs.placeholder, 'খুঁজুন');
    assert.equal(controlLabel.children.length, 1);
});

test('academic and accounting task locale remains bounded to the actual routed workspace', () => {
    const script = read('EduOS.App/wwwroot/js/platform/task-workspace-locales.js');
    for (const [path, rootId, heading, bangla] of [
        ['AcademicRoutines', 'routineApp', 'Class timetable', 'ক্লাস রুটিন'],
        ['AcademicInstruction', 'instructionApp', 'Lesson plans', 'পাঠ পরিকল্পনা'],
        ['AccountingReports', 'institutionAccountingReports', 'Trial Balance', 'ট্রায়াল ব্যালেন্স']
    ]) {
        const view = read(`EduOS.App/Views/${path}/Index.cshtml`);
        assert.match(view, new RegExp(`id="${rootId}"`));
        assert.match(view, /task-workspace-locales\.js/);
        const element = {
            textContent: heading,
            children: [],
            getAttribute: () => null,
            setAttribute: () => { throw new Error('unexpected mutation'); }
        };
        const record = { value: 'Tenant-owned name', textContent: 'Tenant-owned name' };
        const workspace = { querySelectorAll: selector => selector.includes('placeholder') ? [] : [element] };
        const run = lang => vm.runInNewContext(script, {
            document: {
                documentElement: { lang },
                querySelector: selector => selector.includes(`#${rootId}`) ? workspace : null
            }
        });
        run('en-BD');
        assert.equal(element.textContent, heading);
        run('bn-BD');
        assert.equal(element.textContent, bangla);
        assert.equal(record.value, 'Tenant-owned name');
        assert.equal(record.textContent, 'Tenant-owned name');
    }
});

test('semantic-color readable foreground meets WCAG AA contrast on arbitrary valid picks', () => {
    const shell = read('EduOS.App/wwwroot/js/site.js');
    const match = shell.match(/    function readableWorkspaceText\(hex\) \{[\s\S]*?\n    \}/);
    assert.ok(match);
    const readable = vm.runInNewContext(`(${match[0].trim()})`);
    const luminance = hex => {
        const values = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
            .map(x => x <= .04045 ? x / 12.92 : ((x + .055) / 1.055) ** 2.4);
        return .2126 * values[0] + .7152 * values[1] + .0722 * values[2];
    };
    for (const bg of ['#000000', '#FFFFFF', '#777777', '#0F172A', '#FF0000', '#00FF00', '#0000FF',
        '#2563EB', '#F6ED3A', '#123456', '#AABBCC', '#E4E4E4', '#805D74', '#27CD99']) {
        const textColor = readable(bg);
        const b = luminance(bg);
        const t = luminance(textColor);
        const ratio = (Math.max(b, t) + .05) / (Math.min(b, t) + .05);
        assert.ok(ratio >= 4.5, `${bg}: ${ratio.toFixed(2)}:1 contrast`);
    }
});
