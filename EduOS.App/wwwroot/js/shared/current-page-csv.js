(function (root) {
    'use strict';
    function csvCell(value) {
        const raw = value == null ? '' : String(value);
        // Spreadsheet programs evaluate leading =, +, -, @ after whitespace.
        // Treat every API-provided cell as text, including formula-like names.
        const escaped = /^[\u0000-\u0020]*[=+\-@]/.test(raw) ? "'" + raw : raw;
        return '"' + escaped.replace(/"/g, '""') + '"';
    }
    function serialize(headers, rows) {
        if (!Array.isArray(headers) || !headers.length || !Array.isArray(rows))
            throw new Error('A CSV export needs explicit headers and rows.');
        for (const row of rows)
            if (!Array.isArray(row) || row.length !== headers.length)
                throw new Error('CSV export columns do not match their headers.');
        return '\uFEFF' + [headers, ...rows].map(row => row.map(csvCell).join(',')).join('\r\n') + '\r\n';
    }
    function download(filename, headers, rows) {
        if (!Array.isArray(rows) || !rows.length) return false;
        if (!/^[a-zA-Z0-9][a-zA-Z0-9_.-]*\.csv$/.test(filename))
            throw new Error('Invalid CSV file name.');
        const blob = new Blob([serialize(headers, rows)], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        try {
            const link = document.createElement('a');
            link.href = url;
            link.download = filename;
            link.hidden = true;
            document.body.append(link);
            link.click();
            link.remove();
        } finally {
            setTimeout(() => URL.revokeObjectURL(url), 30000);
        }
        return true;
    }
    root.EduOSPageCsv = Object.freeze({ csvCell, serialize, download });
})(typeof window !== 'undefined' ? window : globalThis);
