const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = p => fs.readFileSync(path.join(__dirname, '../..', p), 'utf8');

const commands = [
    {
        name: 'future start reschedule',
        dto: 'RescheduleUpcomingVehicleDriverRequestDto.cs',
        source: 'TransportService.DriverStartReschedule.cs',
        script: 'vehicle-driver-reschedule.js',
        operation: 'transport.upcoming-driver-start-reschedule'
    },
    {
        name: 'future end shortening',
        dto: 'ShortenUpcomingVehicleDriverRequestDto.cs',
        source: 'TransportService.UpcomingDriverShortening.cs',
        script: 'vehicle-driver-upcoming-shorten.js',
        operation: 'transport.upcoming-driver-shortening'
    },
    {
        name: 'future cancellation',
        dto: 'CancelUpcomingVehicleDriverAssignmentRequestDto.cs',
        source: 'TransportService.VehicleDriverUpcomingCancellation.cs',
        script: 'vehicle-driver-list.js',
        operation: 'transport.upcoming-driver-cancellation'
    },
    {
        name: 'operating replacement',
        dto: 'ReplaceActiveVehicleDriverRequestDto.cs',
        source: 'TransportService.ActiveDriverReplacement.cs',
        script: 'vehicle-driver-replace.js',
        operation: 'transport.operating-driver-replacement'
    }
];

for (const command of commands) {
    test(`${command.name} is tenant/campus scoped and replayable`, () => {
        const dto = read('EduOS.Core/DTOs/Transport/' + command.dto);
        const service = read('EduOS.Service/Services/Transport/' + command.source);
        const script = read('EduOS.App/wwwroot/js/transport-operations/' + command.script);
        assert.match(dto, /public Guid ClientRequestId \{ get; set; \}/);
        assert.ok(service.includes(command.operation));
        for (const marker of ['CanManageDriverRegistry()', 'CanManageCampusAsync',
            'IsolationLevel.Serializable', 'CommandFingerprint(', 'ReplayAsync<VehicleDriverAssignmentDto>',
            '_commandReceipts.RecordAsync', 'if (replay.Conflict)', 'if (replay.Found)']) {
            const candidate = command.name === 'operating replacement' ? marker.replace('replay.', 'receipt.') : marker;
            assert.ok(service.includes(candidate), `${command.name}: ${candidate}`);
        }
        assert.ok(service.indexOf('CanManageCampusAsync') < service.indexOf('_commandReceipts.ReplayAsync'));
        assert.match(script, /clientRequestId:/);
        assert.match(script, /randomUUID/);
        assert.doesNotMatch(script, /innerHTML/);
        new Function(script);
    });
}

test('replacement no longer borrows the create-assignment request ID', () => {
    const script = read('EduOS.App/wwwroot/js/transport-operations/vehicle-driver-replace.js');
    assert.doesNotMatch(script, /clientRequestId:u\.requestId/);
    assert.match(script, /const signature=\[item\.id,d\.id,item\.rowVersion,d\.rowVersion\]/);
    assert.match(script, /if\(busy\)return/);
});
