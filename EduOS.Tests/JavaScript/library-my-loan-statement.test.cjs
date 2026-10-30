const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const service=read('EduOS.Service/Services/Library/LibraryService.cs');
const api=read('EduOS.App/Controllers/Api/LibraryController.cs');
const js=read('EduOS.App/wwwroot/js/library/index.js');
const view=read('EduOS.App/Views/Library/Index.cshtml');

test('student loan statement uses tenant-owned identity and server-filtered paging',()=>{
 assert.match(service,/x\.StudentId\.HasValue && studentIds\.Contains\(x\.StudentId\.Value\)/);
 assert.match(service,/x\.TenantId == tenant && x\.StudentId\.HasValue/);
 assert.match(service,/status == "overdue"/);
 assert.match(service,/x\.DueDate < today/);
 assert.match(service,/_user\.IsInRole\("Student"\)/);
 assert.match(api,/HttpGet\("my-issues\/export"\)/);
 assert.match(api,/GetMyIssuesAsync\(page, pageSize, ct, status\)/);
 assert.match(api,/Response\.Headers\.CacheControl = "no-store, private"/);
});

test('borrower page filters loans and exports a fresh authorized page',()=>{
 assert.match(view,/id="issueStatus"/);
 assert.match(view,/id="issueCsv"/);
 assert.match(view,/@if \(canViewMyLoans\)/);
 assert.match(js,/if \(issueRows\) loadIssues\(\)/);
 assert.match(view,/aria-label="Library help"/);
 assert.match(js,/status: el\('issueStatus'\)\.value/);
 assert.match(js,/loanEndpoint \+ '\/export\?'/);
 assert.match(view,/data-loan-endpoint="@\(isStudentBorrower \? "my-issues" : "my-employee-issues"\)"/);
 assert.match(js,/cache: 'no-store'/);
 assert.match(js,/issueRequest/);
 assert.doesNotMatch(js,/issueRows\.innerHTML\s*=/);
 new Function(js);
});
