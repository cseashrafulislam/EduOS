'use strict';
const test=require('node:test'),assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const js=read('EduOS.App/wwwroot/js/finance/fee-discount-attribution-review.js');
const razor=read('EduOS.App/Views/FeeDiscountAttributionReview/Index.cshtml');
const controller=read('EduOS.App/Controllers/FeeDiscountAttributionReviewController.cs');
test('historical discount triage stays read-only and server-authorized',()=>{
 assert.match(controller,/Authorize\(Roles = "TenantAdmin,Principal,Accountant"\)/);
 assert.match(controller,/RequireModule\("FINANCE"\)/);
 assert.match(js,/\/api\/finance\/fees\/discount-attribution-review/);
 assert.match(js,/credentials:'same-origin'/);
 assert.match(js,/cache:'no-store'/);
 assert.doesNotMatch(js,/method:\s*['"](?:POST|PUT|PATCH|DELETE)/);
});
test('discount triage is bilingual and uses one shared contextual Help Drawer',()=>{
 assert.match(razor,/CultureInfo.CurrentUICulture.Name == "bn-BD"/);
 assert.doesNotMatch(razor,/<details|ⓘ/);
 assert.match(js,/document\.documentElement\.lang/);
 assert.match(js,/Candidate amount \(unverified\)/);
 assert.match(js,/t\('Finance access denied/);
});
test('filter and tenant switch cancel stale evidence before export',()=>{
 assert.match(js,/function invalidate\(\)/);
 assert.match(js,/pending\.abort\(\)/);
 assert.match(js,/identity!==context\(\)/);
 assert.match(js,/query\.toString\(\)!==filters\(\)\.toString\(\)/);
 assert.match(js,/data\.campusId/);
 assert.match(js,/invoices\.page!==requested/);
 assert.match(js,/reviewCount!==data\.reviewRowsOnPage/);
 assert.match(js,/candidateSum/);
 assert.match(js,/MutationObserver/);
 assert.match(js,/response\.status===401 \|\| response\.status===403/);
 assert.doesNotMatch(js,/throw Error\(json\.message/);
});
test('verified current-page CSV preserves numeric cents and neutralizes text formulas',()=>{
 assert.match(js,/BigInt/);
 assert.match(js,/\\uFEFF/);
 assert.match(js,/\\u200b-\\u200f/);
 assert.match(js,/csvCell\(v,index>0&&numericColumns\.has\(i\)\)/);
 assert.match(js,/URL\.revokeObjectURL/);
 assert.match(js,/if\(verified\(\)\)window\.print\(\)/);
});
