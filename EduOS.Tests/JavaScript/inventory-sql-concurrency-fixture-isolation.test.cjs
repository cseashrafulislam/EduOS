// Source-contract guard; the disposable SQL test proves the actual concurrency behavior.
const test = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const read = path => readFileSync(resolve(__dirname, '../../', path), 'utf8');

test('inventory SQL tenant and actor fixtures use stable independent audit contexts', () => {
    const code = read('EduOS.Tests/App/SqlServerV3InventoryTransferLifecycleTests.cs');
    const open = code.match(/private static EduOSDbContext Open\([\s\S]*?\n    \}/)?.[0];
    const openAs = code.match(/private static EduOSDbContext OpenAs\([\s\S]*?\n    \}/)?.[0];
    assert.ok(open);
    assert.ok(openAs);
    assert.match(code, /private sealed class FixedHttpContextAccessor\(HttpContext context\) : IHttpContextAccessor/);
    for (const fixture of [open, openAs]) {
        assert.match(fixture, /new FixedHttpContextAccessor\(http\)/);
        assert.doesNotMatch(fixture, /new HttpContextAccessor\s*\{/);
        assert.match(fixture, /http\.Items\["TenantId"\] = tenantId/);
    }
    assert.match(openAs, /ClaimTypes\.NameIdentifier, actorId\.ToString\(\)/);
});

test('cross-store disposable SQL race retains one winner and ten units of canonical stock', () => {
    const code = read('EduOS.Tests/App/SqlServerV3InventoryMultiStoreAcceptanceTests.cs');
    const race = code.match(/public async Task Competing_seven_unit_transfers_to_different_stores_cannot_overspend_ten_unit_origin\([\s\S]*?\n    \}/)?.[0];
    assert.ok(race);
    assert.match(race, /Task\.WhenAll\(first, second\)/);
    assert.match(race, /Assert\.Single\(outcomes, x => x\.StatusCode == 200\)/);
    assert.match(race, /Assert\.Single\(outcomes, x => x\.StatusCode == 409\)/);
    assert.match(race, /Assert\.Equal\(10m, origin\.QuantityOnHand \+ destination\.QuantityOnHand \+ third\.QuantityOnHand\)/);
    assert.match(race, /x\.SourceType == "ManualTransfer" && x\.StateCode == "Posted"/);
});
