import { el, state, api, localInput, alert } from './workspace-core.js?v=1';
import { loadNotices } from './workspace-notice-list.js?v=1';
import { renderAudiences, initAudience } from './workspace-notice-audience.js?v=1';
function reset() {
    el('cwNoticeForm').reset(); el('cwReference').value = ''; el('cwVersion').value = '';
    el('cwPublishAt').value = localInput(); el('cwExpiresAt').value = '';
    el('cwAudienceType').value = 'All'; el('cwAudienceId').value = ''; el('cwAudienceId').disabled = true;
    state.audiences = [{ audienceTypeCode: 'All' }]; renderAudiences();
}
export function edit(row) {
    el('cwReference').value = row.reference; el('cwVersion').value = row.rowVersion || '';
    el('cwTitle').value = row.title; el('cwBody').value = row.body;
    el('cwCategory').value = row.noticeCategoryId || '';
    el('cwPublishAt').value = localInput(row.publishAt); el('cwExpiresAt').value = row.expiresAt ? localInput(row.expiresAt) : '';
    el('cwPublished').value = String(!!row.isPublished);
    state.audiences = (row.audiences || []).map(a => ({ audienceTypeCode: a.audienceTypeCode,
        campusId: a.campusId, academicProgramId: a.academicProgramId, academicBatchId: a.academicBatchId, userId: a.userId }));
    renderAudiences(); el('cwNoticeForm').scrollIntoView({ behavior: 'smooth', block: 'start' });
}
async function save(event) {
    event.preventDefault(); if (state.saving) return;
    const date = el('cwPublishAt').value, expiry = el('cwExpiresAt').value;
    if (!date || !Number.isFinite(new Date(date).getTime()) ||
        (expiry && !Number.isFinite(new Date(expiry).getTime())) || !state.audiences.length) {
        alert('Enter valid dates and at least one audience.'); return;
    }
    const data = { title: el('cwTitle').value.trim(), body: el('cwBody').value.trim(),
        noticeCategoryId: el('cwCategory').value ? Number(el('cwCategory').value) : null,
        publishAt: new Date(date).toISOString(), expiresAt: expiry ? new Date(expiry).toISOString() : null,
        isPublished: el('cwPublished').value === 'true', audiences: state.audiences,
        rowVersion: el('cwVersion').value || null };
    const ref = el('cwReference').value;
    state.saving = true; el('cwSave').disabled = true;
    try {
        await api('/api/communication/notices' + (ref ? '/' + encodeURIComponent(ref) : ''),
            ref ? 'PUT' : 'POST', data);
        reset(); await loadNotices(edit); alert('Notice saved successfully.', true);
    } catch (error) { alert(error.message); }
    finally { state.saving = false; el('cwSave').disabled = false; }
}
export function initNoticeEditor() {
    el('cwNoticeForm').addEventListener('submit', save);
    el('cwReset').addEventListener('click', reset);
    initAudience(); reset();
    el('cwNoticeFilter').addEventListener('change', () => {
        state.noticePage = 1; loadNotices(edit).catch(error => alert(error.message));
    });
    api('/api/communication-admin/notice-categories').then(rows => {
        const select = el('cwCategory');
        for (const c of rows || []) if (c.isActive) select.add(new Option(c.name, c.id));
    }).catch(error => alert(error.message));
}
