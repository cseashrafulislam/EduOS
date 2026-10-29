'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const policy = read('EduOS.Service/Services/SaaS/SubscriptionActiveCapacityPolicy.cs');
const storage = read('EduOS.Service/Services/SaaS/SubscriptionStorageQuotaPolicy.cs');
const admission = read('EduOS.Service/Services/Admission/AdmissionEnrollmentService.cs');
const imports = read('EduOS.Service/Services/Students/StudentImportExecutionService.cs');
const readmission = read('EduOS.Service/Services/Students/StudentReadmissionService.cs');
const transitions = read('EduOS.Service/Services/Students/StudentStatusTransitionService.cs');
const hr = read('EduOS.Service/Services/HR/HrAdminService.cs');
const publicAdmission = read('EduOS.Service/Services/Admission/PublicAdmissionService.cs');
const payment = read('EduOS.Service/Services/SaaS/SubscriptionPaymentService.cs');

// A lock owned by the SQL transaction must be shared by all active-student paths.
assert.match(policy, /sp_getapplock/);
assert.match(policy, /@LockOwner = 'Transaction'/);
assert.match(policy, /CurrentTenantId != tenantId/);
assert.match(policy, /CurrentTransaction\?\.GetDbTransaction\(\)/);
assert.match(policy, /StatusCode == "Active"/);
assert.match(policy, /State == EmployeeState\.Active/);
assert.match(admission, /SubscriptionActiveCapacityPolicy\.CanAddStudentsAsync/);
assert.match(imports, /SubscriptionActiveCapacityPolicy\.CanAddStudentsAsync/);
assert.match(readmission, /SubscriptionActiveCapacityPolicy\.CanAddStudentsAsync/);
assert.match(transitions, /request\.TargetStatus == "Active"[\s\S]*?CanAddStudentsAsync/);
assert.match(hr, /CanAddEmployeesAsync/g);
assert.match(hr, /request\.State == EmployeeState\.Active[\s\S]*?CanAddEmployeesAsync/);

// Public applicant uploads are tenant-verified, transaction-bounded and quota-gated.
assert.match(publicAdmission, /ResolveVisibleTenantAsync\(tenantKey/);
assert.match(publicAdmission, /applicant == null\) return Error/);
assert.match(publicAdmission, /TransactionScopeOption\.Required/);
assert.match(publicAdmission, /SubscriptionStorageQuotaPolicy\.GetAsync\(_db, tenant\.Id/);
assert.match(publicAdmission, /if \(!quota\.CanAdd\(file\.Length\)\)/);
assert.match(publicAdmission, /DeletePrivateAsync\(uploaded\.FileUrl\)/);
assert.match(storage, /db\.CurrentTenantId != tenantId/);

// Deposit-slip evidence is a deliberate, bounded renewal-path exception.
assert.match(payment, /depositSlip\.Length > 5 \* 1024L \* 1024L/);
assert.match(payment, /UploadPrivateForTenantAsync\(depositSlip, "deposit-slips", tenant\)/);
assert.match(payment, /x\.State == PaymentState\.Initiated \|\| x\.State == PaymentState\.AwaitingVerification/);
assert.match(payment, /A full storage quota must not/);
console.log('SaaS quota write-path, tenant-scope and payment recovery contracts: PASS');
