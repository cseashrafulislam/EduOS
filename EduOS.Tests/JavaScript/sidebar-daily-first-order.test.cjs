'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const layout = fs.readFileSync(path.join(root, 'EduOS.App/Views/Shared/_Layout.cshtml'), 'utf8');
const { groupFor } = require('../../EduOS.App/wwwroot/js/sidebar-navigation.js');

test('Dashboard and everyday workflows precede administrative setup', () => {
    const dashboard = layout.indexOf('href="/Dashboard/Index"');
    const workCenter = layout.indexOf('href="/CentralWorkCenter/Index"');
    const academicManagement = layout.indexOf('var academicPath = "/AcademicManagement/"');
    const academics = layout.indexOf('data-sidebar-section="academics"');
    const management = layout.indexOf('data-sidebar-section="management"');
    const adminHeading = layout.indexOf('Administration & Setup');
    const userAccess = layout.indexOf('href="/TenantUsers/Index"');
    const institution = layout.indexOf('Path: "/InstitutionManagement/Profile"');
    assert.ok([dashboard, workCenter, academicManagement, academics, management,
        adminHeading, userAccess, institution].every(index => index >= 0));
    assert.ok(dashboard < workCenter && workCenter < academicManagement);
    assert.ok(academicManagement < academics && academics < management);
    assert.ok(management < adminHeading && adminHeading < userAccess && userAccess < institution);
});

test('No administrative link is duplicated, lost, or exposed outside TenantAdmin', () => {
    const heading = layout.indexOf('Administration & Setup');
    const adminPart = layout.slice(heading);
    const roleCheck = layout.lastIndexOf('@if (User?.IsInRole("TenantAdmin") == true)', heading);
    const management = layout.indexOf('data-sidebar-section="management"');
    assert.ok(roleCheck > management, 'administration section must remain role gated');
    assert.equal((layout.match(/data-sidebar-section="setup"/g) || []).length, 2);
    const links = [
        '/TenantUsers/Index',
        '/TenantPermissionOverrides/Index',
        '/TenantDomains/Index',
        '/TenantTerminology/Index',
        '/TenantSettings/Index',
        '/SmartSetup/Index',
        '/NumberSeries/Index',
        '/TenantModules/Index',
        '/SubscriptionBilling/Index',
        '/IntegrationAdministration/Index'
    ];
    for (const href of links) {
        assert.equal(layout.split('href="' + href + '"').length - 1, 1, href);
        assert.ok(adminPart.includes('href="' + href + '"'), href + ' must be last');
    }
    assert.equal((layout.match(/Path: "\/InstitutionManagement\/(Profile|Campuses|Regional|Branding|Subdomain|History)"/g) || []).length, 6);
});

test('Existing authorization-backed sidebar categories and grouping remain intact', () => {
    assert.equal(groupFor('setup', '/CentralWorkCenter/Index'), 'work-center');
    assert.equal(groupFor('setup', '/AcademicManagement/Years'), 'academic-setup');
    assert.equal(groupFor('setup', '/TenantUsers/Index'), 'access');
    assert.equal(groupFor('setup', '/InstitutionManagement/Branding'), 'institution');
    assert.match(layout, /data-shell-next-task="true"/);
    assert.match(layout, /OptionalModuleNavigation\.GetAvailableAsync/);
    assert.match(layout, /V2RolePortalNavigation\.GetLinks/);
});
