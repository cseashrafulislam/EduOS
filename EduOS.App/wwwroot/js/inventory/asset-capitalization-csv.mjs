// Bounded evidence-only CSV. Escape formula-like spreadsheet cells, including leading whitespace.
export function capitalizationCsv(data) {
    const escape = value => {
        let s = value == null ? '' : String(value);
        if (/^\s*[=+\-@\t\r]/u.test(s)) s = "'" + s;
        return '"' + s.replaceAll('"', '""') + '"';
    };
    const header = ['Asset tag', 'Campus ID', 'Status', 'Receipt reference',
        'Posted stock movement', 'Purchase order ID', 'Line', 'Quantity', 'Posted unit cost', 'Valuation policy', 'Lot/batch', 'Reviewed UTC'];
    const rows = (data.candidates || []).slice(0, 25).map(x => [
        data.assetTag, data.campusId, data.statusCode, x.receiptReference,
        x.postedMovementReference, x.purchaseOrderId, x.lineNo, x.quantity, x.postedUnitCost,
        x.valuationMode, x.lotOrBatchNo, x.reviewedAtUtc
    ]);
    if (!rows.length) rows.push([data.assetTag, data.campusId, data.statusCode, '', '', '', '', '', '', '', '', '']);
    return '\uFEFF' + [header, ...rows].map(row => row.map(escape).join(',')).join('\r\n') + '\r\n';
}
