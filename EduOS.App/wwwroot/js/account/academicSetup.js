$(function() {
    loadAcademicYears();
    loadAcademicTerms();

    $("#btnSaveYear").on("click", function() {
        saveYear();
    });

    $("#btnSaveTerm").on("click", function() {
        saveTerm();
    });

    $("#btnResetYear").on("click", function() {
        resetYearForm();
    });

    $("#btnResetTerm").on("click", function() {
        resetTermForm();
    });

    $(document).on("click", ".btn-edit-year", function() {
        editYear($(this).data("id"));
    });

    $(document).on("click", ".btn-delete-year", function() {
        deleteYear($(this).data("id"));
    });

    $(document).on("click", ".btn-edit-term", function() {
        editTerm($(this).data("id"));
    });

    $(document).on("click", ".btn-delete-term", function() {
        deleteTerm($(this).data("id"));
    });

    function loadAcademicYears() {
        $.ajax({
        url: "/api/institution-onboarding/academic-years",
        type: "GET",
        success: function(res) {
            renderYearTable(res || []);
            bindYearDropdown(res || []);
        },
        error: function() {
            showMsg("Failed to load academic years.", "danger");
        }
    });
    }

    function loadAcademicTerms() {
        $.ajax({
        url: "/api/institution-onboarding/academic-terms",
        type: "GET",
        success: function(res) {
            renderTermTable(res || []);
        },
        error: function() {
            showMsg("Failed to load academic terms.", "danger");
        }
    });
    }

    function renderYearTable(items) {
        const tbody = $("#yearTable tbody");
        tbody.empty();

        if (!items.length) {
            tbody.append(`<tr><td colspan="6" class="text-center text-muted">No academic year found.</td></tr>`);
            return;
        }

        items.forEach(x => {
        tbody.append(`
                <tr>
                    <td>${x.name || ""}</td>
                    <td>${formatDate(x.startDate)}</td>
                    <td>${formatDate(x.endDate)}</td>
                    <td>${x.isCurrent ? "Yes" : "No"}</td>
                    <td>${x.isActive ? "Active" : "Inactive"}</td>
                    <td>
                        <button type="button" class="btn btn-sm btn-primary btn-edit-year" data-id="${x.id}">Edit</button>
                        <button type="button" class="btn btn-sm btn-danger btn-delete-year ms-1" data-id="${x.id}">Delete</button>
                    </td>
                </tr>
            `);
    });
    }

    function renderTermTable(items) {
        const tbody = $("#termTable tbody");
        tbody.empty();

        if (!items.length) {
            tbody.append(`<tr><td colspan="7" class="text-center text-muted">No academic term found.</td></tr>`);
            return;
        }

        items.forEach(x => {
        tbody.append(`
                <tr>
                    <td>${x.academicYearName || ""}</td>
                    <td>${x.name || ""}</td>
                    <td>${x.termType || ""}</td>
                    <td>${formatDate(x.startDate)}</td>
                    <td>${formatDate(x.endDate)}</td>
                    <td>${x.isCurrent ? "Yes" : "No"}</td>
                    <td>
                        <button type="button" class="btn btn-sm btn-primary btn-edit-term" data-id="${x.id}">Edit</button>
                        <button type="button" class="btn btn-sm btn-danger btn-delete-term ms-1" data-id="${x.id}">Delete</button>
                    </td>
                </tr>
            `);
    });
    }

    function bindYearDropdown(items) {
        const ddl = $("#TermAcademicYearId");
        const currentValue = ddl.val();

        ddl.empty();
        ddl.append(`<option value="">Select Year</option>`);

        items.forEach(x => {
        ddl.append(`<option value="${x.id}">${x.name}</option>`);
    });

        if (currentValue) {
            ddl.val(currentValue);
        }
    }

    function saveYear() {
        const payload = {
            id: $("#AcademicYearId").val() ? parseInt($("#AcademicYearId").val()) : null,
            name: $("#YearName").val().trim(),
            startDate: $("#YearStartDate").val(),
            endDate: $("#YearEndDate").val(),
            isCurrent: $("#YearIsCurrent").is(":checked"),
            isActive: $("#YearIsActive").is(":checked"),
            displayOrder: parseInt($("#YearDisplayOrder").val() || "1")
        };

        if (!payload.name)
            return showMsg("Academic year name is required.", "danger");

        if (!payload.startDate)
            return showMsg("Academic year start date is required.", "danger");

        if (!payload.endDate)
            return showMsg("Academic year end date is required.", "danger");

        $("#btnSaveYear").prop("disabled", true).text("Saving...");

        $.ajax({
        url: "/api/institution-onboarding/academic-year",
        type: "POST",
        contentType: "application/json",
        data: JSON.stringify(payload),
        success: function(res) {
            showMsg(res.message || "Academic year saved successfully.", "success");
            resetYearForm();
            loadAcademicYears();
            loadAcademicTerms();
        },
        error: function(xhr) {
            let msg = "Academic year save failed.";
            if (xhr.responseJSON && xhr.responseJSON.message) {
                msg = xhr.responseJSON.message;
            }
            showMsg(msg, "danger");
        },
        complete: function() {
            $("#btnSaveYear").prop("disabled", false).text("Save Year");
        }
    });
    }

    function saveTerm() {
        const payload = {
            id: $("#AcademicTermId").val() ? parseInt($("#AcademicTermId").val()) : null,
            academicYearId: parseInt($("#TermAcademicYearId").val() || "0"),
            name: $("#TermName").val().trim(),
            termType: $("#TermType").val(),
            startDate: $("#TermStartDate").val(),
            endDate: $("#TermEndDate").val(),
            isCurrent: $("#TermIsCurrent").is(":checked"),
            isActive: $("#TermIsActive").is(":checked"),
            displayOrder: parseInt($("#TermDisplayOrder").val() || "1")
        };

        if (!payload.academicYearId)
            return showMsg("Academic year is required.", "danger");

        if (!payload.name)
            return showMsg("Academic term name is required.", "danger");

        if (!payload.startDate)
            return showMsg("Academic term start date is required.", "danger");

        if (!payload.endDate)
            return showMsg("Academic term end date is required.", "danger");

        $("#btnSaveTerm").prop("disabled", true).text("Saving...");

        $.ajax({
        url: "/api/institution-onboarding/academic-term",
        type: "POST",
        contentType: "application/json",
        data: JSON.stringify(payload),
        success: function(res) {
            showMsg(res.message || "Academic term saved successfully.", "success");
            resetTermForm();
            loadAcademicTerms();
        },
        error: function(xhr) {
            let msg = "Academic term save failed.";
            if (xhr.responseJSON && xhr.responseJSON.message) {
                msg = xhr.responseJSON.message;
            }
            showMsg(msg, "danger");
        },
        complete: function() {
            $("#btnSaveTerm").prop("disabled", false).text("Save Term");
        }
    });
    }

    function editYear(id) {
        $.ajax({
        url: "/api/institution-onboarding/academic-year/" + id,
        type: "GET",
        success: function(res) {
            $("#AcademicYearId").val(res.id || "");
            $("#YearName").val(res.name || "");
            $("#YearStartDate").val(toInputDate(res.startDate));
            $("#YearEndDate").val(toInputDate(res.endDate));
            $("#YearIsCurrent").prop("checked", res.isCurrent || false);
            $("#YearIsActive").prop("checked", res.isActive || false);
            $("#YearDisplayOrder").val(res.displayOrder || 1);

            $("html, body").animate({ scrollTop: 0 }, 200);
        },
        error: function() {
            showMsg("Failed to load academic year.", "danger");
        }
    });
    }

    function editTerm(id) {
        $.ajax({
        url: "/api/institution-onboarding/academic-term/" + id,
        type: "GET",
        success: function(res) {
            $("#AcademicTermId").val(res.id || "");
            $("#TermAcademicYearId").val(res.academicYearId || "");
            $("#TermName").val(res.name || "");
            $("#TermType").val(res.termType || "");
            $("#TermStartDate").val(toInputDate(res.startDate));
            $("#TermEndDate").val(toInputDate(res.endDate));
            $("#TermIsCurrent").prop("checked", res.isCurrent || false);
            $("#TermIsActive").prop("checked", res.isActive || false);
            $("#TermDisplayOrder").val(res.displayOrder || 1);

            $("html, body").animate({ scrollTop: 0 }, 200);
        },
        error: function() {
            showMsg("Failed to load academic term.", "danger");
        }
    });
    }

    function deleteYear(id) {
        if (!confirm("Are you sure you want to delete this academic year?")) return;

        $.ajax({
        url: "/api/institution-onboarding/academic-year/" + id,
        type: "DELETE",
        success: function(res) {
            showMsg(res.message || "Academic year deleted successfully.", "success");
            loadAcademicYears();
            loadAcademicTerms();
            resetYearForm();
        },
        error: function(xhr) {
            let msg = "Academic year delete failed.";
            if (xhr.responseJSON && xhr.responseJSON.message) {
                msg = xhr.responseJSON.message;
            }
            showMsg(msg, "danger");
        }
    });
    }

    function deleteTerm(id) {
        if (!confirm("Are you sure you want to delete this academic term?")) return;

        $.ajax({
        url: "/api/institution-onboarding/academic-term/" + id,
        type: "DELETE",
        success: function(res) {
            showMsg(res.message || "Academic term deleted successfully.", "success");
            loadAcademicTerms();
            resetTermForm();
        },
        error: function(xhr) {
            let msg = "Academic term delete failed.";
            if (xhr.responseJSON && xhr.responseJSON.message) {
                msg = xhr.responseJSON.message;
            }
            showMsg(msg, "danger");
        }
    });
    }

    function resetYearForm() {
        $("#AcademicYearId").val("");
        $("#YearName").val("");
        $("#YearStartDate").val("");
        $("#YearEndDate").val("");
        $("#YearIsCurrent").prop("checked", false);
        $("#YearIsActive").prop("checked", true);
        $("#YearDisplayOrder").val(1);
    }

    function resetTermForm() {
        $("#AcademicTermId").val("");
        $("#TermAcademicYearId").val("");
        $("#TermName").val("");
        $("#TermType").val("");
        $("#TermStartDate").val("");
        $("#TermEndDate").val("");
        $("#TermIsCurrent").prop("checked", false);
        $("#TermIsActive").prop("checked", true);
        $("#TermDisplayOrder").val(1);
    }

    function toInputDate(value) {
        if (!value) return "";
        return value.split("T")[0];
    }

    function formatDate(value) {
        if (!value) return "";
        return value.split("T")[0];
    }

    function showMsg(message, type) {
        $("#msgBox")
   .removeClass("d-none alert-success alert-danger")
   .addClass("alert alert-" + type)
   .text(message);
    }
});