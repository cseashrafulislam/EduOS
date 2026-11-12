export function setupGuardianSearch({ $, api, state, render, notice }) {
    $('guardianSearchForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (state.busy || state.selecting || !state.sameSession()) return;
        ++state.selectionVersion;
        const search = $('guardianSearch').value.trim();
        if (search.length < 2) return notice('Enter at least two characters.');
        const list = $('guardianMatches');
        const request = ++state.searchVersion;
        list.replaceChildren();
        try {
            const data = await api('/api/students?' +
                new URLSearchParams({ search, page: 1, pageSize: 20 }));
            if (request !== state.searchVersion || !state.sameSession()) return;
            list.replaceChildren();
            for (const item of data.items || []) {
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-outline-secondary m-1';
                button.textContent = item.studentCode + ' — ' + item.fullName;
                button.addEventListener('click', async () => {
                    if (state.busy || state.selecting || !state.sameSession() || !state.confirmStudentSwitch()) return;
                    const selection = ++state.selectionVersion;
                    state.setSelecting(true);
                    try {
                        const student = await api('/api/student-guardians/students/' + encodeURIComponent(item.reference));
                        if (selection !== state.selectionVersion || !state.sameSession()) return;
                        if (student?.studentReference !== item.reference) throw Error('Selected student did not match the response.');
                        state.requiresRefresh = false;
                        render(student);
                        notice('Student selected.');
                    } catch (error) {
                        if (selection === state.selectionVersion && state.sameSession()) notice(error.message, true);
                    } finally { if (selection === state.selectionVersion) state.setSelecting(false); }
                });
                list.append(button);
            }
            if (!list.childElementCount) notice('No matching students.');
        } catch (error) { if (request === state.searchVersion && state.sameSession()) notice(error.message, true); }
    });
    $('guardianSearch').addEventListener('input', () => {
        ++state.searchVersion;
        ++state.selectionVersion;
        $('guardianMatches').replaceChildren();
    });
}
