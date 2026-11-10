// Reuse a request key after an uncertain response only while the command is identical.
// The stock API remains responsible for authoritative idempotency and concurrency checks.
export function commandKey() {
    let signature = '', key = '';
    return {
        for: (operation, body) => {
            const current = JSON.stringify([operation, body]);
            if (current !== signature) { signature = current; key = crypto.randomUUID(); }
            return key;
        },
        clear: () => { signature = ''; key = ''; }
    };
}

export async function refreshAfterCommit(refresh, message, notice) {
    try { await refresh(); notice(message, true); }
    catch (error) { notice(message + ' The list could not be refreshed: ' + error.message, true); }
}
