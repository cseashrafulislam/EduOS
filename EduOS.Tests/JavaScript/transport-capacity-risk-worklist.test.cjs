'use strict';
const test = require('node:test'), assert = require('node:assert/strict'), fs = require('node:fs');
const read = path => fs.readFileSync(path, 'utf8');
const svc = read('EduOS.Service/Services/Transport/TransportService.CapacityReport.cs');
const risk = read('EduOS.Service/Services/Transport/TransportService.CapacityRisk.cs');
const iface = read('EduOS.Core/Interfaces/IServices/ITransportService.cs');
const dto = read('EduOS.Core/DTOs/Transport/TransportCapacityReportDtos.cs');
const api = read('EduOS.App/Controllers/Api/TransportController.cs');
const partial = read('EduOS.App/Views/TransportOperations/_CapacityReport.cshtml');
const js = read('EduOS.App/wwwroot/js/transport-operations/capacity-report.js');
test('risk filtering is applied before final pagination, bounded and consistency checked', () => {
    assert.match(svc, /if \(attentionOnly\) return await GetCapacityRiskAsync/);
    assert.match(risk, /TotalCount > 1000/);
    assert.match(risk, /scanPage <=/);
    assert.match(risk, /next.Data.TotalCount != first.Data.TotalCount/);
    assert.match(risk, /!unique.Add\(x.VehicleReference\)/);
    assert.match(risk, /all.Where\(x => x.HasRisk\).ToList\(\)/);
    assert.match(risk, /risks.Skip\(\(page - 1\) \* pageSize\).Take\(pageSize\)/);
});
test('risk criteria and privacy reuse canonical tenant/campus capacity rows', () => {
    assert.match(dto, /bool HasRisk/);
    assert.match(svc, /count > x.Capacity/);
    assert.match(svc, /!x.IsActive \|\| routes.GetValueOrDefault\(x.Id\) == 0/);
    assert.match(svc, /AllowedCampusIds\(tenant\)/);
    assert.match(svc, /x.StartDate <= today/);
    assert.doesNotMatch(risk, /StudentName|MonthlyFare|DriverName/);
});
test('authorized API, Razor and JS share risk filter for page and export', () => {
    assert.match(iface, /ExportCapacityCsvAsync\([^;]+attentionOnly = false\)/);
    assert.match(api, /GetCapacityReportAsync\(campusId, page, pageSize, search, cancellationToken, attentionOnly\)/);
    assert.match(api, /ExportCapacityCsvAsync\(campusId, search, cancellationToken, attentionOnly\)/);
    assert.match(partial, /transportCapacityAttentionOnly/);
    assert.match(js, /q.set\('attentionOnly', 'true'\)/);
    assert.match(js, /transportCapacityAttentionOnly'\).addEventListener\('change', invalidate\)/);
    assert.match(svc, /row.HasRisk \? "Yes" : "No"/);
    new Function(js);
});
