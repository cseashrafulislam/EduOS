(function (root) {
    'use strict';
    function recover(requirements, documents) {
        const latest = new Map();
        for (const document of documents || []) {
            const type = String(document?.documentTypeCode || '').trim().toUpperCase();
            if (!type) continue;
            const version = Number(document.versionNo) || 0;
            if (!latest.has(type) || version > (Number(latest.get(type).versionNo) || 0))
                latest.set(type, document);
        }
        const completedTypes = [];
        for (const requirement of requirements || []) {
            const type = String(requirement?.documentType || '').trim().toUpperCase();
            if (!type) continue;
            const current = latest.get(type);
            // Rejected latest versions need replacement; pending and verified are received.
            if (current && Number(current.verificationStatus) !== 3)
                completedTypes.push(type);
        }
        return { completedTypes };
    }
    const api = { recover };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    if (root) root.EduOSAdmissionDocuments = api;
})(typeof globalThis !== 'undefined' ? globalThis : null);
