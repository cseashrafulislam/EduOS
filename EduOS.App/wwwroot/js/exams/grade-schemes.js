(() => {
    'use strict';
    const root = document.getElementById('gradeSchemeWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { page: 1, pageSize: 20, total: 0, rows: [], busy: false };
    function notice(message, error = false) {
        const box = el('gradeSchemeNotice');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message;
    }
    async function api(path, method = 'GET', body) {
        const response = await fetch(path, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json',
                ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
                ...(method === 'GET' ? {} : { RequestVerificationToken: token }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.success !== true) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    const cell = value => {
        const td = document.createElement('td');
        td.textContent = value == null ? '' : String(value);
        return td;
    };
    function render() {
        const body = el('gradeSchemeRows'); body.replaceChildren();
        for (const row of state.rows) {
            const tr = document.createElement('tr');
            tr.append(cell(row.code), cell(row.name), cell(row.academicProgramId || 'Tenant-wide'),
                cell(row.ruleCount), cell(row.isActive ? 'Yes' : 'No'),
                cell(row.isDefault ? 'Yes' : 'No'));
            const action = document.createElement('td'); action.className = 'grade-scheme-action';
            const button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
            button.textContent = 'View rules'; button.disabled = state.busy;
            button.addEventListener('click', () => details(row.id));
            action.append(button); tr.append(action); body.append(tr);
        }
        if (!state.rows.length) {
            const tr = document.createElement('tr'), td = cell('No grade schemes found.');
            td.colSpan = 7; tr.append(td); body.append(tr);
        }
        el('gradeSchemeCount').textContent = state.total + ' scheme(s) · Page ' + state.page;
        el('gradeSchemePrev').disabled = state.page <= 1;
        el('gradeSchemeNext').disabled = state.page * state.pageSize >= state.total;
    }
    async function load() {
        const params = new URLSearchParams({
            page: String(state.page), pageSize: String(state.pageSize),
            search: el('gradeSchemeSearch').value.trim(),
            activeOnly: String(el('gradeSchemeActiveOnly').checked)
        });
        try {
            const data = await api('/api/exams/workflow/grade-schemes?' + params);
            state.rows = data.items || []; state.total = data.totalCount || 0;
            render();
        } catch (error) { notice(error.message, true); }
    }
    async function details(id) {
        try {
            const data = await api('/api/exams/workflow/grade-schemes/' + encodeURIComponent(id));
            el('gradeSchemeDetailTitle').textContent = data.code + ' — ' + data.name;
            const body = el('gradeSchemeDetailRules'); body.replaceChildren();
            for (const rule of data.rules || []) {
                const tr = document.createElement('tr');
                tr.append(cell(rule.minMarks), cell(rule.maxMarks), cell(rule.gradeLetter),
                    cell(rule.gradePoint), cell(rule.isFailGrade ? 'Yes' : 'No'));
                body.append(tr);
            }
            el('gradeSchemeDetail').classList.remove('d-none');
            el('gradeSchemeDetail').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        } catch (error) { notice(error.message, true); }
    }
    function addRule() {
        const body = el('gradeSchemeRuleEditor');
        if (body.children.length >= 100) { notice('Maximum 100 grade intervals.', true); return; }
        const tr = document.createElement('tr');
        const fields = [
            ['minMarks', 'number', '0', '100', '0.01'],
            ['maxMarks', 'number', '0', '100', '0.01'],
            ['gradeLetter', 'text', '', '', ''],
            ['gradePoint', 'number', '0', '10', '0.01']
        ];
        for (const [name, type, min, max, step] of fields) {
            const td = document.createElement('td'), input = document.createElement('input');
            input.className = 'form-control form-control-sm'; input.dataset.field = name;
            input.type = type; input.required = true;
            if (type === 'text') input.maxLength = 20;
            else { input.min = min; input.max = max; input.step = step; }
            td.append(input); tr.append(td);
        }
        const fail = document.createElement('td'), check = document.createElement('input');
        check.type = 'checkbox'; check.className = 'form-check-input'; check.dataset.field = 'isFailGrade';
        fail.append(check); tr.append(fail);
        const action = document.createElement('td'), remove = document.createElement('button');
        remove.type = 'button'; remove.className = 'btn btn-sm btn-outline-danger';
        remove.textContent = 'Remove'; remove.addEventListener('click', () => tr.remove());
        action.append(remove); tr.append(action); body.append(tr);
    }
    function collectRules() {
        const rows = [...el('gradeSchemeRuleEditor').children];
        if (!rows.length || rows.length > 100) throw new Error('Add 1–100 grade intervals.');
        const rules = rows.map((tr, index) => {
            const field = name => tr.querySelector('[data-field="' + name + '"]');
            const letter = field('gradeLetter').value.trim().toUpperCase();
            const minMarks = Number(field('minMarks').value);
            const maxMarks = Number(field('maxMarks').value);
            const gradePoint = Number(field('gradePoint').value);
            if (!field('minMarks').value || !field('maxMarks').value ||
                !field('gradePoint').value || !letter || letter.length > 20 ||
                ![minMarks, maxMarks, gradePoint].every(Number.isFinite) ||
                [minMarks, maxMarks, gradePoint].some(x => Math.abs(Math.round(x * 100) - x * 100) > 1e-7) ||
                minMarks < 0 || maxMarks > 100 || minMarks > maxMarks ||
                gradePoint < 0 || gradePoint > 10)
                throw new Error('Invalid interval, grade point or letter in row ' + (index + 1) + '.');
            return { minMarks, maxMarks, gradeLetter: letter, gradePoint,
                isFailGrade: field('isFailGrade').checked, displayOrder: index + 1 };
        }).sort((a, b) => a.minMarks - b.minMarks);
        if (rules[0].minMarks !== 0 || rules[rules.length - 1].maxMarks !== 100 ||
            new Set(rules.map(x => x.gradeLetter)).size !== rules.length ||
            rules.slice(1).some((x, i) => Math.round(x.minMarks * 100) !== Math.round(rules[i].maxMarks * 100) + 1))
            throw new Error('Intervals must cover 0.00–100.00 with no gaps/overlaps and unique letters.');
        return rules;
    }
    async function save(event) {
        event.preventDefault();
        if (state.busy) return;
        let body;
        try {
            const program = el('gradeSchemeProgram').value.trim();
            if (program && (!/^[1-9]\d*$/.test(program) || !Number.isSafeInteger(Number(program))))
                throw new Error('Program ID must be a positive whole number.');
            body = {
                code: el('gradeSchemeCode').value.trim(),
                name: el('gradeSchemeName').value.trim(),
                academicProgramId: program ? Number(program) : null,
                isActive: el('gradeSchemeActive').checked,
                isDefault: el('gradeSchemeDefault').checked,
                rules: collectRules()
            };
            if (!body.code || !body.name || body.code.length > 50 || body.name.length > 150)
                throw new Error('Enter a valid name and code.');
        } catch (error) { notice(error.message, true); return; }
        state.busy = true; el('gradeSchemeSave').disabled = true;
        try {
            await api('/api/exams/workflow/grade-schemes', 'POST', body);
            notice('Grade scheme created. To amend it, create a new code.');
            clear(); state.page = 1; await load();
        } catch (error) {
            notice(error.message + ' Check the register before retrying; a successful request may already exist.', true);
            await load();
        } finally { state.busy = false; el('gradeSchemeSave').disabled = false; }
    }
    function clear() {
        el('gradeSchemeForm').reset();
        el('gradeSchemeRuleEditor').replaceChildren();
        addRule();
        el('gradeSchemeActive').checked = true;
    }
    function csv() {
        const safe = value => /^[=+@\-\t\r]/.test(String(value ?? '')) ? "'" + String(value) : value;
        const quote = value => '"' + String(safe(value) ?? '').replace(/"/g, '""') + '"';
        const rows = [['Code', 'Name', 'Program', 'Rules', 'Active', 'Default'],
            ...state.rows.map(x => [x.code, x.name, x.academicProgramId || 'Tenant-wide',
                x.ruleCount, x.isActive ? 'Yes' : 'No', x.isDefault ? 'Yes' : 'No'])];
        const data = rows.map(row => row.map(quote).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF' + data], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a'); a.href = url;
        a.download = 'grade-schemes-page-' + state.page + '.csv';
        document.body.append(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    el('gradeSchemeForm').addEventListener('submit', save);
    el('gradeSchemeClear').addEventListener('click', clear);
    el('gradeSchemeAddRule').addEventListener('click', addRule);
    el('gradeSchemeRefresh').addEventListener('click', () => { state.page = 1; load(); });
    el('gradeSchemePrev').addEventListener('click', () => { if (state.page > 1) { state.page--; load(); } });
    el('gradeSchemeNext').addEventListener('click', () => { if (state.page * state.pageSize < state.total) { state.page++; load(); } });
    el('gradeSchemeCsv').addEventListener('click', csv);
    el('gradeSchemePrint').addEventListener('click', () => window.print());
    clear(); load();
})();
