const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('member access history export uses the authorized bounded tenant-scoped service', () => {
    const service = read('EduOS.Service/Services/Auth/TenantUserAdministrationService.AccessHistory.cs');
    const api = read('EduOS.App/Controllers/Api/TenantUsersController.cs');
    const contract = read('EduOS.Core/Interfaces/IServices/ITenantUserAdministrationService.cs');
    assert.match(service, /if \(!CanManage\) return Denied<string>\(\)/);
    assert.match(service, /x\.TenantId == TenantId/);
    assert.match(service, /x\.EntityName == nameof\(TenantMembership\)/);
    assert.match(service, /\.Take\(1001\)/);
    assert.match(service, /rows\.Count > 1000/);
    assert.match(service, /HistoryCsvCell\(row\.OldValue\)/);
    assert.match(service, /UserPermissions\.IgnoreQueryFilters\(\)/);
    assert.match(service, /permission\.TenantId == TenantId/);
    assert.match(service, /permission\.UserId == memberUserId/);
    assert.match(service, /ValidHistoryChange\(change\)/);
    assert.match(contract, /ExportMemberAccessHistoryCsvAsync/);
    assert.match(api, /HttpGet\("\{membershipId:long\}\/access-history\/export"\)/);
    assert.match(api, /ExportMemberAccessHistoryCsvAsync/);
    assert.match(api, /"text\/csv"/);
    assert.match(api, /"nosniff"/);
});
test('member access history export is selected-member-bound and safely rendered', () => {
    const view = read('EduOS.App/Views/TenantUsers/Index.cshtml');
    const js = read('EduOS.App/wwwroot/js/tenant-users/index.js');
    assert.match(view, /id="memberHistoryExport"/);
    assert.match(js, /state\.selected\?\.membershipId !== memberId/);
    assert.match(js, /access-history\/export/);
    assert.match(js, /credentials: 'same-origin', cache: 'no-store'/);
    assert.match(js, /URL\.revokeObjectURL/);
    assert.match(js, /textNode\('td', value/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});

test('permission exception lineage includes legacy rows and scoped filter in read and export', () => {
    const service = read('EduOS.Service/Services/Auth/TenantUserAdministrationService.AccessHistory.cs');
    const mutation = read('EduOS.Service/Services/Auth/TenantUserAdministrationService.PermissionOverrides.cs');
    const api = read('EduOS.App/Controllers/Api/TenantUsersController.cs');
    const js = read('EduOS.App/wwwroot/js/tenant-users/index.js');
    const view = read('EduOS.App/Views/TenantUsers/Index.cshtml');
    assert.match(mutation, /EntityName = nameof\(TenantMembership\), EntityId = membershipId/);
    assert.match(mutation, /PermissionCode = permission\.Code/);
    assert.match(service, /"permissions" => history\.Where/);
    assert.match(service, /x\.EntityName == nameof\(UserPermission\)/);
    assert.match(api, /\[FromQuery\] string\? change = null/);
    assert.match(js, /memberHistoryChangeFilter/);
    assert.match(js, /encodeURIComponent\(change\)/);
    assert.match(js, /TenantMemberPermissionOverrideChanged/);
    assert.match(view, /value="permissions"/);
});

test('member access history UTC date filters are identical for page and export', () => {
    const service = read('EduOS.Service/Services/Auth/TenantUserAdministrationService.AccessHistory.cs');
    const api = read('EduOS.App/Controllers/Api/TenantUsersController.cs');
    const js = read('EduOS.App/wwwroot/js/tenant-users/index.js');
    const view = read('EduOS.App/Views/TenantUsers/Index.cshtml');
    assert.match(service, /ValidHistoryRange\(from, to\)/);
    assert.match(service, /OccurredAt >= startUtc && x\.OccurredAt < endExclusiveUtc/);
    assert.match(api, /\[FromQuery\] DateOnly\? from = null/);
    assert.match(js, /function historyRange\(\)/);
    assert.match(js, /encodeURIComponent\(from\).*encodeURIComponent\(to\)/);
    assert.match(view, /id="memberHistoryFrom"/);
    assert.match(view, /id="memberHistoryTo"/);
});
