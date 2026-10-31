const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const app = path.resolve(__dirname, '../../EduOS.App');
const script = fs.readFileSync(path.join(app, 'wwwroot/js/procurement/supplier-confirmations.js'), 'utf8');
const view = fs.readFileSync(path.join(app, 'Views/ProcurementSupplierAcknowledgements/Index.cshtml'), 'utf8');

test('supplier confirmations provide bilingual real maker-checker workflow', () => {
 for (const action of ['submit', 'approve', 'reject', 'revise'])
  assert.match(script, new RegExp(action));
 for (const field of ['clientRequestId', 'purchaseOrderId', 'supplierReference',
  'responseDate', 'promisedDeliveryDate', 'rowVersion'])
  assert.match(script, new RegExp(field));
 assert.match(script, /credentials:'same-origin'/);
 assert.match(script, /cache:'no-store'/);
 assert.match(script, /RequestVerificationToken/);
 assert.match(script, /textContent/);
 assert.match(script, /pagehide/);
 assert.match(script, /সরবরাহকারীর নিশ্চিতকরণ/);
 assert.match(script, /checker&&!owner/);
 assert.match(script, /maker&&owner/);
 assert.match(script, /reason.length<10/);
 assert.match(view, /data-maker/);
 assert.match(view, /data-checker/);
 assert.match(view, /data-i18n/);
 assert.match(view, /ackDecisionDialog/);
 assert.match(view, /ackOrderSearch/);
 assert.match(view, /ackPromisedDate/);
 assert.match(view, /@@media print/);
});

test('confirmation list is bounded, accessible and has no fake posting action', () => {
 assert.match(script, /pageSize:25/);
 assert.match(script, /ackRows/);
 assert.match(view, /aria-live="polite"/);
 assert.match(view, /ackHelpText/);
 assert.doesNotMatch(script, /postStock|postPayment|postJournal/);
});
