const fs=require('node:fs'),path=require('node:path'),test=require('node:test'),assert=require('node:assert/strict');
const script=fs.readFileSync(path.join(__dirname,'../../EduOS.App/wwwroot/js/procurement/suppliers.js'),'utf8');
const razor=fs.readFileSync(path.join(__dirname,'../../EduOS.App/Views/ProcurementSuppliers/Index.cshtml'),'utf8');
test('supplier onboarding uses CSRF, safe DOM and bilingual responsive dialogs',()=>{assert.match(script,/RequestVerificationToken/);assert.match(script,/credentials:'same-origin'/);assert.match(script,/cache:'no-store'/);assert.match(script,/textContent/);assert.match(script,/event.persisted/);assert.match(razor,/data-checker/);assert.match(razor,/data-i18n/);assert.match(razor,/spDecisionDialog/);});
