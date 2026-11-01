const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('workspace appearance is a connected authorized user/tenant feature', () => {
    const controller = read('EduOS.App/Controllers/WorkspacePreferencesController.cs');
    const service = read('EduOS.Service/Services/Tenants/WorkspacePreferencesService.cs');
    const admin = read('EduOS.Service/Services/Tenants/TenantSettingService.cs');
    const view = read('EduOS.App/Views/WorkspacePreferences/Index.cshtml');
    const script = read('EduOS.App/wwwroot/js/platform/workspace-preferences.js');
    const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
    assert.match(controller, /\[Authorize\]/);
    assert.match(controller, /\[AutoValidateAntiforgeryToken\]/);
    assert.match(controller, /\/api\/workspace-preferences/);
    assert.match(service, /_user\.TenantId/);
    assert.match(service, /_user\.UserId/);
    assert.match(service, /RowVersion/);
    assert.match(service, /SaveChangesAsync/);
    assert.match(admin, /WorkspacePreferencesService\.KeyPrefix/);
    assert.match(view, /workspacePreferencesForm/);
    assert.match(script, /credentials: 'same-origin'/);
    assert.match(script, /rowVersion/);
    assert.doesNotMatch(script, /\.innerHTML/);
    assert.match(layout, /\/workspace/);
});

