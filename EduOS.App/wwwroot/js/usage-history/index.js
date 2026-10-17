(() => {
"use strict";
const $ = id => document.getElementById(id);
const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
const state = { page: 1, pages: 1, generation: 0, capturing: false };
const format = n => Number(n || 0).toLocaleString();
function status(message, error = false) {
    const el = $("uhStatus"); el.textContent = message;
    el.className = "small mb-3 " + (error ? "text-danger" : "text-muted");
}
async function api(path, method = "GET") {
    const options = { method, credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } };
    if (method !== "GET") {
        options.headers.RequestVerificationToken = token;
        options.headers["Content-Type"] = "application/json";
        options.body = "{}";
    }
    const response = await fetch("/api/usage-history" + path, options);
    let data; try { data = await response.json(); } catch { throw Error("Unexpected server response."); }
    if (!response.ok || !data.success) throw Error(data.message || "Request failed (" + response.status + ").");
    return data.data;
}
function td(tr, value) {
    const cell = document.createElement("td"); cell.textContent = String(value ?? ""); tr.append(cell);
}
async function load() {
    const generation = ++state.generation, page = state.page;
    status("Loading usage history...");
    try {
        const data = await api("?page=" + page + "&pageSize=20");
        if (generation !== state.generation || page !== state.page) return;
        state.pages = Math.max(1, Number(data.totalPages || 1));
        const tbody = $("uhRows"); tbody.replaceChildren();
        for (const item of data.items || []) {
            const tr = document.createElement("tr");
            td(tr, item.periodStart); td(tr, format(item.activeStudents));
            td(tr, format(item.activeEmployees)); td(tr, format(item.activeCampuses));
            tbody.append(tr);
        }
        if (!data.items?.length) {
            const tr = document.createElement("tr"), cell = document.createElement("td");
            cell.colSpan = 4; cell.textContent = "No usage snapshots yet. Capture today's usage to begin.";
            tr.append(cell); tbody.append(tr);
        }
        $("uhPage").textContent = "Page " + page + " / " + state.pages;
        $("uhPrev").disabled = page <= 1; $("uhNext").disabled = page >= state.pages;
        status((data.totalCount || 0) + " recorded daily snapshots.");
    } catch (error) { if (generation === state.generation) status(error.message, true); }
}
$("uhCapture").addEventListener("click", async () => {
    if (state.capturing) return;
    state.capturing = true; $("uhCapture").disabled = true;
    status("Capturing current counts...");
    try {
        await api("/capture", "POST");
        state.page = 1; await load();
    } catch (error) { status(error.message, true); }
    finally { state.capturing = false; $("uhCapture").disabled = false; }
});
$("uhRefresh").addEventListener("click", load);
for (const [id, delta] of [["uhPrev", -1], ["uhNext", 1]]) {
    $(id).addEventListener("click", () => {
        const next = state.page + delta;
        if (next < 1 || next > state.pages) return;
        state.page = next; load();
    });
}
load();
})();
