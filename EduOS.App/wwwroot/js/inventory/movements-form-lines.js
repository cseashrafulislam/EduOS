import { el, notice, td } from './movements-api.js';
export function createLineEditor() {
    const lines = [];
    function render() {
        const body = el('movementDraftLines'); body.replaceChildren();
        lines.forEach((line, i) => {
            const tr = document.createElement('tr');
            [line.label, line.lotOrBatchNo, line.quantity, line.unitCost].forEach(x => tr.append(td(x)));
            const action = td(''), button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-outline-danger btn-sm'; button.textContent = 'Remove';
            button.addEventListener('click', () => { lines.splice(i, 1); render(); });
            action.append(button); tr.append(action); body.append(tr);
        });
    }
    el('movementAddLine').addEventListener('click', () => {
        const item = el('movementItem'), quantity = el('movementQty').value.trim();
        const cost = el('movementType').value === 'Receipt' ? el('movementCost').value.trim() : '0';
        if (!item.value || !/^\d{1,7}(\.\d{1,6})?$/.test(quantity) || Number(quantity) <= 0 ||
            !/^\d{1,8}(\.\d{1,4})?$/.test(cost)) return notice('Select an item and valid quantity/cost.');
        lines.push({ inventoryItemId: Number(item.value), label: item.selectedOptions[0].textContent,
            quantity: Number(quantity), unitCost: Number(cost), lotOrBatchNo: el('movementLot').value.trim() });
        render();
    });
    return { lines, clear: () => { lines.length = 0; render(); } };
}