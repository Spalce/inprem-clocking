// Provider Console (multi-tenancy Phase 1). No dependencies.
(function () {
  "use strict";

  var passwordDialog = document.querySelector("[data-temporary-password-dialog]");
  if (passwordDialog) {
    var closePasswordDialog = passwordDialog.querySelector("[data-close-temporary-password]");
    if (closePasswordDialog) {
      closePasswordDialog.focus();
      closePasswordDialog.addEventListener("click", function () {
        passwordDialog.remove();
      });
    }
  }

  // Organization wizard: suggest an address name from the name, and check availability while typing.
  var slug = document.querySelector("[data-slug-input]");
  if (slug) {
    var name = document.querySelector("[data-slug-source]");
    var status = document.querySelector("[data-slug-status]");
    var checkUrl = slug.getAttribute("data-slug-check");
    var touched = slug.value.length > 0;
    var timer = null;

    var suggest = function (text) {
      return text.toLowerCase().trim().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").substring(0, 50);
    };

    var check = function () {
      var value = slug.value.trim();
      if (!value) { status.textContent = ""; return; }
      fetch(checkUrl + "&slug=" + encodeURIComponent(value), { headers: { "Accept": "application/json" } })
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function (result) {
          if (!result) { status.textContent = ""; return; }
          status.className = result.available ? "pc-slug-ok" : "pc-slug-taken";
          status.textContent = result.available ? "Available" : (result.problem + (result.suggestion ? " Try: " + result.suggestion : ""));
        })
        .catch(function () { status.textContent = ""; });
    };

    var schedule = function () {
      clearTimeout(timer);
      timer = setTimeout(check, 300);
    };

    slug.addEventListener("input", function () { touched = true; schedule(); });
    if (name) {
      name.addEventListener("input", function () {
        if (!touched) { slug.value = suggest(name.value); schedule(); }
      });
    }
    if (slug.value) schedule();
  }
})();
