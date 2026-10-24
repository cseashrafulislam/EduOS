(() => {
"use strict";
const $ = id => document.getElementById(id);
const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
const state = { page: 1, pages: 1, generation: 0, quotaGeneration: 0, capturing: false };
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
async function loadQuota() {
    const generation = ++state.quotaGeneration;
    const info = $("uhQuotaInfo"), rows = $("uhQuotaRows");
    info.textContent = "Loading quota comparison...";
    try {
        const data = await api("/quota-overview");
        if (generation !== state.quotaGeneration) return;
        rows.replaceChildren();
        const snapshot = data.snapshotDay;
        info.textContent = (data.hasActiveSubscription
            ? "Plan: " + data.planName + " (" + data.subscriptionStatus + "). "
            : "No currently effective subscription. ")
            + (snapshot ? "Saved usage as of " + snapshot + " (not live)." :
                "No saved usage snapshot yet. Capture today's usage to compare limits.");
        for (const metric of data.metrics || []) {
            const tr = document.createElement("tr");
            td(tr, metric.name); td(tr, format(metric.used));
            td(tr, metric.isUnlimited ? "Unlimited" :
                (metric.limit == null ? "Not available" : format(metric.limit)));
            td(tr, metric.utilizationPercent == null ? "—" :
                Number(metric.utilizationPercent).toFixed(1) + "%");
            td(tr, metric.isOverLimit ? "Over limit" :
                (metric.isNearLimit ? "At or near limit" :
                    (metric.limit == null ? "No applicable limit" : "Within limit")));
            rows.append(tr);
        }
    } catch (error) {
        if (generation === state.quotaGeneration) {
            rows.replaceChildren();
            info.textContent = "Quota comparison unavailable: " + error.message;
        }
    }
}

async function loadAutomation() {
    const element = $("uhAutomationStatus");
    try {
        const data = await api("/automation-status");
        element.textContent = "Institution local day: " + data.localDay +
            " (" + data.effectiveTimeZoneId + "). " +
            (data.capturedToday ? "Today's snapshot is saved." :
                "Today's snapshot has not been saved yet.") +
            (data.lastCapturedDay ? " Latest saved day: " + data.lastCapturedDay + "." : "");
    } catch (error) {
        element.textContent = "Automatic capture status unavailable: " + error.message;
    }
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
        state.page = 1; await load(); await loadQuota(); await loadAutomation();
    } catch (error) { status(error.message, true); }
    finally { state.capturing = false; $("uhCapture").disabled = false; }
});
$("uhRefresh").addEventListener("click", () => { load(); loadQuota(); loadAutomation(); });
for (const [id, delta] of [["uhPrev", -1], ["uhNext", 1]]) {
    $(id).addEventListener("click", () => {
        const next = state.page + delta;
        if (next < 1 || next > state.pages) return;
        state.page = next; load();
    });
}
load();
loadQuota();
loadAutomation();
})();
