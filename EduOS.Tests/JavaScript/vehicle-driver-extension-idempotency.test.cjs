const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = p => fs.readFileSync(path.join(__dirname, '../..', p), 'utf8');

const variants = [
    {
        name: 'operating',
        dto: 'EduOS.Core/DTOs/Transport/ExtendOperatingVehicleDriverRequestDto.cs',
        service: 'EduOS.Service/Services/Transport/TransportService.DriverExtension.cs',
        ui: 'EduOS.App/wwwroot/js/transport-operations/vehicle-driver-list.js',
        endpoint: /\/extend','POST'/,
        scope: 'transport.operating-driver-extension'
    },
    {
        name: 'upcoming',
        dto: 'EduOS.Core/DTOs/Transport/ExtendUpcomingVehicleDriverRequestDto.cs',
        service: 'EduOS.Service/Services/Transport/TransportService.UpcomingDriverExtension.cs',
        ui: 'EduOS.App/wwwroot/js/transport-operations/vehicle-driver-upcoming-extend.js',
        endpoint: /\/extend-upcoming','POST'/,
        scope: 'transport.upcoming-driver-extension'
    }
];

for (const variant of variants) {
    test(`${variant.name} driver extension persists replayable outcomes inside its transaction`, () => {
        const dto = read(variant.dto);
        const service = read(variant.service);
        assert.match(dto, /public Guid ClientRequestId \{ get; set; \}/);
        assert.match(service, /IsolationLevel\.Serializable/);
        assert.match(service, /CanManageDriverRegistry\(\)/);
        assert.match(service, /CanManageCampusAsync/);
        assert.ok(service.includes(variant.scope));
        assert.match(service, /LegacyExtensionRequestId\(tenant, _currentUser\.UserId, operation, assignmentId, requestHash\)/);
        assert.match(service, /_commandReceipts\.ReplayAsync<VehicleDriverAssignmentDto>/);
        assert.match(service, /_commandReceipts\.RecordAsync/g);
        assert.match(service, /if \(replay\.Conflict\)/);
        assert.match(service, /if \(replay\.Found\) return replay\.Response!/);
        assert.ok(service.indexOf('CanManageCampusAsync') < service.indexOf('_commandReceipts.ReplayAsync'));
        assert.ok(service.indexOf('_commandReceipts.ReplayAsync') < service.indexOf('!link.RowVersion.AsSpan().SequenceEqual'));
        assert.ok((service.match(/_unitOfWork\.SaveChangesAsync\(token\)/g) || []).length >= 3);
        assert.match(service, /request\.EffectiveTo\.ToString\("yyyy-MM-dd"\)/);
        const ui = read(variant.ui);
        assert.match(ui, variant.endpoint);
        assert.match(ui, /window\.crypto\?\.randomUUID/);
        assert.match(ui, /clientRequestId:/);
        assert.match(ui, /requestSignature|extensionSignature/);
        assert.doesNotMatch(ui, /innerHTML/);
        new Function(ui);
    });
}

test('legacy deterministic keys isolate tenants, actors, operations and original versions', () => {
    const helper = read('EduOS.Service/Services/Transport/TransportService.CommandFingerprint.cs');
    for (const value of ['tenantId', 'actorId', 'operation', 'assignmentId', 'requestHash'])
        assert.match(helper, new RegExp(value));
    assert.match(helper, /SHA256\.HashData/);
    assert.match(helper, /new Guid\(digest\.AsSpan\(0, 16\)\)/);
});
