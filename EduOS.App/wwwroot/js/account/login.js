$(function() {

    $("#btnLogin").click(function() {
        login();
    });

    function login() {

        const payload = {
            email: $("#Email").val().trim(),
            password: $("#Password").val(),
            rememberMe: $("#RememberMe").is(":checked")
        };

        if (!payload.email)
            return showMsg("Email required", "danger");

        if (!payload.password)
            return showMsg("Password required", "danger");

        $("#btnLogin").prop("disabled", true).text("Logging...");

        $.ajax({
        url: "/api/auth/login",
        type: "POST",
        contentType: "application/json",
        data: JSON.stringify(payload),

        success: function(res) {

            if (res.success) {

                window.location.href = "/Dashboard/Index";

            } else {

                showMsg(res.message, "danger");

            }
        },

        error: function() {
            showMsg("Login failed", "danger");
        },

        complete: function() {
            $("#btnLogin").prop("disabled", false).text("Login");
        }
    });
    }

    function showMsg(msg, type) {

        $("#msgBox")
   .removeClass("d-none alert-success alert-danger")
   .addClass("alert-" + type)
   .text(msg);
    }

});