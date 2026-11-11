(() => {
    'use strict';
    const root = document.getElementById('academicCalendarApp');
    if (!root) return;
    const el = id => document.getElementById(id);
    const canManage = root.dataset.canManage === 'true';
    const isTenantAdmin = root.dataset.tenantAdmin === 'true';
    const weekday = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
    const types = { 1: 'Academic', 2: 'Holiday', 3: 'Examination', 4: 'Admission', 5: 'Sports', 6: 'Cultural', 7: 'Meeting', 8: 'Other', 99: 'Other' };
    const state = { years: [], campuses: [], events: [], working: [], policy: null, editId: null,
        eventSaving: false, policySaving: false, loadSeq: 0, appliedYear: '', appliedCampus: '' };
    const identity = [document.body?.dataset?.authTenantId || '', document.body?.dataset?.authSessionReference || '', document.body?.dataset?.authUserId || ''];
    const sameSession = () => identity[0] === (document.body?.dataset?.authTenantId || '') &&
        identity[1] === (document.body?.dataset?.authSessionReference || '') &&
        identity[2] === (document.body?.dataset?.authUserId || '');
    const eventGuard = canManage && el('calendarEventForm') ? window.EduOSFormGuard?.create(el('calendarEventForm')) : null;
    const policyGuard = canManage && el('calendarPolicyForm') ? window.EduOSFormGuard?.create(el('calendarPolicyForm')) : null;
    const asDate = value => value ? String(value).slice(0, 10) : '';
    const toNum = value => value ? Number(value) : null;
    const option = (value, label) => { const x = document.createElement('option'); x.value = value; x.textContent = label; return x; };
    function alertMessage(kind, text) {
        if (!sameSession()) return;
        const box = el('calendarAlert');
        box.className = 'alert alert-' + kind;
        box.textContent = text; box.focus();
    }
    function clearAlert() { el('calendarAlert').className = 'd-none'; el('calendarAlert').textContent = ''; }
    async function api(url, method = 'GET', body) {
        if (!sameSession()) throw new Error('Session changed. Reload the page.');
        const csrf = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (method !== 'GET' && !csrf) throw new Error('Security token missing. Reload the page.');
        const response = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', ...(method === 'GET' ? {} : { RequestVerificationToken: csrf }), ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const payload = await response.json().catch(() => null);
        if (!sameSession()) throw new Error('Session changed. Reload the page.');
        if (!response.ok || !payload?.success) throw new Error(payload?.message || 'Unable to complete request.');
        return payload.data;
    }
    function setOptions(id, items, first) {
        const x = el(id); x.replaceChildren(option('', first));
        for (const item of items) x.append(option(item.id, item.name));
    }
    function addScopes(years, campuses) {
        const yMap = new Map(state.years.map(x => [Number(x.id), x]));
        for (const x of years) if (Number(x.id) > 0) yMap.set(Number(x.id), { ...yMap.get(Number(x.id)), ...x });
        state.years = [...yMap.values()].sort((a, b) => b.id - a.id);
        const cMap = new Map(state.campuses.map(x => [Number(x.id), x]));
        for (const x of campuses) if (Number(x.id) > 0) cMap.set(Number(x.id), { ...cMap.get(Number(x.id)), ...x });
        state.campuses = [...cMap.values()].sort((a, b) => a.name.localeCompare(b.name));
        const prevYear = el('calendarYear').value, prevCampus = el('calendarCampus').value;
        setOptions('calendarYear', state.years, 'Select academic year');
        setOptions('calendarCampus', state.campuses, 'Institution-wide');
        if (state.years.some(x => String(x.id) === prevYear)) el('calendarYear').value = prevYear;
        else if (state.years.length) el('calendarYear').value = String(state.years.find(x => x.isCurrent)?.id || state.years[0].id);
        if (state.campuses.some(x => String(x.id) === prevCampus)) el('calendarCampus').value = prevCampus;
        setYearDates();
        updateManageScope();
        state.appliedYear = el('calendarYear').value;
        state.appliedCampus = el('calendarCampus').value;
    }
    function updateManageScope() {
        const canWrite = sameSession() && canManage && !state.eventSaving && !state.policySaving &&
            (isTenantAdmin || !!el('calendarCampus').value);
        if (el('calendarNewEvent')) el('calendarNewEvent').disabled = !canWrite || !el('calendarYear').value;
        if (el('calendarSavePolicy')) el('calendarSavePolicy').disabled = !canWrite;
    }
    async function initScopes() {
        try {
            const data = await api('/api/academic-setup/catalog');
            if (!sameSession()) return;
            const batches = data?.batches || [];
            const years = [...new Map(batches.map(x => [Number(x.academicYearId), { id: Number(x.academicYearId), name: x.academicYearName || 'Year ' + x.academicYearId }])).values()];
            const campuses = [...new Map(batches.map(x => [Number(x.campusId), { id: Number(x.campusId), name: x.campusName || 'Campus ' + x.campusId }])).values()];
            addScopes(years, campuses);
        } catch (error) { alertMessage('warning', 'Academic catalog: ' + error.message); }
        if (root.dataset.canManage === 'true') {
            // The onboarding endpoints are available to TenantAdmin; other academic managers use the canonical batch catalog.
            const results = await Promise.allSettled([api('/api/institution-onboarding/academic-years'), api('/api/institution-onboarding/campus-list')]);
            if (!sameSession()) return;
            const years = results[0].status === 'fulfilled' ? (Array.isArray(results[0].value) ? results[0].value : (results[0].value?.items || [])) : [];
            const campuses = results[1].status === 'fulfilled' ? (Array.isArray(results[1].value) ? results[1].value : (results[1].value?.items || [])) : [];
            addScopes(years.map(x => ({ id: x.id, name: x.name, startDate: x.startDate, endDate: x.endDate, isCurrent: x.isCurrent })),
                campuses.map(x => ({ id: x.id, name: x.name })));
        }
        if (el('calendarYear').value) await load();
        else alertMessage('warning', 'No academic years available. Set up an academic year and academic batch first.');
    }
    function setYearDates() {
        const year = state.years.find(x => String(x.id) === el('calendarYear').value);
        const from = el('calendarFrom'), to = el('calendarTo');
        if (year?.startDate && year?.endDate) {
            from.min = asDate(year.startDate); from.max = asDate(year.endDate);
            to.min = from.min; to.max = from.max;
            if (!from.value || from.value < from.min || from.value > from.max) from.value = from.min;
            if (!to.value || to.value < to.min || to.value > to.max) to.value = to.max;
        } else {
            from.min = ''; from.max = ''; to.min = ''; to.max = '';
            const currentYear = new Date().getFullYear();
            const yearNumber = Number(String(year?.name || '').match(/\d{4}/)?.[0]) || currentYear;
            if (!from.value) from.value = yearNumber + '-01-01';
            if (!to.value) to.value = yearNumber + '-12-31';
        }
    }
    function scope() {
        const academicYearId = Number(el('calendarYear').value), campusId = toNum(el('calendarCampus').value);
        if (!academicYearId) throw new Error('Choose an academic year.');
        const fromDate = el('calendarFrom').value, toDate = el('calendarTo').value;
        if (!fromDate || !toDate || fromDate > toDate) throw new Error('Choose a valid date range.');
        if ((new Date(toDate + 'T00:00:00') - new Date(fromDate + 'T00:00:00')) / 86400000 > 366)
            throw new Error('Choose a date range of at most one academic year.');
        return { academicYearId, campusId, fromDate, toDate };
    }
    function params(s) {
        const q = new URLSearchParams({ academicYearId: String(s.academicYearId), fromDate: s.fromDate, toDate: s.toDate });
        if (s.campusId) q.set('campusId', String(s.campusId));
        return q;
    }
    function cell(value) { const x = document.createElement('td'); x.textContent = value === undefined || value === null ? '' : String(value); return x; }
    function renderEvents(items) {
        const target = el('calendarEvents'); target.replaceChildren();
        el('calendarCount').textContent = items.length + ' event(s)';
        el('calendarExportEvents').disabled = items.length === 0;
        if (!items.length) { const tr = document.createElement('tr'), td = cell('No events in this period.'); td.colSpan = canManage ? 6 : 5; td.className = 'text-center text-muted p-4'; tr.append(td); target.append(tr); return; }
        for (const ev of items) {
            const tr = document.createElement('tr');
            tr.append(cell(asDate(ev.startDate) + (asDate(ev.endDate) !== asDate(ev.startDate) ? ' – ' + asDate(ev.endDate) : '')),
                cell(ev.title), cell(types[ev.eventType] || 'Other'), cell(ev.isHoliday ? 'Yes' : 'No'), cell(ev.campusName || 'Institution-wide'));
            if (canManage) {
                const td = document.createElement('td');
                if (isTenantAdmin || ev.campusId) {
                    const button = document.createElement('button');
                    button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary'; button.textContent = 'Edit'; button.dataset.editId = ev.id;
                    td.append(button);
                }
                tr.append(td);
            }
            target.append(tr);
        }
    }
    function renderWorking(items) {
        state.working = items;
        el('calendarExportWorking').disabled = items.length === 0;
        const tbody = el('calendarWorkingDays'); tbody.replaceChildren();
        const working = items.filter(x => x.isWorkingDay).length;
        el('calendarWorkingSummary').textContent = working + ' working / ' + (items.length - working) + ' non-working days';
        for (const x of items) {
            const tr = document.createElement('tr'); tr.append(cell(asDate(x.date)), cell(weekday[new Date(asDate(x.date) + 'T12:00:00').getDay()]),
                cell(x.isWorkingDay ? 'Working' : (x.holidayNames?.join(', ') || (x.isWeekend ? 'Weekend' : 'Non-working'))));
            tbody.append(tr);
        }
    }
    function showPolicy(policy) {
        state.policy = policy || null;
        const days = policy ? [policy.weekendDay1, policy.weekendDay2].filter(x => x !== null && x !== undefined) : [];
        el('calendarPolicySummary').textContent = policy ? 'Weekend: ' + days.map(x => weekday[Number(x)] || x).join(', ') : 'No policy configured.';
        if (!policyGuard?.isDirty()) {
            document.querySelectorAll('input[name="weekendDay"]').forEach(x => x.checked = days.some(d => Number(d) === Number(x.value)));
            policyGuard?.markClean();
        }
    }
    async function load() {
        if (!sameSession()) return false;
        let s; try { s = scope(); } catch (e) { alertMessage('danger', e.message); return; }
        const seq = ++state.loadSeq, fingerprint = JSON.stringify(s);
        const current = () => {
            if (!sameSession() || seq !== state.loadSeq) return false;
            try { return JSON.stringify(scope()) === fingerprint; } catch { return false; }
        };
        clearAlert(); const b = el('calendarLoad'); b.disabled = true;
        try {
            const q = params(s);
            const [eventResult, workingResult] = await Promise.allSettled([
                api('/api/academic-calendars/events?' + q), api('/api/academic-calendars/working-days?' + q)
            ]);
            if (!current()) return false;
            state.events = eventResult.status === 'fulfilled' ? (eventResult.value || []) : [];
            renderEvents(state.events);
            renderWorking(workingResult.status === 'fulfilled' ? (workingResult.value || []) : []);
            if (eventResult.status === 'rejected') alertMessage('warning', 'Events: ' + eventResult.reason.message);
            if (workingResult.status === 'rejected') alertMessage('warning', 'Working days: ' + workingResult.reason.message);
            const pq = new URLSearchParams({ academicYearId: String(s.academicYearId) });
            if (s.campusId) pq.set('campusId', String(s.campusId));
            let policyOk = true;
            try {
                const policy = await api('/api/academic-calendars/policy?' + pq);
                if (!current()) return false;
                showPolicy(policy);
            } catch (e) {
                if (!current()) return false;
                if (e.message.includes('not configured')) showPolicy(null);
                else { policyOk = false; alertMessage('warning', 'Calendar loaded, but policy could not be loaded: ' + e.message); }
            }
            return eventResult.status === 'fulfilled' && workingResult.status === 'fulfilled' && policyOk;
        } catch (e) {
            if (!current()) return false;
            alertMessage('danger', e.message); renderEvents([]); renderWorking([]);
            return false;
        } finally { if (seq === state.loadSeq) b.disabled = false; }
    }
    function discardEditors() {
        if (state.eventSaving || state.policySaving || !sameSession()) return false;
        for (const guard of [eventGuard, policyGuard])
            if (guard?.isDirty() && !guard.confirmDiscard()) return false;
        eventGuard?.markClean(); policyGuard?.markClean();
        return true;
    }
    function changeScope(kind) {
        const select = el(kind === 'year' ? 'calendarYear' : 'calendarCampus');
        const previous = kind === 'year' ? state.appliedYear : state.appliedCampus;
        if (!discardEditors()) { select.value = previous; return; }
        closeEditor(true);
        if (kind === 'year') { el('calendarFrom').value = ''; el('calendarTo').value = ''; setYearDates(); showPolicy(null); }
        else { updateManageScope(); showPolicy(null); }
        state.appliedYear = el('calendarYear').value;
        state.appliedCampus = el('calendarCampus').value;
        ++state.loadSeq;
        el('calendarLoad').disabled = false;
    }
    function openEditor(ev) {
        if (!canManage || !sameSession() || state.eventSaving || state.policySaving ||
            (!isTenantAdmin && !(ev?.campusId || toNum(el('calendarCampus').value)))) return;
        if (eventGuard?.isDirty() && !eventGuard.confirmDiscard()) return;
        state.editId = ev?.id || null;
        el('calendarEditorTitle').textContent = ev ? 'Edit calendar event' : 'New calendar event';
        el('calendarEventTitle').value = ev?.title || '';
        const eventType = Number(ev?.eventType || 1);
        el('calendarEventType').value = String(eventType);
        el('calendarEventStart').value = asDate(ev?.startDate) || el('calendarFrom').value;
        el('calendarEventEnd').value = asDate(ev?.endDate) || el('calendarFrom').value;
        el('calendarEventDescription').value = ev?.description || '';
        el('calendarEventHoliday').checked = !!ev?.isHoliday;
        el('calendarEventVisible').checked = ev?.isPublicVisible !== false;
        eventGuard?.markClean();
        el('calendarEditorPanel').classList.remove('d-none');
        el('calendarEventTitle').focus();
    }
    function closeEditor(force = false) {
        if (!force && (state.eventSaving || !sameSession() ||
            (eventGuard?.isDirty() && !eventGuard.confirmDiscard()))) return false;
        state.editId = null;
        eventGuard?.markClean();
        el('calendarEditorPanel')?.classList.add('d-none');
        return true;
    }
    async function saveEvent(e) {
        e.preventDefault();
        if (state.eventSaving || state.policySaving || !sameSession() || !e.currentTarget.reportValidity()) return;
        const selected = state.events.find(x => Number(x.id) === Number(state.editId));
        let s; try { s = scope(); } catch (error) { alertMessage('danger', error.message); return; }
        const startDate = el('calendarEventStart').value, endDate = el('calendarEventEnd').value;
        const year = state.years.find(x => Number(x.id) === s.academicYearId);
        if (startDate > endDate || (year?.startDate && startDate < asDate(year.startDate)) ||
            (year?.endDate && endDate > asDate(year.endDate))) {
            alertMessage('danger', 'Event dates must lie within the academic year.'); return;
        }
        if (state.editId && !selected) { alertMessage('danger', 'Reload the event before editing.'); return; }
        const body = { academicYearId: selected?.academicYearId || s.academicYearId,
            academicTermId: selected?.academicTermId ?? null,
            campusId: selected ? selected.campusId : s.campusId,
            title: el('calendarEventTitle').value.trim(), eventType: Number(el('calendarEventType').value),
            description: el('calendarEventDescription').value.trim() || null,
            startDate, endDate, isHoliday: el('calendarEventHoliday').checked, isPublicVisible: el('calendarEventVisible').checked };
        if (!body.title) { alertMessage('danger', 'Event title is required.'); return; }
        if (state.editId) { body.rowVersion = selected?.rowVersion; if (!body.rowVersion) { alertMessage('danger', 'Reload this event before editing.'); return; } }
        const button = el('calendarSaveEvent');
        state.eventSaving = true; button.disabled = true; e.currentTarget.inert = true;
        el('calendarFilters').inert = true; updateManageScope();
        try {
            await api(state.editId ? '/api/academic-calendars/events/' + state.editId : '/api/academic-calendars/events', 'POST', body);
            if (!sameSession()) return;
            eventGuard?.markClean(); closeEditor(true);
            const refreshed = await load();
            alertMessage(refreshed ? 'success' : 'warning', refreshed ? 'Calendar event saved.' :
                'Calendar event saved, but the calendar could not be refreshed. Reload before making another change.');
        } catch (error) {
            if (sameSession()) alertMessage('danger', error.message + ' Check the current event register before retrying; your draft remains.');
        } finally {
            state.eventSaving = false; button.disabled = false; e.currentTarget.inert = false;
            el('calendarFilters').inert = false; updateManageScope();
        }
    }
    async function savePolicy(event) {
        event.preventDefault();
        if (state.policySaving || state.eventSaving || !sameSession() || !event.currentTarget.reportValidity()) return;
        let s; try { s = scope(); } catch (error) { alertMessage('danger', error.message); return; }
        if (!isTenantAdmin && !s.campusId) { alertMessage('danger', 'Choose an authorized campus to change its weekend policy.'); return; }
        const days = [...document.querySelectorAll('input[name="weekendDay"]:checked')].map(x => Number(x.value));
        if (days.length < 1 || days.length > 2) { alertMessage('danger', 'Select one or two weekend days.'); return; }
        const button = el('calendarSavePolicy');
        state.policySaving = true; button.disabled = true; event.currentTarget.inert = true;
        el('calendarFilters').inert = true; updateManageScope();
        try {
            const saved = await api('/api/academic-calendars/policy', 'POST', {
                campusId: s.campusId, weekendDay1: days[0], weekendDay2: days[1] ?? null,
                isActive: true, rowVersion: state.policy?.campusId === s.campusId ? state.policy.rowVersion : null });
            if (!sameSession()) return;
            policyGuard?.markClean(); showPolicy(saved);
            const refreshed = await load();
            alertMessage(refreshed ? 'success' : 'warning', refreshed ? 'Weekend policy saved.' :
                'Weekend policy saved, but the calendar could not be refreshed. Reload before another change.');
        } catch (error) {
            if (sameSession()) alertMessage('danger', error.message + ' Check the current policy before retrying; your selections remain.');
        } finally {
            state.policySaving = false; button.disabled = false; event.currentTarget.inert = false;
            el('calendarFilters').inert = false; updateManageScope();
        }
    }
    el('calendarFilters').addEventListener('submit', e => { e.preventDefault(); load(); });
    el('calendarYear').addEventListener('change', () => changeScope('year'));
    el('calendarCampus').addEventListener('change', () => changeScope('campus'));
    el('calendarEvents').addEventListener('click', e => { const button = e.target.closest('[data-edit-id]'); if (button) openEditor(state.events.find(x => String(x.id) === button.dataset.editId)); });
    el('calendarNewEvent')?.addEventListener('click', () => openEditor(null));
    el('calendarCancelEvent')?.addEventListener('click', () => closeEditor());
    el('calendarEventForm')?.addEventListener('submit', saveEvent);
    el('calendarPolicyForm')?.addEventListener('submit', savePolicy);
    function exportCsv(filename, headers, rows) {
        if (!sameSession()) return;
        const safe = value => {
            const raw = String(value ?? '');
            const escaped = /^[=+@-]/.test(raw.trimStart()) ? "'" + raw : raw;
            return '"' + escaped.replace(/"/g, '""') + '"';
        };
        const csv = [headers, ...rows].map(row => row.map(safe).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF' + csv], { type: 'text/csv;charset=utf-8' }));
        const anchor = document.createElement('a'); anchor.href = url; anchor.download = filename;
        document.body.append(anchor); anchor.click(); anchor.remove(); URL.revokeObjectURL(url);
    }
    el('calendarExportEvents').addEventListener('click', () => exportCsv('academic-calendar-events.csv',
        ['Start', 'End', 'Event', 'Type', 'Holiday', 'Campus', 'Visible'],
        state.events.map(x => [asDate(x.startDate), asDate(x.endDate), x.title, types[x.eventType] || 'Other',
            x.isHoliday ? 'Yes' : 'No', x.campusName || 'Institution-wide', x.isPublicVisible ? 'Yes' : 'No'])));
    el('calendarExportWorking').addEventListener('click', () => exportCsv('academic-working-days.csv',
        ['Date', 'Day', 'Working', 'Weekend', 'Holiday names'],
        state.working.map(x => [asDate(x.date), weekday[new Date(asDate(x.date) + 'T12:00:00').getDay()],
            x.isWorkingDay ? 'Yes' : 'No', x.isWeekend ? 'Yes' : 'No', (x.holidayNames || []).join('; ')])));
    initScopes();
})();
