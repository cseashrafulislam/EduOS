(() => {
    "use strict";
    const root = document.getElementById("platformDashboard");
    if (!root) return;
    const button = document.getElementById("platformRefresh");
    const status = document.getElementById("platformStatus");
    const refreshed = document.getElementById("platformRefreshedAt");
    let pending = null;
    const date = value => {
        const d = new Date(value);
        return Number.isNaN(d.getTime()) ? "—" : d.toISOString().slice(0, 10);
    };
    const row = values => {
        const tr = document.createElement("tr");
        values.forEach(value => {
            const td = document.createElement("td");
            td.textContent = String(value ?? "—");
            tr.appendChild(td);
        });
        return tr;
    };
    const fill = (id, items, mapping) => {
        const tbody = document.getElementById(id);
        if (!tbody) return;
        const rows = Array.isArray(items) ? items.map(x => row(mapping(x))) : [];
        if (!rows.length) {
            const tr = row(["No records found"]);
            tr.firstChild.colSpan = mapping([]).length;
            rows.push(tr);
        }
        tbody.replaceChildren(...rows);
    };
    const render = data => {
        const keys = ["totalTenants", "activeTenants", "pendingVerificationTenants",
            "incompleteOnboardingTenants", "newTenantsLast30Days",
            "subscriptionsExpiringNext7Days", "overdueActiveSubscriptions", "pendingManualPayments"];
        keys.forEach(key => {
            const el = root.querySelector('[data-platform-count="' + key + '"]');
            if (el) el.textContent = data[key] === null || data[key] === undefined ? "—" : String(data[key]);
        });
        fill("platformRecentTenants", data.recentTenants,
            x => [x.name, x.state, x.onboardingStage, date(x.createdAt)]);
        fill("platformExpiringSubscriptions", data.upcomingExpirations,
            x => [x.tenantName, x.subscriptionState, date(x.endsAtUtc)]);
        refreshed.textContent = data.generatedAtUtc ? new Date(data.generatedAtUtc).toISOString().replace("T", " ").slice(0, 19) : "—";
    };
    button.addEventListener("click", async () => {
        if (pending) pending.abort();
        const controller = new AbortController();
        pending = controller;
        button.disabled = true;
        status.textContent = "Refreshing platform metrics…";
        try {
            const response = await fetch("/api/dashboard/platform", {
                method: "GET", credentials: "same-origin", cache: "no-store",
                headers: { Accept: "application/json" }, signal: controller.signal
            });
            const payload = await response.json();
            if (!response.ok || !payload.success || !payload.data)
                throw new Error(payload.message || "Unable to load platform metrics.");
            if (pending !== controller) return;
            render(payload.data);
            status.textContent = "Platform metrics updated.";
        } catch (error) {
            if (error.name !== "AbortError" && pending === controller)
                status.textContent = "Refresh failed. Existing values were preserved.";
        } finally {
            if (pending === controller) { pending = null; button.disabled = false; }
        }
    });
})();
