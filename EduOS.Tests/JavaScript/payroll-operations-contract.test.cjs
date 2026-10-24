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
for(const endpoint of ['/api/hr-payroll/runs','/approve','/employees','/api/hr-payroll/my-payslips'])
    assert.ok(js.includes(endpoint),'Missing canonical route: '+endpoint);
for(const obsolete of ['/api/hr-payroll/generate','/api/hr-payroll/period','/api/hr-payroll/pay','/api/hr-payroll/my','/api/hr-payroll/salary-structure'])
    assert.ok(!js.includes(obsolete),'Obsolete payroll route: '+obsolete);
assert.match(js,/RequestVerificationToken=token/);
assert.match(js,/clientRequestId:state.requestId/);
assert.match(js,/rowVersion:latest.rowVersion/);
assert.match(js,/data.items/);
assert.match(js,/state.requestId=null/);
assert.doesNotMatch(js,/innerHTML\s*=/);
assert.match(view,/@Html.AntiForgeryToken\(\)/);
assert.match(view,/User.IsInRole\("TenantAdmin"\)/);
assert.match(view,/PayrollJournalPosting\/Index/);
assert.match(view,/PayrollPaymentPosting\/Index/);
for(const id of ['payrollRunRows','payrollEmployeeRows','payrollSlipRows','payrollRunPageLabel','payrollDetailPageLabel','payrollSlipPageLabel'])
    assert.ok(view.includes('id="'+id+'"'),'Missing element: '+id);
for(const route of ['runs','runs/{reference:guid}/employees','runs/{reference:guid}/approve','my-payslips'])
    assert.ok(api.includes('"'+route+'"'),'API route missing: '+route);
for(const method of ['CreatePayrollRunAsync','GetPayrollRunsAsync','GetPayrollEmployeesAsync','ApprovePayrollRunAsync','GetMyPayslipsAsync'])
    assert.ok(service.includes(method),'Service missing: '+method);
for(const field of ['ClientRequestId','RowVersion','GrossAmount','DeductionAmount','NetAmount'])
    assert.ok(dto.includes(field),'DTO missing: '+field);
console.log('Payroll operations canonical routes, security token, state and JavaScript syntax: PASS');
