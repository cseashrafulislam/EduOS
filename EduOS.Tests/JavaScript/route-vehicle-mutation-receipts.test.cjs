const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const read = file => fs.readFileSync(path.join(__dirname, '../..', file), 'utf8');

const commands = [
    {
        name: 'start reschedule', dto: 'RescheduleUpcomingRouteVehicleRequestDto.cs',
        service: 'TransportService.RouteVehicleStartReschedule.cs',
        script: 'route-vehicle-reschedule.js', method: 'rescheduleUpcomingLink',
        endpoint: 'reschedule-start', operation: 'transport.route-vehicle-start-reschedule',
        answer: '2026-11-05', field: 'effectiveFrom'
    },
    {
        name: 'period extension', dto: 'ExtendUpcomingRouteVehicleRequestDto.cs',
        service: 'TransportService.UpcomingRouteVehicleExtension.cs',
        script: 'route-vehicle-upcoming-extend.js', method: 'extendUpcomingLink',
        endpoint: 'extend-upcoming', operation: 'transport.route-vehicle-extend-upcoming',
        answer: '2026-12-15', field: 'effectiveTo'
    },
    {
        name: 'period shortening', dto: 'ShortenUpcomingRouteVehicleRequestDto.cs',
        service: 'TransportService.UpcomingRouteVehicleShortening.cs',
        script: 'route-vehicle-upcoming-shorten.js', method: 'shortenUpcomingLink',
        endpoint: 'shorten-upcoming', operation: 'transport.route-vehicle-shorten-upcoming',
        answer: '2026-11-15', field: 'effectiveTo'
    },
    {
        name: 'upcoming cancellation', dto: 'CancelUpcomingRouteVehicleLinkRequestDto.cs',
        service: 'TransportService.RouteVehicleUpcomingCancellation.cs',
        script: 'route-vehicle-cancel.js', method: 'cancelUpcomingLink',
        endpoint: 'cancel-upcoming', operation: 'transport.route-vehicle-cancel-upcoming'
    }
];

const item = { id: 77, effectiveFrom: '2026-11-01', effectiveTo: '2026-11-30',
    isCurrent: true, isCancelled: false, activeRiders: 0, rowVersion: 'version' };

for (const cmd of commands) {
    test(`${cmd.name}: tenant-scoped durable receipts and original UI request key`, async () => {
        const dto = read('EduOS.Core/DTOs/Transport/' + cmd.dto);
        const service = read('EduOS.Service/Services/Transport/' + cmd.service);
        const source = read('EduOS.App/wwwroot/js/transport-operations/' + cmd.script);
        assert.match(dto, /public Guid ClientRequestId \{ get; set; \}/);
        assert.ok(service.includes(cmd.operation));
        for (const marker of ['CanManageCampusAsync', 'IsolationLevel.Serializable',
            'CommandFingerprint(', 'DeriveRouteVehicleCommandRequestId(',
            'ReplayAsync<RouteVehicleAssignmentDto>', 'if (replay.Conflict)', 'if (replay.Found)',
            '_commandReceipts.RecordAsync']) {
            assert.ok(service.includes(marker), `${cmd.name} lacks ${marker}`);
        }
        assert.ok(service.indexOf('CanManageCampusAsync') < service.indexOf('_commandReceipts.ReplayAsync'));
        assert.match(source, /clientRequestId:/);
        assert.match(source, /randomUUID/);
        assert.match(source, /if\s*\(busy\)\s*return/);

        let nonce = 0, fail = true;
        const requests = [];
        const u = {
            today: () => '2026-10-26',
            notify: () => {},
            load: async () => {},
            api: async (...args) => {
                requests.push(args);
                if (fail) { fail = false; throw new Error('network timeout'); }
                return { success: true };
            }
        };
        vm.runInNewContext(source, { window: {
            transportLink: u, prompt: () => cmd.answer, confirm: () => true,
            crypto: { randomUUID: () => `00000000-0000-4000-8000-${String(++nonce).padStart(12, '0')}` }
        }, Date, Number, encodeURIComponent });
        await u[cmd.method](item);
        await u[cmd.method](item);
        assert.equal(requests.length, 2);
        assert.equal(nonce, 1, 'retry must not generate another request ID');
        assert.equal(requests[0][0], `/api/transport/route-vehicle-links/77/${cmd.endpoint}`);
        assert.equal(requests[0][1], 'POST');
        assert.equal(requests[0][2].rowVersion, 'version');
        if (cmd.field) assert.equal(requests[0][2][cmd.field], cmd.answer);
        assert.equal(requests[0][2].clientRequestId, requests[1][2].clientRequestId);
        await u[cmd.method](item);
        assert.equal(nonce, 2, 'successful command releases the request key');
    });
}
