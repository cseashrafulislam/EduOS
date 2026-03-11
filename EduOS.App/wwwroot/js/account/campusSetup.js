$(function() {
    loadCampuses();

    $("#btnSave").on("click", function() {
        saveCampus();
    });

    $("#btnReset").on("click", function() {
        resetForm();
    });

    $(document).on("click", ".btn-edit-campus", function() {
        const id = $(this).data("id");
        editCampus(id);
    });

    $(document).on("click", ".btn-delete-campus", function() {
        const id = $(this).data("id");
        deleteCampus(id);
    });

    function loadCampuses() {
        $.ajax({
        url: "/api/institution-onboarding/campus-list",
        type: "GET",
        success: function(res) {
            renderCampusTable(res || []);
        },
        error: function() {
            showMsg("Failed to load campus list.", "danger");
        }
    });
    }

    function renderCampusTable(items) {
        const tbody = $("#campusTable tbody");
        tbody.empty();

        if (!items.length) {
            tbody.append(`
                <tr>
                    <td colspan="7" class="text-center text-muted">No campus found.</td>
                </tr>
            `);
            return;
        }

        items.forEach(x => {
        tbody.append(`
                <tr>
                    <td>${x.name || ""}</td>
                    <td>${x.code || ""}</td>
                    <td>${x.campusType || ""}</td>
                    <td>${x.contactNumber || ""}</td>
                    <td>${x.isMainCampus ? "Yes" : "No"}</td>
                    <td>${x.isActive ? "Active" : "Inactive"}</td>
                    <td>
                        <button type="button" class="btn btn-sm btn-primary btn-edit-campus" data-id="${x.id}">Edit</button>
                        <button type="button" class="btn btn-sm btn-danger btn-delete-campus ms-1" data-id="${x.id}">Delete</button>
                    </td>
                </tr>
            `);
    });
    }

    function saveCampus() {
        const payload = {
            id: $("#CampusId").val() ? parseInt($("#CampusId").val()) : null,
            name: $("#Name").val().trim(),
            code: $("#Code").val().trim(),
            campusType: $("#CampusType").val(),
            contactNumber: $("#ContactNumber").val().trim(),
            email: $("#Email").val().trim(),
            country: $("#Country").val().trim(),
            division: $("#Division").val().trim(),
            district: $("#District").val().trim(),
            thana: $("#Thana").val().trim(),
            postCode: $("#PostCode").val().trim(),
            address: $("#Address").val().trim(),
            principalName: $("#PrincipalName").val().trim(),
            headName: $("#HeadName").val().trim(),
            isMainCampus: $("#IsMainCampus").is(":checked"),
            isActive: $("#IsActive").is(":checked"),
            displayOrder: parseInt($("#DisplayOrder").val() || "1")
        };

        if (!payload.name) {
            return showMsg("Campus name is required.", "danger");
        }

        $("#btnSave").prop("disabled", true).text("Saving...");

        $.ajax({
        url: "/api/institution-onboarding/campus",
        type: "POST",
        contentType: "application/json",
        data: JSON.stringify(payload),
        success: function(res) {
            showMsg(res.message || "Campus saved successfully.", "success");
            resetForm();
            loadCampuses();
        },
        error: function(xhr) {
            let msg = "Campus save failed.";
            if (xhr.responseJSON && xhr.responseJSON.message) {
                msg = xhr.responseJSON.message;
            }
            showMsg(msg, "danger");
        },
        complete: function() {
            $("#btnSave").prop("disabled", false).text("Save Campus");
        }
    });
    }

    function editCampus(id) {
        $.ajax({
        url: "/api/institution-onboarding/campus/" + id,
        type: "GET",
        success: function(res) {
            $("#CampusId").val(res.id || "");
            $("#Name").val(res.name || "");
            $("#Code").val(res.code || "");
            $("#CampusType").val(res.campusType || "");
            $("#ContactNumber").val(res.contactNumber || "");
            $("#Email").val(res.email || "");
            $("#Country").val(res.country || "");
            $("#Division").val(res.division || "");
            $("#District").val(res.district || "");
            $("#Thana").val(res.thana || "");
            $("#PostCode").val(res.postCode || "");
            $("#Address").val(res.address || "");
            $("#PrincipalName").val(res.principalName || "");
            $("#HeadName").val(res.headName || "");
            $("#IsMainCampus").prop("checked", res.isMainCampus || false);
            $("#IsActive").prop("checked", res.isActive || false);
            $("#DisplayOrder").val(res.displayOrder || 1);

            $("html, body").animate({ scrollTop: 0 }, 200);
        },
        error: function() {
            showMsg("Failed to load campus details.", "danger");
        }
    });
    }

    function deleteCampus(id) {
        if (!confirm("Are you sure you want to delete this campus?")) {
            return;
        }

        $.ajax({
        url: "/api/institution-onboarding/campus/" + id,
        type: "DELETE",
        success: function(res) {
            showMsg(res.message || "Campus deleted successfully.", "success");
            loadCampuses();
            resetForm();
        },
        error: function(xhr) {
            let msg = "Campus delete failed.";
            if (xhr.responseJSON && xhr.responseJSON.message) {
                msg = xhr.responseJSON.message;
            }
            showMsg(msg, "danger");
        }
    });
    }

    function resetForm() {
        $("#CampusId").val("");
        $("#Name").val("");
        $("#Code").val("");
        $("#CampusType").val("");
        $("#ContactNumber").val("");
        $("#Email").val("");
        $("#Country").val("Bangladesh");
        $("#Division").val("");
        $("#District").val("");
        $("#Thana").val("");
        $("#PostCode").val("");
        $("#Address").val("");
        $("#PrincipalName").val("");
        $("#HeadName").val("");
        $("#IsMainCampus").prop("checked", false);
        $("#IsActive").prop("checked", true);
        $("#DisplayOrder").val(1);
    }

    function showMsg(message, type) {
        $("#msgBox")
   .removeClass("d-none alert-success alert-danger")
   .addClass("alert alert-" + type)
   .text(message);
    }
});