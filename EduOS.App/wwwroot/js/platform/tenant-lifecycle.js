(() => {
    "use strict";
    const root = document.getElementById("platformTenantWorkspace");
    if (!root) return;
    const $ = id => document.getElementById(id);
    const rows = $("platformTenantRows"), status = $("platformTenantStatus"), pageText = $("platformTenantPage");
    const form = $("platformTenantDecision"), reason = $("platformTenantReason");
    const csrf = document.querySelector('meta[name="request-verification-token"]')?.content || root.querySelector('input[name="__RequestVerificationToken"]')?.value;
    let page = 1, totalPages = 1, sequence = 0, selected = null, requestId = null, busy = false;
    const setStatus = message => { status.textContent = message || ""; };
    const cell = (tr, value) => { const td = document.createElement("td"); td.textContent = String(value ?? ""); tr.appendChild(td); return td; };
    const safeDate = value => value ? String(value).slice(0, 10) : "—";
    const stateCode = value => typeof value === "number" ? value : ({ PendingVerification: 1, Active: 2, Suspended: 3, Closed: 4 }[value] || Number(value));
    const stateLabel = item => item.stateName || ({ 1: "PendingVerification", 2: "Active", 3: "Suspended", 4: "Closed" }[stateCode(item.state)] || "Unknown");
    const closeDecision = () => { form.hidden = true; selected = null; requestId = null; reason.value = ""; };
    const load = async () => {
        const ticket = ++sequence;
        setStatus("Loading institutions…");
        const query = new URLSearchParams({ page: String(page), pageSize: "20" });
        const search = $("platformTenantSearch").value.trim();
        const state = $("platformTenantState").value;
        if (search) query.set("search", search);
        if (state) query.set("state", state);
        try {
            const response = await fetch(`/api/platform/tenants?${query}`, { credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } });
            const payload = await response.json();
            if (ticket !== sequence) return;
            if (!response.ok || !payload.success || !payload.data) throw new Error(payload.message || "Unable to load institutions.");
            const data = payload.data;
            totalPages = Math.max(1, Number(data.totalPages) || 1);
            rows.replaceChildren();
            for (const item of data.items || []) {
                const tr = document.createElement("tr");
                cell(tr, item.name); cell(tr, item.code); cell(tr, stateLabel(item));
                cell(tr, item.onboardingComplete ? "Complete" : "Incomplete"); cell(tr, safeDate(item.createdAt));
                const actionCell = cell(tr, "");
                const currentState = stateCode(item.state);
                if (currentState === 2 || currentState === 3) {
                    const button = document.createElement("button");
                    button.type = "button"; button.className = currentState === 2 ? "btn btn-sm btn-outline-danger" : "btn btn-sm btn-outline-success";
                    button.textContent = currentState === 2 ? "Suspend" : "Reactivate";
                    button.addEventListener("click", () => {
                        selected = { id: item.id, name: item.name, rowVersion: item.rowVersion, targetState: currentState === 2 ? 3 : 2 };
                        requestId = crypto.randomUUID();
                        $("platformTenantDecisionTitle").textContent = `${button.textContent} — ${item.name}`;
                        reason.value = ""; form.hidden = false; reason.focus();
                    });
                    actionCell.appendChild(button);
                }
                rows.appendChild(tr);
            }
            pageText.textContent = `Page ${page} of ${totalPages} • ${Number(data.totalCount) || 0} institutions`;
            $("platformTenantPrevious").disabled = page <= 1;
            $("platformTenantNext").disabled = page >= totalPages;
            setStatus(data.items?.length ? "" : "No matching institutions.");
        } catch (error) { if (ticket === sequence) setStatus(error.message || "Unable to load institutions."); }
    };
    $("platformTenantFilter").addEventListener("submit", event => { event.preventDefault(); if (busy) return; page = 1; closeDecision(); load(); });
    $("platformTenantRefresh").addEventListener("click", () => { if (busy) return; closeDecision(); load(); });
    $("platformTenantPrevious").addEventListener("click", () => { if (!busy && page > 1) { closeDecision(); page--; load(); } });
    $("platformTenantNext").addEventListener("click", () => { if (!busy && page < totalPages) { closeDecision(); page++; load(); } });
    $("platformTenantCancel").addEventListener("click", closeDecision);
    form.addEventListener("submit", async event => {
        event.preventDefault();
        if (busy || !selected) return;
        const why = reason.value.trim();
        if (why.length < 10 || why.length > 500) { setStatus("An audit reason of 10–500 characters is required."); return; }
        if (!csrf) { setStatus("Security token unavailable. Reload the page."); return; }
        busy = true; $("platformTenantSubmit").disabled = true;
        try {
            const response = await fetch(`/api/platform/tenants/${encodeURIComponent(selected.id)}/state`, {
                method: "POST", credentials: "same-origin", cache: "no-store",
                headers: { "Content-Type": "application/json", "RequestVerificationToken": csrf },
                body: JSON.stringify({ targetState: selected.targetState, reason: why, rowVersion: selected.rowVersion, requestId })
            });
            const payload = await response.json();
            if (!response.ok || !payload.success) {
                if (response.status === 409) { closeDecision(); await load(); }
                throw new Error(payload.message || "Institution state could not be changed.");
            }
            closeDecision(); await load(); setStatus("Institution state updated and audited.");
        } catch (error) { setStatus(error.message || "Institution state could not be changed."); }
        finally { busy = false; $("platformTenantSubmit").disabled = false; }
    });
    load();
})();
