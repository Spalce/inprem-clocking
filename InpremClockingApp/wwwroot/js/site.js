// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// set lastAction to the current time
var lastAction = new Date().getTime();

// make a ajax call to get the timeout value from the server
$.ajax({
    url: "/api/controls/get-logout",
    type: "GET",
    success: function(response) {
        if (response === true) {

        } else {
            setInterval(function () {
                if (new Date().getTime() - lastAction > response * 1000) {
                    // Redirect the user to the login page
                    window.location.href = '/Identity/Account/Logout';
                }
            }, 5 * 1000);
        }
    },
    error: function (xhr) {
        // showToast('Oops! An error occured. Try again', 5000, 'danger');
    }
});


