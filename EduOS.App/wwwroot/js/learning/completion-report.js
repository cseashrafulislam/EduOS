/* Bounded, filtered LMS completion register export. Authorization stays in the server API. */
(function (root, factory) {
    'use strict';
    const report = factory();
    if (typeof module === 'object' && module.exports) module.exports = report;
    else root.EduOSLmsCompletionReport = report;
})(typeof globalThis === 'object' ? globalThis : this, function () {
    'use strict';
    const PAGE_SIZE = 25;
    const MAX_ROWS = 250;
    const columns = ['Student code', 'Student name', 'State', 'Lessons', 'Assignments', 'Quizzes', 'Eligibility'];

    async function collect(fetchPage, isCurrent = () => true) {
        if (typeof fetchPage !== 'function' || typeof isCurrent !== 'function')
            throw new Error('Invalid report request.');
        const rows = [], ids = new Set();
        let expectedTotal = null;
        for (let page = 1; page <= Math.ceil(MAX_ROWS / PAGE_SIZE); page++) {
            if (!isCurrent()) throw new Error('Course, filter or account changed. Restart the export.');
            const data = await fetchPage(page, PAGE_SIZE);
            if (!isCurrent()) throw new Error('Course, filter or account changed. Restart the export.');
            if (!data || !Array.isArray(data.items) || !Number.isSafeInteger(data.totalCount) ||
                data.totalCount < 0 || data.items.length > PAGE_SIZE)
                throw new Error('The completion register returned an invalid page.');
            if (data.totalCount > MAX_ROWS)
                throw new Error('More than 250 matching enrollments. Narrow the student search before exporting.');
            if (expectedTotal === null) expectedTotal = data.totalCount;
            if (expectedTotal !== data.totalCount)
                throw new Error('The completion register changed during export. Retry.');
            for (const row of data.items) {
                const id = String(row?.enrollmentId ?? '');
                if (!/^[1-9]\d*$/.test(id) || ids.has(id) || rows.length >= expectedTotal)
                    throw new Error('The completion register changed during export. Retry.');
                ids.add(id);
                rows.push(row);
            }
            if (rows.length === expectedTotal) return rows;
            if (data.items.length !== PAGE_SIZE)
                throw new Error('The completion register changed during export. Retry.');
        }
        throw new Error('The completion register exceeded the export limit. Narrow the search.');
    }

    function validateSnapshot(data, isCurrent = () => true) {
        if (!isCurrent()) throw new Error('Course, filter or account changed. Restart the export.');
        if (!data || !Array.isArray(data.items) || !Number.isSafeInteger(data.totalCount) ||
            data.totalCount < 0 || data.totalCount > MAX_ROWS || data.page !== 1 ||
            data.pageSize !== MAX_ROWS || data.items.length !== data.totalCount)
            throw new Error('The completion register changed or exceeded the export limit. Narrow the search and retry.');
        const ids = new Set();
        for (const row of data.items) {
            const id = String(row?.enrollmentId ?? '');
            if (!/^[1-9]\d*$/.test(id) || ids.has(id))
                throw new Error('The completion register contains duplicate or invalid enrollments.');
            ids.add(id);
        }
        if (!isCurrent()) throw new Error('Course, filter or account changed. Restart the export.');
        return data.items;
    }

    function csvCell(value) {
        let text = String(value ?? '');
        // Spreadsheet formula injection can begin after leading whitespace/control characters.
        if (/^[\s\u0000-\u001f]*[=+\-@]/u.test(text)) text = "'" + text;
        return '"' + text.replaceAll('"', '""') + '"';
    }

    function toCsv(rows) {
        const lines = [columns];
        for (const row of rows) {
            const e = row.eligibility || {};
            const state = row.state === 2 ? 'Completed' : row.state === 1 ? 'Active' : 'Inactive';
            lines.push([
                row.studentCode, row.studentName, state,
                `${e.completedLessons || 0}/${e.requiredLessons || 0}`,
                `${e.gradedAssignments || 0}/${e.requiredAssignments || 0}`,
                `${e.passedQuizzes || 0}/${e.requiredQuizzes || 0}`,
                row.state === 2 ? 'Completed' : e.ready ? 'Ready' : (e.blockers || []).join(' ')
            ]);
        }
        return '\uFEFF' + lines.map(row => row.map(csvCell).join(',')).join('\r\n');
    }
    function validateLessonSnapshot(data, enrollmentId, isCurrent = () => true) {
        if (!isCurrent()) throw new Error('Course, learner or account changed. Restart the export.');
        if (!data || !Array.isArray(data.items) || !Number.isSafeInteger(data.totalCount) ||
            data.totalCount < 0 || data.totalCount > MAX_ROWS || data.page !== 1 ||
            data.pageSize !== MAX_ROWS || data.items.length !== data.totalCount)
            throw new Error('The lesson report changed or exceeded 250 rows.');
        const seen = new Set();
        for (const row of data.items) {
            const id = String(row?.lessonReference ?? '');
            if (!/^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(id) ||
                String(row?.courseEnrollmentId) !== String(enrollmentId) ||
                seen.has(id) || !Number.isFinite(Number(row.progressPercent)) ||
                Number(row.progressPercent) < 0 || Number(row.progressPercent) > 100)
                throw new Error('The lesson report contains invalid or duplicate evidence.');
            seen.add(id);
        }
        if (!isCurrent()) throw new Error('Course, learner or account changed. Restart the export.');
        return data.items;
    }

    function toLessonCsv(rows) {
        const lines = [['Lesson', 'Progress (%)', 'Completed', 'Completed at (UTC)']];
        for (const row of rows) lines.push([
            row.lessonTitle, row.progressPercent, row.isCompleted ? 'Yes' : 'No',
            row.completedAt || ''
        ]);
        return '\uFEFF' + lines.map(row => row.map(csvCell).join(',')).join('\r\n');
    }

    return Object.freeze({ PAGE_SIZE, MAX_ROWS, collect, validateSnapshot, toCsv,
        validateLessonSnapshot, toLessonCsv });
});
