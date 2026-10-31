import { api, el, td, notice } from './assets-api.js';
import { capitalizationCsv } from './asset-capitalization-csv.mjs';

const descriptions = {
    BookExists: 'Existing book; verify Finance journal and original source / বুক আছে; ফাইন্যান্স জার্নাল ও উৎস যাচাই করুন',
    AssetInactive: 'Inactive asset / নিষ্ক্রিয় সম্পদ',
    CampusRequired: 'Campus assignment required / ক্যাম্পাস নির্ধারণ প্রয়োজন',
    AssetTrackedItemRequired: 'Active asset-tracked item required / সক্রিয় সম্পদ-ট্র্যাকড আইটেম প্রয়োজন',
    CostPolicyRequired: 'Reviewed cost basis required / অনুমোদিত মূল্যনীতি প্রয়োজন',
    NoPostedReceiptEvidence: 'No current posted goods receipt candidate / পোস্টেড রিসিপ্টের প্রমাণ নেই',
    ReviewRequired: 'Candidates found; source binding and independent approval required / উৎস সংযোগ ও স্বতন্ত্র অনুমোদন প্রয়োজন'
};
let current = null;
export async function showCapitalizationReadiness(reference) {
    const panel = el('assetCapitalizationPanel');
    panel.classList.remove('d-none');
    el('assetCapitalizationStatus').textContent = 'Loading evidence / প্রমাণ লোড হচ্ছে…';
    el('assetCapitalizationExport').disabled = true;
    el('assetCapitalizationRows').replaceChildren();
    current = null;
    try {
        const data = await api('/' + encodeURIComponent(reference) + '/capitalization-readiness');
        current = data;
        el('assetCapitalizationStatus').textContent =
            data.assetTag + ' · ' + (descriptions[data.statusCode] || data.statusCode) +
            ' · ' + data.candidateLineCount + ' candidate lines / সম্ভাব্য লাইন' +
            (data.candidateListTruncated ? ' (first 25 only / প্রথম ২৫টি)' : '') +
            '. No Finance posting performed / কোনো ফাইন্যান্স পোস্টিং হয়নি।';
        const body = el('assetCapitalizationRows');
        for (const x of data.candidates || []) {
            const tr = document.createElement('tr');
            [x.receiptReference, x.postedMovementReference, x.purchaseOrderId, x.lineNo, x.quantity,
                x.postedUnitCost, x.valuationMode, x.lotOrBatchNo, x.reviewedAtUtc].forEach(value => tr.append(td(value)));
            body.append(tr);
        }
        if (!data.candidates?.length) {
            const tr = document.createElement('tr'), cell = td('No matching evidence / কোনো মিল পাওয়া যায়নি');
            cell.colSpan = 9; tr.append(cell); body.append(tr);
        }
        el('assetCapitalizationExport').disabled = false;
    } catch (error) {
        el('assetCapitalizationStatus').textContent = 'Evidence unavailable / প্রমাণ পাওয়া যায়নি';
        notice(error.message);
    }
}
export function bindCapitalizationReadiness() {
    el('assetCapitalizationClose').addEventListener('click', () => {
        el('assetCapitalizationPanel').classList.add('d-none');
        current = null;
    });
    el('assetCapitalizationExport').addEventListener('click', () => {
        if (!current) return;
        const blob = new Blob([capitalizationCsv(current)], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob), anchor = document.createElement('a');
        anchor.href = url; anchor.download = 'asset-capitalization-evidence.csv';
        document.body.append(anchor); anchor.click(); anchor.remove();
        setTimeout(() => URL.revokeObjectURL(url), 0);
    });
}
