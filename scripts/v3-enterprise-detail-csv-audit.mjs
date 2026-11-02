// Read-only enterprise Stock/Asset CSV-to-JSON integrity acceptance.
// Compare canonical source, Finance and campus evidence, not merely UUIDs.
// Error messages are deliberately independent of private response values.
const ensure = (ok, message) => { if (!ok) throw new Error(message); };
const STOCK_HEADER = ['Evidence', 'Reference', 'Number', 'State', 'Debit', 'Credit',
    'RecordedValue', 'SourceType', 'SourceId', 'GoodsReceiptReference',
    'PurchaseOrderReference', 'SourceEvidenceStatus'];
const ASSET_HEADER = ['Evidence', 'Reference', 'State', 'Debit', 'Credit',
    'RunAmount', 'LinesAmount', 'Campus', 'PeriodEnd'];
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const safeCell = value => {
    const s = value == null ? '' : String(value);
    return s && '=+-@\t\r'.includes(s[0]) ? "'" + s : s;
};
const exact = (csv, value) => csv === safeCell(value);
const money = value => {
    const s = String(value);
    ensure(/^-?\d+(?:\.\d{1,10})?$/.test(s), 'Invalid detail monetary evidence.');
    const neg = s[0] === '-';
    const [whole, fraction = ''] = (neg ? s.slice(1) : s).split('.');
    const scaled = BigInt(whole) * 10000000000n + BigInt(fraction.padEnd(10, '0'));
    return neg ? -scaled : scaled;
};
const amount = (csv, value) => {
    const plain = csv.startsWith("'") ? csv.slice(1) : csv;
    ensure(csv === safeCell(plain), 'Detail CSV monetary formula protection or value mismatch.');
    return money(plain) === money(value);
};
const validRef = value => typeof value === 'string' && UUID.test(value);
const journalRows = (journals, rows, offset, kind) => {
    ensure(Array.isArray(journals) && journals.length <= 100 &&
        rows.length === journals.length + offset,
        'Detail CSV journal row count mismatch.');
    const seen = new Set();
    for (let i = 0; i < journals.length; i++) {
        const j = journals[i], r = rows[offset + i];
        ensure(validRef(j?.reference) && !seen.has(j.reference.toLowerCase()),
            'Detail journal reference invalid or repeated.');
        seen.add(j.reference.toLowerCase());
        const shared = r[0] === 'Journal' && exact(r[1], j.reference) &&
            exact(r[kind === 'stock' ? 3 : 2], j.state) &&
            amount(r[kind === 'stock' ? 4 : 3], j.debit) &&
            amount(r[kind === 'stock' ? 5 : 4], j.credit);
        const rest = kind === 'stock'
            ? exact(r[2], j.number) && r.slice(6).every(cell => cell === '')
            : exact(r[7], j.campusId) && r.slice(5, 7).every(cell => cell === '') &&
                r[8] === '';
        ensure(shared && rest, 'Detail CSV journal source/Finance evidence mismatch.');
    }
};

export function assertDetailCsvParity({ kind, data, csv, reference }) {
    ensure(kind === 'stock' || kind === 'asset', 'Invalid detail evidence kind.');
    const header = kind === 'stock' ? STOCK_HEADER : ASSET_HEADER;
    ensure(csv?.header?.join('|') === header.join('|') &&
        Array.isArray(csv.rows) && csv.rows.length > 0 &&
        csv.rows.every(row => row.length === header.length),
        'Detail CSV schema mismatch.');
    const d = data, r = csv.rows[0];
    ensure(d && validRef(reference) &&
        validRef(kind === 'stock' ? d.movementReference : d.runReference) &&
        String(kind === 'stock' ? d.movementReference : d.runReference).toLowerCase() ===
            reference.toLowerCase(), 'Detail canonical reference mismatch.');
    if (kind === 'stock') {
        ensure(r[0] === 'Stock' && exact(r[1], d.movementReference) &&
            exact(r[2], d.movementNumber) && exact(r[3], d.state) &&
            r[4] === '' && r[5] === '' && amount(r[6], d.recordedMovementValue) &&
            exact(r[7], d.sourceType) && exact(r[8], d.sourceId) &&
            exact(r[9], d.procurementGoodsReceiptReference) &&
            exact(r[10], d.procurementPurchaseOrderReference) &&
            exact(r[11], d.sourceEvidenceStatus),
            'Stock detail CSV source/Finance evidence mismatch.');
    } else {
        ensure(r[0] === 'AssetDepreciation' && exact(r[1], d.runReference) &&
            exact(r[2], d.state) && r[3] === '' && r[4] === '' &&
            amount(r[5], d.runTotalAmount) &&
            amount(r[6], d.depreciationLinesAmount) &&
            Number.isSafeInteger(d.campusId) && d.campusId > 0 &&
            exact(r[7], d.campusId) &&
            typeof d.periodEnd === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(d.periodEnd) &&
            exact(r[8], d.periodEnd),
            'Asset detail CSV campus/period/Finance evidence mismatch.');
    }
    journalRows(d.journals, csv.rows, 1, kind);
}
