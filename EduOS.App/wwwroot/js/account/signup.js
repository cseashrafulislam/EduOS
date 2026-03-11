$(function() {
    $("#btnSignup").on("click", function() {
        signup();
    });

    $("#signupForm").on("keypress", function(e) {
        if (e.which === 13) {
            e.preventDefault();
            signup();
        }
    });

    function signup() {
        const payload = {
            institutionName: $("#InstitutionName").val().trim(),
            institutionType: $("#InstitutionType").val(),
            ownerName: $("#OwnerName").val().trim(),
            email: $("#Email").val().trim(),
            phone: $("#Phone").val().trim(),
            address: $("#Address").val().trim(),
            password: $("#Password").val()
        };

        const confirmPassword = $("#ConfirmPassword").val();
        const agreeTerms = $("#AgreeTerms").is(":checked");

        if (!payload.institutionName) {
            return showMsg("Institution name is required.", "danger");
        }

        if (!payload.institutionType) {
            return showMsg("Institution type is required.", "danger");
        }

        if (!payload.ownerName) {
            return showMsg("Owner/Admin full name is required.", "danger");
        }

        if (!payload.email) {
            return showMsg("Email is required.", "danger");
        }

        if (!isValidEmail(payload.email)) {
            return showMsg("Please enter a valid email address.", "danger");
        }

        if (!payload.phone) {
            return showMsg("Phone number is required.", "danger");
        }

        if (!payload.password) {
            return showMsg("Password is required.", "danger");
        }

        if (payload.password.length < 6) {
            return showMsg("Password must be at least 6 characters.", "danger");
        }

        if (payload.password !== confirmPassword) {
            return showMsg("Password and confirm password do not match.", "danger");
        }

        if (!agreeTerms) {
            return showMsg("You must agree to the terms first.", "danger");
        }

        toggleButton(true);

        $.ajax({
        url: "/api/institution-onboarding/signup",
        type: "POST",
        contentType: "application/json",
        data: JSON.stringify(payload),
        success: function(res) {
            showMsg(res.message || "Institution account created successfully.", "success");

            setTimeout(function() {
                window.location.href = "/Account/SignupSuccess";
            }, 1200);
        },
        error: function(xhr) {
            let msg = "Signup failed.";

            if (xhr.responseJSON) {
                if (xhr.responseJSON.message) {
                    msg = xhr.responseJSON.message;
                } else if (xhr.responseJSON.title) {
                    msg = xhr.responseJSON.title;
                }
            }

            showMsg(msg, "danger");
        },
        complete: function() {
            toggleButton(false);
        }
    });
    }

    function toggleButton(isLoading) {
        $("#btnSignup")
   .prop("disabled", isLoading)
   .text(isLoading ? "Creating..." : "Create Institution Account");
    }

    function showMsg(message, type) {
        $("#msgBox")
   .removeClass("d-none alert-success alert-danger alert-warning")
   .addClass("alert-" + type)
   .text(message);
    }

    function isValidEmail(email) {
        const re = /^[^\s@@]+@[^\s@@]+\.[^\s@@]+$/;
        return re.test(email);
    }
});