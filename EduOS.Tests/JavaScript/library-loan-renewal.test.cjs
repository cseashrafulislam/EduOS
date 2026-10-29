const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const app=path.resolve(__dirname,'../../EduOS.App');
const controller=fs.readFileSync(path.join(app,'Controllers/Api/LibraryController.cs'),'utf8');
const view=fs.readFileSync(path.join(app,'Views/Library/Circulation.cshtml'),'utf8');
const script=fs.readFileSync(path.join(app,'wwwroot/js/library/circulation.js'),'utf8');
const service=fs.readFileSync(path.resolve(app,'../EduOS.Service/Services/Library/LibraryService.Renewals.cs'),'utf8');

test('staff-only renewal validates rowversion, branch and waitlist inside serializable transaction',()=>{
 assert.match(controller,/HttpPost\("issues\/\{reference:guid\}\/renew"\)/);
 assert.match(controller,/Authorize\(Roles = "TenantAdmin,Principal,Librarian"\)/);
 assert.match(service,/IsolationLevel\.Serializable/);
 assert.match(service,/StaffCanManageBranchAsync/);
 assert.match(service,/MatchesVersion/);
 assert.match(service,/BookReservationState\.Pending/);
 assert.match(script,/rowVersion:issue\.rowVersion/);
 assert.match(view,/circulationRenewalDue/);
});
test('renewal audit history is freshly authorized, bounded and user-triggered',()=>{
 assert.match(controller,/HttpGet\("issues\/\{reference:guid\}\/renewals"\)/);
 assert.match(service,/pageSize is < 1 or > 50/);
 assert.match(service,/RenewalEvents\(tenant, issue.Id\)/);
 assert.match(script,/loadRenewalHistory/);
 assert.match(script,/cache:'no-store'/);
 assert.match(view,/circulationRenewalHistoryRows/);
});
