export function setupGuardianSearch({ $, api, state, render, notice }) {
    $('guardianSearchForm').addEventListener('submit', async event => {
        event.preventDefault();
        const search = $('guardianSearch').value.trim();
        if (search.length < 2) return notice('Enter at least two characters.');
        const list = $('guardianMatches');
        try {
            const data = await api('/api/students?' +
                new URLSearchParams({ search, page: 1, pageSize: 20 }));
            list.replaceChildren();
            for (const item of data.items || []) {
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-outline-secondary m-1';
                button.textContent = item.studentCode + ' — ' + item.fullName;
                button.addEventListener('click', async () => {
                    try {
                        state.requestId = null;
                        render(await api('/api/student-guardians/students/' + encodeURIComponent(item.reference)));
                        notice('Student selected.');
                    } catch (error) { notice(error.message); }
                });
                list.append(button);
            }
            if (!list.childElementCount) notice('No matching students.');
        } catch (error) { notice(error.message); }
    });
}
