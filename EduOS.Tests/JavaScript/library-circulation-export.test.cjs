const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const app=path.resolve(__dirname,'../../EduOS.App');
const controller=fs.readFileSync(path.join(app,'Controllers/Api/LibraryController.cs'),'utf8');
const script=fs.readFileSync(path.join(app,'wwwroot/js/library/circulation.js'),'utf8');
const view=fs.readFileSync(path.join(app,'Views/Library/Circulation.cshtml'),'utf8');

test('circulation CSV is freshly authorized, bounded and tenant/campus scoped',()=>{
 assert.match(controller,/HttpGet\("circulation\/export"\)/);
 assert.match(controller,/Authorize\(Roles = "TenantAdmin,Principal,Librarian"\)/);
 assert.match(controller,/GetCirculationRegisterAsync\(search, status, campusId, page, pageSize, ct\)/);
 assert.match(controller,/LibraryCirculationCsvWriter\.Write/);
 assert.match(controller,/Response\.Headers\.CacheControl = "no-store, private"/);
 assert.match(script,/\/api\/library\/circulation\/export\?/);
 assert.match(script,/cache:'no-store'/);
 assert.match(script,/circulationQuery\(\)/);
 assert.doesNotMatch(script,/String\.fromCharCode\(0xfeff\)/);
 assert.match(view,/aria-label="Circulation help"/);
});
