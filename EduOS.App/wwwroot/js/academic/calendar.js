(() => {
    'use strict';
    const root = document.getElementById('academicCalendarApp');
    if (!root) return;
    const el = id => document.getElementById(id);
    const canManage = root.dataset.canManage === 'true';
    const weekday = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
    const types = { 1: 'Academic', 2: 'Holiday', 3: 'Examination', 4: 'Admission', 5: 'Sports', 6: 'Cultural', 7: 'Meeting', 8: 'Other', 99: 'Other' };
    const state = { years: [], campuses: [], events: [], policy: null, editId: null, busy: false };
    const asDate = value => value ? String(value).slice(0, 10) : '';
    const iso = value => value + 'T00:00:00';
    const toNum = value => value ? Number(value) : null;
    const option = (value, label) => { const x = document.createElement('option'); x.value = value; x.textContent = label; return x; };
    function alertMessage(kind, text) {
        const box = el('calendarAlert');
        box.className = 'alert alert-' + kind;
        box.textContent = text; box.focus();
    }
    function clearAlert() { el('calendarAlert').className = 'd-none'; el('calendarAlert').textContent = ''; }
    async function api(url, method = 'GET', body) {
        const response = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const payload = await response.json().catch(() => null);
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
        if (el('calendarNewEvent')) el('calendarNewEvent').disabled = !el('calendarYear').value;
    }
    async function initScopes() {
        try {
            const data = await api('/api/academic-setup/catalog');
            const batches = data?.batches || [];
            const years = [...new Map(batches.map(x => [Number(x.academicYearId), { id: Number(x.academicYearId), name: x.academicYearName || 'Year ' + x.academicYearId }])).values()];
            const campuses = [...new Map(batches.map(x => [Number(x.campusId), { id: Number(x.campusId), name: x.campusName || 'Campus ' + x.campusId }])).values()];
            addScopes(years, campuses);
        } catch (error) { alertMessage('warning', 'Academic catalog: ' + error.message); }
        if (root.dataset.canManage === 'true') {
            // The onboarding endpoints are available to TenantAdmin; other academic managers use the canonical batch catalog.
            const results = await Promise.allSettled([api('/api/institution-onboarding/academic-years'), api('/api/institution-onboarding/campus-list')]);
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
        const q = new URLSearchParams({ academicYearId: String(s.academicYearId), fromDate: iso(s.fromDate), toDate: iso(s.toDate) });
        if (s.campusId) q.set('campusId', String(s.campusId));
        return q;
    }
    function cell(value) { const x = document.createElement('td'); x.textContent = value === undefined || value === null ? '' : String(value); return x; }
    function renderEvents(items) {
        const target = el('calendarEvents'); target.replaceChildren();
        el('calendarCount').textContent = items.length + ' event(s)';
        if (!items.length) { const tr = document.createElement('tr'), td = cell('No events in this period.'); td.colSpan = canManage ? 6 : 5; td.className = 'text-center text-muted p-4'; tr.append(td); target.append(tr); return; }
        for (const ev of items) {
            const tr = document.createElement('tr');
            tr.append(cell(asDate(ev.startDate) + (asDate(ev.endDate) !== asDate(ev.startDate) ? ' – ' + asDate(ev.endDate) : '')),
                cell(ev.title), cell(types[ev.eventType] || 'Other'), cell(ev.isHoliday ? 'Yes' : 'No'), cell(ev.campusName || 'Institution-wide'));
            if (canManage) {
                const td = document.createElement('td'), button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary'; button.textContent = 'Edit'; button.dataset.editId = ev.id;
                td.append(button); tr.append(td);
            }
            target.append(tr);
        }
    }
    function renderWorking(items) {
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
        document.querySelectorAll('input[name="weekendDay"]').forEach(x => x.checked = days.some(d => Number(d) === Number(x.value)));
    }
    async function load() {
        let s; try { s = scope(); } catch (e) { alertMessage('danger', e.message); return; }
        clearAlert(); const b = el('calendarLoad'); b.disabled = true;
        try {
            const q = params(s);
            const [events, working] = await Promise.all([api('/api/academic-calendars/events?' + q), api('/api/academic-calendars/working-days?' + q)]);
            state.events = events || []; renderEvents(state.events); renderWorking(working || []);
            const pq = new URLSearchParams({ academicYearId: String(s.academicYearId) });
            if (s.campusId) pq.set('campusId', String(s.campusId));
            try { showPolicy(await api('/api/academic-calendars/policy?' + pq)); }
            catch (e) { if (e.message.includes('not configured')) showPolicy(null); else alertMessage('warning', 'Calendar loaded, but policy could not be loaded: ' + e.message); }
        } catch (e) { alertMessage('danger', e.message); renderEvents([]); renderWorking([]); }
        finally { b.disabled = false; }
    }
    function openEditor(ev) {
        if (!canManage) return;
        state.editId = ev?.id || null;
        el('calendarEditorTitle').textContent = ev ? 'Edit calendar event' : 'New calendar event';
        el('calendarEventTitle').value = ev?.title || '';
        const eventType = Number(ev?.eventType || 1);
        el('calendarEventType').value = eventType === 8 ? '99' : String(eventType);
        el('calendarEventStart').value = asDate(ev?.startDate) || el('calendarFrom').value;
        el('calendarEventEnd').value = asDate(ev?.endDate) || el('calendarFrom').value;
        el('calendarEventDescription').value = ev?.description || '';
        el('calendarEventHoliday').checked = !!ev?.isHoliday;
        el('calendarEventVisible').checked = ev?.isPublicVisible !== false;
        el('calendarEditorPanel').classList.remove('d-none');
        el('calendarEventTitle').focus();
    }
    function closeEditor() { state.editId = null; el('calendarEditorPanel')?.classList.add('d-none'); }
    async function saveEvent(e) {
        e.preventDefault(); if (!e.currentTarget.reportValidity()) return;
        const selected = state.events.find(x => Number(x.id) === Number(state.editId));
        const s = scope(), startDate = el('calendarEventStart').value, endDate = el('calendarEventEnd').value;
        if (startDate > endDate || startDate < s.fromDate || endDate > s.toDate) {
            alertMessage('danger', 'Event dates must lie within the selected reporting period.'); return;
        }
        const body = { title: el('calendarEventTitle').value.trim(), eventType: Number(el('calendarEventType').value),
            description: el('calendarEventDescription').value.trim() || null,
            startDate: iso(startDate), endDate: iso(endDate), isHoliday: el('calendarEventHoliday').checked, isPublicVisible: el('calendarEventVisible').checked };
        if (!body.title) { alertMessage('danger', 'Event title is required.'); return; }
        if (state.editId) { body.isActive = true; body.rowVersion = selected?.rowVersion; if (!body.rowVersion) { alertMessage('danger', 'Reload this event before editing.'); return; } }
        else { body.clientRequestId = crypto.randomUUID(); body.academicYearId = s.academicYearId; body.campusId = s.campusId; body.academicTermId = null; }
        const button = el('calendarSaveEvent'); button.disabled = true;
        try {
            await api(state.editId ? '/api/academic-calendars/events/' + state.editId : '/api/academic-calendars/events', 'POST', body);
            closeEditor(); await load(); alertMessage('success', 'Calendar event saved.');
        } catch (error) { alertMessage('danger', error.message); }
        finally { button.disabled = false; }
    }
    async function savePolicy(event) {
        event.preventDefault();
        let s; try { s = scope(); } catch (error) { alertMessage('danger', error.message); return; }
        const days = [...document.querySelectorAll('input[name="weekendDay"]:checked')].map(x => Number(x.value));
        if (days.length < 1 || days.length > 2) { alertMessage('danger', 'Select one or two weekend days.'); return; }
        const button = el('calendarSavePolicy'); button.disabled = true;
        try {
            const saved = await api('/api/academic-calendars/policy', 'POST', { clientRequestId: crypto.randomUUID(),
                academicYearId: s.academicYearId, campusId: s.campusId, weekendDays: days, rowVersion: state.policy?.rowVersion || null });
            showPolicy(saved); await load(); alertMessage('success', 'Weekend policy saved.');
        } catch (error) { alertMessage('danger', error.message); }
        finally { button.disabled = false; }
    }
    el('calendarFilters').addEventListener('submit', e => { e.preventDefault(); load(); });
    el('calendarYear').addEventListener('change', () => { closeEditor(); el('calendarFrom').value = ''; el('calendarTo').value = ''; setYearDates(); });
    el('calendarCampus').addEventListener('change', closeEditor);
    el('calendarEvents').addEventListener('click', e => { const button = e.target.closest('[data-edit-id]'); if (button) openEditor(state.events.find(x => String(x.id) === button.dataset.editId)); });
    el('calendarNewEvent')?.addEventListener('click', () => openEditor(null));
    el('calendarCancelEvent')?.addEventListener('click', closeEditor);
    el('calendarEventForm')?.addEventListener('submit', saveEvent);
    el('calendarPolicyForm')?.addEventListener('submit', savePolicy);
    initScopes();
})();
