'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const js=read('EduOS.App/wwwroot/js/finance/discount-rules.js');
const razor=read('EduOS.App/Views/FeeDiscountRules/Index.cshtml');
const controller=read('EduOS.App/Controllers/FeeDiscountRulesController.cs');
const api=read('EduOS.App/Controllers/Api/FeeBillingController.cs');
test('discount rule editor compiles and preserves tenant-admin API/CSRF and rowversion',()=>{
 assert.doesNotThrow(()=>new vm.Script(js));
 assert.match(controller,/Authorize\(Roles = "TenantAdmin"\)/);
 assert.match(controller,/RequireModule\("FINANCE"\)/);
 assert.match(api,/AutoValidateAntiforgeryToken/);
 assert.match(api,/\[HttpPut\("discount-rules\/\{id:long\}"\)\]/);
 assert.match(js,/headers\.RequestVerificationToken=token/);
 assert.match(js,/rowVersion:state\.editing\?\.rowVersion/);
 assert.match(js,/credentials:'same-origin'/);
 assert.match(js,/cache:'no-store'/);
});
test('discount rule UX is bilingual with accessible controls and shared help',()=>{
 assert.match(razor,/CultureInfo\.CurrentUICulture\.Name == "bn-BD"/);
 assert.match(razor,/aria-live="assertive"/);
 assert.match(razor,/aria-describedby="discountRuleValueHelp"/);
 assert.match(js,/document\.documentElement\.lang/);
 assert.match(js,/ফি ছাড়ের নিয়ম সংরক্ষিত হয়েছে/);
 assert.match(js,/ফি ছাড়ের পরিবর্তিত নিয়ম সংরক্ষণ করবেন/);
 assert.doesNotMatch(razor,/<details|ⓘ/);
});
test('stale register and institution switch cannot expose old rows or authorize stale edits',()=>{
 assert.match(js,/new AbortController/);
 assert.match(js,/state\.read\?\.abort\(\)/);
 assert.match(js,/seq!==state\.sequence/);
 assert.match(js,/sameSession\(\)/);
 assert.match(js,/MutationObserver\(lockSession\)/);
 assert.match(js,/data\.page!==requested/);
 assert.match(js,/data\.pageSize!==25/);
 assert.match(js,/row\.rowVersion/);
 assert.match(js,/state\.saving=true/);
});
test('denied API errors never echo backend messages and no direct finance mutation is added',()=>{
 assert.match(js,/response\.status===401\|\|response\.status===403/);
 assert.match(js,/response\.status===409/);
 assert.doesNotMatch(js,/result\?\.message|json\?\.message|result\.message/);
 assert.doesNotMatch(js,/\/invoices\/generate|\/payments|\/journals|\/stock/);
});
