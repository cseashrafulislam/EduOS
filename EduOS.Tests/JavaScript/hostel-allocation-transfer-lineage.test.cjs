'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const js = read('EduOS.App/wwwroot/js/hostel-operations/allocation-history.js');
const view = read('EduOS.App/Views/HostelOperations/_AllocationHistory.cshtml');
const csv = read('EduOS.Service/Services/Hostel/HostelService.AllocationHistoryExport.cs');

test('transfer lineage is rendered with text-only DOM and a matching empty-state span', () => {
    assert.match(view, /<th>Transfer lineage<\/th>/);
    assert.match(js, /item\.isTransferSuccessor/);
    assert.match(js, /item\.hasTransferSuccessor/);
    assert.match(js, /cell\.colSpan = 9/);
    assert.match(js, /cell\.textContent = String\(value/);
    assert.doesNotMatch(js, /innerHTML/);
    new Function(js);
});
test('bounded existing CSV export carries privacy-safe lineage flags', () => {
    assert.match(csv, /TotalCount > 1000/);
    assert.match(csv, /Transferred In,Transferred Onward/);
    assert.match(csv, /row\.IsTransferSuccessor \? "Yes" : "No"/);
    assert.match(csv, /row\.HasTransferSuccessor \? "Yes" : "No"/);
    assert.doesNotMatch(csv, /MonthlyRent|SecurityDeposit|ClientRequestId|StudentName/);
});
