'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const catalog = read('EduOS.Service/Services/Tenants/SmartSetupCatalog.cs');
const service = read('EduOS.Service/Services/Tenants/SmartSetupService.cs');
const view = read('EduOS.App/Views/SmartSetup/Index.cshtml');
const client = read('EduOS.App/wwwroot/js/tenant/smart-setup.js');

test('Bangladesh board and UGC grade schemes are complete independent nondefault presets', () => {
    assert.match(catalog, /"BD-GPA-5"[\s\S]*?new\(80m, 100m, "A\+", 5m\)/);
    assert.match(catalog, /"BD-CGPA-4"[\s\S]*?new\(75m, 79\.99m, "A", 3\.75m\)/);
    assert.match(catalog, /new\(0m, 32\.99m, "F", 0m\)/);
    assert.match(catalog, /new\(0m, 39\.99m, "F", 0m\)/);
    assert.match(service, /IsDefault = false, IsActive = true/);
    assert.match(service, /UseCreditWeightedGpa = seed\.Code == "BD-CGPA-4"/);
    assert.match(service, /db\.GradeRules\.Add\(new GradeRule/);
    assert.match(service, /scheme\.Id/);
    assert.match(service, /actual\.Length == expected!\.Length/);
});

test('Subject, GL and payroll presets use the existing authoritative tables', () => {
    assert.match(catalog, /if \(bangladesh && !university && !training\)/);
    assert.match(catalog, /\("ICT", "Information and Communication Technology"\)/);
    assert.match(catalog, /if \(type\.Contains\("MADRAS"\)\)/);
    assert.match(catalog, /if \(bangladesh\)[\s\S]*?list\.Add\(new\("FINANCE", "Account"/);
    assert.match(catalog, /if \(bangladesh\)[\s\S]*?list\.Add\(new\("PAYROLL", "SalaryComponent"/);
    assert.match(service, /db\.Subjects\.Add\(new Subject/);
    assert.match(service, /db\.Accounts\.Add\(new Account/);
    assert.match(service, /db\.SalaryComponents\.Add\(new SalaryComponent/);
    assert.match(service, /ParentAccountId = parentId/);
    assert.match(service, /AccountType = seed\.AccountType\.Value/);
    assert.match(service, /IsCashInHand = false/);
    assert.match(service, /Type = seed\.SalaryType\.Value, IsTaxable = seed\.IsTaxable/);
    assert.match(service, /!seed\.IsGroup/);
    assert.match(service, /await db\.SaveChangesAsync\(ct\)/);
    assert.doesNotMatch(service, /db\.JournalLines\.Add|db\.SalaryStructures\.Add|db\.CurriculumSubjects\.Add/);
});

test('New modules keep confirmed tenant-scoped idempotent transactional apply', () => {
    assert.match(catalog, /Version = "2026\.11\.3"/);
    for (const code of ['ACADEMIC', 'ASSESSMENT', 'FINANCE', 'PAYROLL'])
        assert.match(catalog, new RegExp('new\\("' + code + '"[^\\n]*'));
    assert.match(service, /request\.Modules\.Count is < 1 or > 10/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /await LockTenantAsync\(token\)/);
    assert.match(service, /rows\.Any\(r => r\.Module == name/);
    assert.match(service, /request\.ClientRequestId\.ToString\("D"\)/);
    assert.match(service, /AddCanonicalMasterAsync\(candidate, seed, now, token\)/);
    assert.match(service, /new AuditLog/);
    assert.match(service, /Existing master data conflicts|Selected row conflicts/);
});

test('Smart Setup UI exposes new master kinds and removes requested verbose warning', () => {
    new Function(client);
    for (const label of ['Subject:', 'GradeScheme:', 'Account:', 'SalaryComponent:'])
        assert.ok(client.includes(label), label);
    assert.doesNotMatch(view, /Safe setup:|Select and save reviewed basic master records here/);
    assert.doesNotMatch(view, /role="note"/);
    assert.match(view, /id="smartSetupForm"/);
    assert.match(view, /id="smartSetupConfirm"/);
    assert.match(view, /smart-setup\.js/);
});
