const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const script = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/finance/bank-statement-preview.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/AccountingReports/Index.cshtml'), 'utf8');

test('saved review offers explicit match, withdrawal and authorized history export', () => {
    for (const id of ['statementMatchHistoryRows', 'statementMatchHistoryCsv',
        'statementMatchHistoryPrev', 'statementMatchHistoryNext']) {
        assert.ok(view.includes('id="' + id + '"'));
        assert.ok(script.includes("el('" + id + "')"));
    }
    assert.match(script, /data-confirm-row/);
    assert.match(script, /data-withdraw-match/);
    assert.match(script, /crypto\.randomUUID\(\)/);
    assert.match(script, /RequestVerificationToken:token\(\)/);
    assert.match(script, /match-events\?page=/);
    assert.match(script, /Not approved or cleared/);
    assert.match(script, /row\.map\(cell\)/);
    assert.match(script, /escapeHtml\(x\.journalLineVersion\)/);
});
