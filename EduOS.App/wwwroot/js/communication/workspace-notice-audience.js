import { el, state, alert } from './workspace-core.js?v=1';
const key = a => [a.audienceTypeCode, a.campusId || '', a.academicProgramId || '', a.academicBatchId || '', a.userId || ''].join('|');
export function renderAudiences() {
    const list = el('cwAudiences'); list.replaceChildren();
    state.audiences.forEach((a, index) => {
        const chip = document.createElement('span'); chip.className = 'badge text-bg-light border';
        chip.append(document.createTextNode(a.audienceTypeCode + (a.userId || a.campusId || a.academicProgramId || a.academicBatchId ?
            ': ' + (a.userId || a.campusId || a.academicProgramId || a.academicBatchId) : '') + ' '));
        const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-outline-secondary btn-sm';
        button.textContent = 'Remove'; button.addEventListener('click', () => { state.audiences.splice(index, 1); renderAudiences(); });
        chip.append(button); list.append(chip);
    });
}
export function initAudience() {
    el('cwAudienceType').addEventListener('change', () => { el('cwAudienceId').disabled = el('cwAudienceType').value === 'All'; });
    el('cwAddAudience').addEventListener('click', () => {
        const code = el('cwAudienceType').value, id = Number(el('cwAudienceId').value);
        if (code !== 'All' && (!Number.isSafeInteger(id) || id <= 0)) { alert('Enter a positive audience ID.'); return; }
        const a = { audienceTypeCode: code };
        if (code === 'User') a.userId = id;
        if (code === 'Campus') a.campusId = id;
        if (code === 'Program') a.academicProgramId = id;
        if (code === 'Batch') a.academicBatchId = id;
        if (state.audiences.some(x => key(x) === key(a))) return;
        if (state.audiences.length >= 25) { alert('Maximum 25 audiences per notice.'); return; }
        if (code === 'All') state.audiences = [a];
        else { state.audiences = state.audiences.filter(x => x.audienceTypeCode !== 'All'); state.audiences.push(a); }
        renderAudiences();
    });
}