test('shell applies only whitelisted server preferences without cross-tenant browser storage', () => {
    const site = read('EduOS.App/wwwroot/js/site.js');
    assert.match(site, /EduOSApplyWorkspacePreferences/);
    assert.match(site, /\/api\/workspace-preferences/);
    assert.doesNotMatch(site, /(?:window\.)?localStorage\s*[.\[]/);
    assert.doesNotMatch(site, /(?:window\.)?sessionStorage\s*[.\[]/);
    const css = read('EduOS.App/wwwroot/css/site.css');
    assert.match(css, /data-workspace-navigation="right"/);
    assert.match(css, /data-workspace-navigation="top"/);
    assert.match(css, /data-workspace-theme="dark"/);
});

test('dashboard panel arrangement stays private, allowlisted and never hides mandatory content', () => {
    const service = read('EduOS.Service/Services/Tenants/WorkspacePreferencesService.cs');
    const dto = read('EduOS.Core/DTOs/SaaS/WorkspacePreferencesDtos.cs');
    const view = read('EduOS.App/Views/Dashboard/Index.cshtml');
    const appearance = read('EduOS.App/Views/WorkspacePreferences/Index.cshtml');
    const script = read('EduOS.App/wwwroot/js/platform/dashboard-panel-preferences.js');
    const shell = read('EduOS.App/wwwroot/js/site.js');
    assert.match(service, /ValidPanels\(request\.DashboardPanels\)/);
    assert.match(service, /Distinct\(StringComparer\.Ordinal\)/);
    assert.match(dto, /DashboardPanelPreferenceDto/);
    assert.match(appearance, /workspaceDashboardPanels/);
    assert.match(view, /data-dashboard-panels/);
    assert.match(view, /data-dashboard-panel="capacity"/);
    assert.match(view, /data-dashboard-panel="quick-actions"/);
    assert.match(view, /data-dashboard-panel="setup-checklist"/);
    assert.match(view, /dashboard-account-status/);
    assert.match(view, /dashboard-panel-preferences\.js/);
    assert.match(script, /eduos:workspace-preferences/);
    assert.match(shell, /EduOSWorkspacePreferences/);
    assert.doesNotMatch(script, /localStorage|sessionStorage|innerHTML/);
});

test('institution administration table columns use private versioned allowlists and live safe DOM updates', () => {
    const dto = read('EduOS.Core/DTOs/SaaS/WorkspacePreferencesDtos.cs');
    const service = read('EduOS.Service/Services/Tenants/WorkspacePreferencesService.cs');
    const view = read('EduOS.App/Views/WorkspacePreferences/Index.cshtml');
    const shell = read('EduOS.App/wwwroot/js/site.js');
    const adapter = read('EduOS.App/wwwroot/js/platform/table-column-preferences.js');
    const users = read('EduOS.App/Views/TenantUsers/Index.cshtml');
    const invitations = read('EduOS.App/Views/TenantUsers/Invitations.cshtml');
    assert.match(dto, /TablePreferenceDto/);
    assert.match(service, /ValidTables\(request\.TablePreferences\)/);
    assert.match(service, /TablePreferences = tables\.Select/);
    assert.match(service, /request\.TablePreferences == null \? currentTables/);
    assert.match(service, /MergeTables\(currentTables, request\.TablePreferences\)/);
    assert.match(view, /User\.IsInRole\("TenantAdmin"\)/);
    assert.match(view, /data-table-columns/);
    assert.match(shell, /safeTablePreferences/);
    assert.match(adapter, /MutationObserver/);
    assert.match(adapter, /eduos:workspace-preferences/);
    assert.doesNotMatch(adapter, /innerHTML|localStorage|sessionStorage/);
    assert.match(users, /data-workspace-table="institution-users"/);
    assert.match(invitations, /data-workspace-table="institution-invitations"/);
    const featureAudit = read('EduOS.App/Views/TenantFeatureAudit/Index.cshtml');
    const activityAudit = read('EduOS.App/Views/AuditLogs/Index.cshtml');
    assert.match(featureAudit, /data-workspace-table="institution-feature-audit"/);
    assert.match(activityAudit, /data-workspace-table="institution-activity-audit"/);
    const management = read('EduOS.App/Views/InstitutionManagement/Index.cshtml');
    assert.match(management, /data-workspace-table="institution-campuses"/);
    assert.match(management, /data-workspace-table="institution-change-history"/);
    assert.match(management, /table-column-preferences/);
    const settings = read('EduOS.App/Views/TenantSettings/Index.cshtml');
    const exceptions = read('EduOS.App/Views/TenantPermissionOverrides/Index.cshtml');
    const exceptionJs = read('EduOS.App/wwwroot/js/tenant-users/permission-overrides.js');
    assert.match(settings, /data-workspace-table="institution-settings"/);
    assert.match(exceptions, /data-workspace-table="institution-permission-exceptions"/);
    assert.match(settings, /data-table-column="protection"/);
    assert.match(exceptions, /data-table-column="expires"/);
    assert.match(exceptionJs, /node\.dataset\.tableColumn = column/);
    assert.match(exceptionJs, /cell\('td', '', null, 'action'\)/);
    assert.match(settings, /table-column-preferences\.js/);
    assert.match(exceptions, /table-column-preferences\.js/);
    assert.match(view, /__RequestVerificationToken|AntiForgeryToken/);
    assert.match(read('EduOS.App/wwwroot/js/platform/workspace-preferences.js'), /RequestVerificationToken/);
});

test('advanced appearance preserves tenant-scoped versioning and allowlisted color surfaces', () => {
 const dto=read('EduOS.Core/DTOs/SaaS/WorkspacePreferencesDtos.cs');
 const svc=read('EduOS.Service/Services/Tenants/WorkspacePreferencesService.cs');
 const shell=read('EduOS.App/wwwroot/js/site.js');
 const editor=read('EduOS.App/wwwroot/js/platform/workspace-preferences.js');
 const view=read('EduOS.App/Views/WorkspacePreferences/Index.cshtml');
 const css=read('EduOS.App/wwwroot/css/site.css');
 assert.match(dto,/CustomColors/);
 assert.match(svc,/ValidColors\(request\.CustomColors\)/);
  assert.match(svc,/var previous = ToDto\(row\)/);
  assert.match(svc,/request\.CustomColors \?\? previous\.CustomColors/);
 assert.match(shell,/EduOSNormalizeWorkspaceColor/);
 assert.match(shell,/html\.style\.removeProperty\(bg\)/);
 assert.match(editor,/data-custom-color-text/);
 assert.match(view,/workspaceAdvancedColors/);
 for(const token of ['sidebar','header','page','card','button','form','table']){
   assert.match(view,new RegExp('Key: "'+token+'"'));
   assert.match(css,new RegExp('data-workspace-custom-'+token+'="true"'));
 }
 assert.doesNotMatch(editor,/\.innerHTML|localStorage|sessionStorage/);
});

test('appearance preview can be discarded, warns before leaving and never saves after failed load', () => {
    const vm = require('node:vm');
    const editor = read('EduOS.App/wwwroot/js/platform/workspace-preferences.js');
    const view = read('EduOS.App/Views/WorkspacePreferences/Index.cshtml');
    const guide = read('EduOS.App/wwwroot/js/platform/page-context-help.js');
    assert.match(view, /id="workspaceDiscard" disabled/);
    assert.match(editor, /discard\.addEventListener\('click'/);
    assert.match(editor, /fill\(persisted\);\s*preview\(\);\s*message\('Unsaved preview discarded/);
    assert.match(editor, /window\.addEventListener\('beforeunload'/);
    assert.match(editor, /if \(!dirty \|\| sessionInvalidated\) return/);
    assert.match(editor, /const disabled = value \|\| sessionInvalidated \|\| !loaded/);
    assert.match(editor, /savedSignature = JSON\.stringify\(values\(\)\)/);
    assert.match(editor, /updateMoveButtons\(\);\s*preview\(\)/);
    assert.match(editor, /updateTableButtons\(\);\s*preview\(\)/);
    assert.match(editor, /window\.EduOSApplyWorkspacePreferences\?\.\(defaults\)/);
    assert.match(guide, /Discard preview to restore the last saved design/);
    assert.match(guide, /প্রিভিউ বাতিল/);
    const fn = editor.match(/    function isUnsavedPreview\(current, signature\) \{[\s\S]*?\n    \}/);
    assert.ok(fn, 'same saved-state comparison must be executable');
    const changed = vm.runInNewContext('(' + fn[0].trim() + ')');
    const original = { theme: 'light', customColors: { sidebar: '#123456' },
        dashboardPanels: [{ id: 'capacity', visible: true }, { id: 'quick-actions', visible: true }] };
    const signature = JSON.stringify(original);
    assert.equal(changed(original, signature), false);
    assert.equal(changed({ ...original, theme: 'dark' }, signature), true);
    assert.equal(changed({ ...original, customColors: { sidebar: '#654321' } }, signature), true);
    assert.equal(changed({ ...original, dashboardPanels: [...original.dashboardPanels].reverse() }, signature), true);
    assert.equal(changed(null, signature), true);
});


test('appearance revision restore uses same tenant session transaction and bounded private history', () => {
    const controller = read('EduOS.App/Controllers/WorkspacePreferencesController.cs');
    const service = read('EduOS.Service/Services/Tenants/WorkspacePreferencesService.cs');
    const view = read('EduOS.App/Views/WorkspacePreferences/Index.cshtml');
    const script = read('EduOS.App/wwwroot/js/platform/workspace-preferences.js');
    const guide = read('EduOS.App/wwwroot/js/platform/page-context-help.js');
    assert.match(controller, /HttpPost\("\/api\/workspace-preferences\/restore"\)/);
    assert.match(controller, /WriteInCurrentSessionAsync\(ct => service\.RestoreAsync/);
    assert.match(service, /ReadHistory\(ReadStored\(row\)\)/);
    assert.match(service, /MatchesVersion\(row\.RowVersion, request\.RowVersion\)/);
    assert.match(service, /history\.TakeLast\(5\)/);
    assert.match(service, /Snapshot = Snapshot\(previous\)/);
    assert.match(view, /id="workspaceHistoryList"/);
    assert.match(script, /renderHistory\(persisted\.history\)/);
    assert.match(script, /commit\('\/api\/workspace-preferences\/restore'/);
    assert.match(script, /if \(!button \|\| busy \|\| sessionInvalidated \|\| !loaded \|\| dirty\) return;/);
    assert.doesNotMatch(script, /innerHTML|localStorage|sessionStorage/);
    assert.match(guide, /Previously saved appearances/);
});

test('TenantAdmin workspace previews existing institute branding without changing personal settings', () => {
    const view = read('EduOS.App/Views/WorkspacePreferences/Index.cshtml');
    const script = read('EduOS.App/wwwroot/js/platform/workspace-institute-branding.js');
    const profileApi = read('EduOS.App/Controllers/Api/TenantProfileController.cs');
    const settingsApi = read('EduOS.App/Controllers/Api/TenantSettingController.cs');
    const account = read('EduOS.App/Controllers/AccountController.cs');
    const manual = read('EduOS.App/wwwroot/js/platform/page-context-help.js');
    assert.match(view, /@if \(User\.IsInRole\("TenantAdmin"\)\)/);
    assert.match(view, /id="workspaceInstituteBranding"/);
    assert.match(view, /href="\/Account\/BrandingSetup"/);
    assert.match(view, /workspace-institute-branding\.js/);
    assert.match(profileApi, /\[Authorize\(Roles = "TenantAdmin,SuperAdmin"\)\]/);
    assert.match(account, /BrandingSetup\(\) => View\(\)/);
    assert.match(settingsApi, /TenantAdmin/);
    assert.match(script, /\/api\/tenant-profile/);
    assert.match(script, /\/api\/tenant-settings\?category=Branding&page=1&pageSize=50/);
    assert.match(script, /await proveSession\(controller\.signal\);[\s\S]*?render\(snapshot\(profile, settings\)\)/);
    assert.match(script, /EduOSWorkspaceResponseMatchesSession/);
    assert.doesNotMatch(script, /method:\s*['"](?:POST|PUT|DELETE)['"]|innerHTML|localStorage|sessionStorage/);
    assert.match(manual, /version: 4, reviewed: true/);
    assert.match(manual, /institute branding fails to load/);
    const { color, safeLogoPath, snapshot, sessionMatches } =
        require('../../EduOS.App/wwwroot/js/platform/workspace-institute-branding.js');
    assert.equal(color('#ab12ef', '#000000'), '#AB12EF');
    assert.equal(color('red; background:url(//evil)', '#000000'), '#000000');
    assert.equal(safeLogoPath('/uploads/tenant/logo.png'), '/uploads/tenant/logo.png');
    for (const unsafe of ['https://evil.test/uploads/logo.png', '//evil.test/uploads/logo.png',
        '/uploads/../secret', '/uploads/logo.png?token=private', 'javascript:alert(1)']) {
        assert.equal(safeLogoPath(unsafe), null);
    }
    const data = snapshot({ name: '<script>Institution</script>', logoUrl: '/uploads/tenant/logo.png' },
        { items: [{ key: 'Branding.PrimaryColor', value: '#1a2b3c' },
                  { key: 'Branding.SecondaryColor', value: 'url(javascript:evil)' }] });
    assert.equal(data.name, '<script>Institution</script>');
    assert.equal(data.colors.primary, '#1A2B3C');
    assert.equal(data.colors.secondary, '#64748B');
    assert.equal(data.logo, '/uploads/tenant/logo.png');
    assert.equal(sessionMatches({ ok: true }, () => true), true);
    assert.equal(sessionMatches({ ok: true }, () => false), false);
    assert.equal(sessionMatches({ ok: false }, () => true), false);
});
