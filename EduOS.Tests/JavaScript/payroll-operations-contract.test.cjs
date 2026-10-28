'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const js=read('EduOS.App/wwwroot/js/payroll/index.js');
const view=read('EduOS.App/Views/PayrollOperations/Index.cshtml');
const api=read('EduOS.App/Controllers/Api/HrPayrollController.cs');
const service=read('EduOS.Service/Services/HR/HrPayrollService.cs');
const dto=read('EduOS.Core/DTOs/Payroll/PayrollDtos.cs');
new Function(js);
for(const endpoint of ['/api/hr-payroll/runs','/approve','/employees','/api/hr-payroll/my-payslips',
    '/api/hr-payroll/payments','/api/finance/accounting/setup/banks','/bank-accounts'])
    assert.ok(js.includes(endpoint),'Missing canonical route: '+endpoint);
for(const obsolete of ['/api/hr-payroll/generate','/api/hr-payroll/period','/api/hr-payroll/salary-structure'])
    assert.ok(!js.includes(obsolete),'Obsolete payroll route: '+obsolete);
assert.doesNotMatch(js,/['"`]\/api\/hr-payroll\/pay['"`]/,'Obsolete pay route must not be used');
assert.match(js,/RequestVerificationToken=token/);
assert.match(js,/clientRequestId:state.requestId/);
assert.match(js,/rowVersion:latest.rowVersion/);
assert.match(js,/data.items/);
assert.match(js,/state.requestId=null/);
assert.match(js,/payrollEmployeeRowVersion:employee.rowVersion/);
assert.match(js,/clientRequestId:requestId/);
assert.match(js,/paymentMethod:method/);
assert.match(js,/state.paymentRequestId = crypto.randomUUID\(\)/);
assert.match(js,/state.paymentBusy/);
assert.match(js,/No funds are moved/);
assert.doesNotMatch(js,/innerHTML\s*=/);
assert.match(view,/@Html.AntiForgeryToken\(\)/);
assert.match(view,/User.IsInRole\("TenantAdmin"\)/);
assert.match(view,/PayrollJournalPosting\/Index/);
assert.match(view,/PayrollPaymentPosting\/Index/);
for(const id of ['payrollRunRows','payrollEmployeeRows','payrollSlipRows','payrollRunPageLabel',
    'payrollDetailPageLabel','payrollSlipPageLabel','payrollPaymentRequestForm',
    'payrollPaymentDate','payrollPaymentAmount','payrollPaymentMethod','payrollPaymentBankFields',
    'payrollPaymentSourceBank','payrollPaymentDestinationBank','payrollPaymentExternalRef'])
    assert.ok(view.includes('id="'+id+'"'),'Missing element: '+id);
for(const route of ['runs','runs/{reference:guid}/employees','runs/{reference:guid}/approve','my-payslips'])
    assert.ok(api.includes('"'+route+'"'),'API route missing: '+route);
for(const method of ['CreatePayrollRunAsync','GetPayrollRunsAsync','GetPayrollEmployeesAsync','ApprovePayrollRunAsync','GetMyPayslipsAsync'])
    assert.ok(service.includes(method),'Service missing: '+method);
for(const field of ['ClientRequestId','RowVersion','GrossAmount','DeductionAmount','NetAmount'])
    assert.ok(dto.includes(field),'DTO missing: '+field);
assert.match(dto,/class PayrollEmployeeDto[\s\S]*?public string RowVersion/);
assert.match(service,/Id = row.Id, RowVersion = Version\(row.RowVersion\)/);
assert.match(service,/PaymentMethodType\.Cash or PaymentMethodType\.BankTransfer/);
assert.match(service,/PaymentDate < run.PeriodEnd/);
for(const route of ['runs','runs/{reference:guid}','runs/{reference:guid}/employees',
    'runs/{reference:guid}/approve','runs/{reference:guid}/post','payments']) {
    assert.ok(api.includes('"'+route+'")'),'Canonical route missing: '+route);
}
console.log('Payroll operations canonical routes, security token, state and JavaScript syntax: PASS');
