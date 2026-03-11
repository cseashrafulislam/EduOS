$(function() {
    loadProfile();

    $("#btnSave").on("click", function() {
        saveProfile();
    });

    $("#LogoFile").on("change", function() {
        previewImage(this, "#LogoPreview", "#LogoEmptyText");
    });

    $("#FaviconFile").on("change", function() {
        previewImage(this, "#FaviconPreview", "#FaviconEmptyText");
    });

    function loadProfile() {
        $.ajax({
        url: "/api/institution-onboarding/institution-profile",
        type: "GET",
        success: function(res) {
            res = res.data;
            $("#InstitutionName").val(res.institutionName || "");
            $("#InstitutionType").val(res.institutionType || "");
            $("#OwnerName").val(res.ownerName || "");
            $("#Email").val(res.email || "");
            $("#Phone").val(res.phone || "");
            $("#AlternatePhone").val(res.alternatePhone || "");
            $("#Address").val(res.address || "");
            $("#ContactPersonName").val(res.contactPersonName || "");
            $("#ContactPersonDesignation").val(res.contactPersonDesignation || "");
            $("#ContactPersonEmail").val(res.contactPersonEmail || "");
            $("#ShortName").val(res.shortName || "");
            $("#TimeZone").val(res.timeZone || "Asia/Dhaka");
            $("#Currency").val(res.currency || "BDT");
            $("#Country").val(res.country || "");
            $("#Division").val(res.division || "");
            $("#District").val(res.district || "");
            $("#Thana").val(res.thana || "");
            $("#PostCode").val(res.postCode || "");
            $("#Subdomain").val(res.subdomain || "");
            $("#CustomDomain").val(res.customDomain || "");
            $("#PrimaryColor").val(res.primaryColor || "");
            $("#SecondaryColor").val(res.secondaryColor || "");
            $("#WebsiteUrl").val(res.websiteUrl || "");
            $("#EIIN").val(res.eiin || "");
            $("#RegistrationNumber").val(res.registrationNumber || "");
            $("#EducationBoard").val(res.educationBoard || "");
            $("#EstablishedDate").val(res.establishedDate ? res.establishedDate.split("T")[0] : "");
            $("#InstitutionCode").val(res.institutionCode || "");
            $("#Language").val(res.language || "en");
            $("#DateFormat").val(res.dateFormat || "dd-MMM-yyyy");

            setExistingImage(res.logoUrl, "#LogoPreview", "#LogoEmptyText");
            setExistingImage(res.faviconUrl, "#FaviconPreview", "#FaviconEmptyText");
        },
        error: function(xhr) {
            let msg = "Profile load failed.";
            if (xhr.responseJSON && xhr.responseJSON.message) {
                msg = xhr.responseJSON.message;
            }
            showMsg(msg, "danger");
        }
    });
    }

    function saveProfile() {
        const formData = new FormData();

        formData.append("InstitutionName", $("#InstitutionName").val().trim());
        formData.append("InstitutionType", $("#InstitutionType").val());
        formData.append("OwnerName", $("#OwnerName").val().trim());

        formData.append("Email", $("#Email").val().trim());
        formData.append("Phone", $("#Phone").val().trim());
        formData.append("AlternatePhone", $("#AlternatePhone").val().trim());
        formData.append("Address", $("#Address").val().trim());

        formData.append("ContactPersonName", $("#ContactPersonName").val().trim());
        formData.append("ContactPersonDesignation", $("#ContactPersonDesignation").val().trim());
        formData.append("ContactPersonEmail", $("#ContactPersonEmail").val().trim());

        formData.append("ShortName", $("#ShortName").val().trim());
        formData.append("TimeZone", $("#TimeZone").val().trim());
        formData.append("Currency", $("#Currency").val().trim());

        formData.append("Country", $("#Country").val().trim());
        formData.append("Division", $("#Division").val().trim());
        formData.append("District", $("#District").val().trim());
        formData.append("Thana", $("#Thana").val().trim());
        formData.append("PostCode", $("#PostCode").val().trim());

        formData.append("Subdomain", $("#Subdomain").val().trim());
        formData.append("CustomDomain", $("#CustomDomain").val().trim());

        formData.append("PrimaryColor", $("#PrimaryColor").val().trim());
        formData.append("SecondaryColor", $("#SecondaryColor").val().trim());
        formData.append("WebsiteUrl", $("#WebsiteUrl").val().trim());

        formData.append("EIIN", $("#EIIN").val().trim());
        formData.append("RegistrationNumber", $("#RegistrationNumber").val().trim());
        formData.append("EducationBoard", $("#EducationBoard").val().trim());
        formData.append("EstablishedDate", $("#EstablishedDate").val());

        formData.append("InstitutionCode", $("#InstitutionCode").val().trim());

        formData.append("Language", $("#Language").val().trim());
        formData.append("DateFormat", $("#DateFormat").val().trim());

        const logoFile = $("#LogoFile")[0].files[0];
        if (logoFile) {
            formData.append("LogoFile", logoFile);
        }

        const faviconFile = $("#FaviconFile")[0].files[0];
        if (faviconFile) {
            formData.append("FaviconFile", faviconFile);
        }

        const institutionName = $("#InstitutionName").val().trim();
        const institutionType = $("#InstitutionType").val();
        const ownerName = $("#OwnerName").val().trim();
        const email = $("#Email").val().trim();

        if (!institutionName)
            return showMsg("Institution name is required.", "danger");

        if (!institutionType)
            return showMsg("Institution type is required.", "danger");

        if (!ownerName)
            return showMsg("Owner name is required.", "danger");

        if (!email)
            return showMsg("Email is required.", "danger");

        $("#btnSave").prop("disabled", true).text("Saving...");

        $.ajax({
        url: "/api/institution-onboarding/institution-profile",
        type: "POST",
        data: formData,
        processData: false,
        contentType: false,
        success: function(res) {
            showMsg(res.message || "Saved successfully.", "success");

            setTimeout(function() {
                window.location.href = "/Dashboard/Index";
            }, 1000);
        },
        error: function(xhr) {
            let msg = "Save failed.";
            if (xhr.responseJSON && xhr.responseJSON.message) {
                msg = xhr.responseJSON.message;
            }
            showMsg(msg, "danger");
        },
        complete: function() {
            $("#btnSave").prop("disabled", false).text("Save & Continue");
        }
    });
    }

    function previewImage(input, previewSelector, emptyTextSelector) {
        const file = input.files && input.files[0];
        if (!file) return;

        const reader = new FileReader();
        reader.onload = function(e) {
            $(previewSelector).attr("src", e.target.result).show();
            $(emptyTextSelector).hide();
        };
        reader.readAsDataURL(file);
    }

    function setExistingImage(url, previewSelector, emptyTextSelector) {
        if (url) {
            $(previewSelector).attr("src", url).show();
            $(emptyTextSelector).hide();
        } else {
            $(previewSelector).hide();
            $(emptyTextSelector).show();
        }
    }

    function showMsg(message, type) {
        $("#msgBox")
   .removeClass("d-none alert-success alert-danger")
   .addClass("alert alert-" + type)
   .text(message);
    }
});