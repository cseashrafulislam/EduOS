'use strict';
// Source/CLI contract only; NOT authenticated browser or SQL proof.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const loginView=read('EduOS.App/Views/Account/Login.cshtml');
const loginJs=read('EduOS.App/wwwroot/js/account/login.js');
const layout=read('EduOS.App/Views/Shared/_Layout.cshtml');
const appearance=read('EduOS.App/Views/WorkspacePreferences/Index.cshtml');
const branding=read('EduOS.App/Views/Account/BrandingSetup.cshtml');
const account=read('EduOS.App/Controllers/AccountController.cs');
const localization=read('EduOS.App/Controllers/LocalizationController.cs');
const livePath=path.join(root,'tests/browser/workspace-appearance-live.cjs');
const live=fs.readFileSync(livePath,'utf8');
for(const [source,needle] of [
    [loginView,'id="userNameOrEmail"'],
    [loginView,'id="institutionChoicePanel"'],
    [loginJs,"document.getElementById('userNameOrEmail')"],
    [loginJs,"fetch('/api/auth/login'"],
    [layout,'id="uiCulture"'],
    [layout,'id="pageHelpDrawer"'],
    [appearance,'id="workspacePreferencesForm"'],
    [appearance,'id="workspaceSave"'],
    [branding,'id="brandingForm"'],
    [account,'[Authorize(Roles = "TenantAdmin")]'],
    [localization,'[ValidateAntiForgeryToken]'],
    [localization,'[HttpPost("set-language")]'],
    [live,"page.locator('#userNameOrEmail').fill(user.email)"],
    [live,"route.abort('blockedbyclient')"],
    [live,"await page.goBack("],
    [live,"await page.setViewportSize({width:390"],
    [live,"Staging language restoration failed:"]
]) assert(source.includes(needle),'Staging browser source contract changed: '+needle);
assert(!live.includes("page.locator('#email')"),'Stale login selector');
const invalid=spawnSync(process.execPath,[livePath],{
    cwd:root,env:{...process.env,EDUOS_BASE_URL:'',EDUOS_E2E_STAGING_HOST:''},
    encoding:'utf8',timeout:10000
});
assert.equal(invalid.status,2,'Unconfigured staging run must fail closed before launching browser');
assert.match(invalid.stderr,/Staging URL, explicit staging hostname/);
console.log('Authenticated staging source/CLI contract PASS (not live browser proof).');
