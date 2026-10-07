// Client-side validation for the back-office Add/Edit Staff and Add/Edit Volunteer dialogs
// (_StaffEditorScript / _VolunteerEditorScript). Those dialogs save via fetch() as JSON, not a
// normal form post, so they can't use jQuery Validate the way the kiosk register forms do - this
// gives them the same behaviour: a message under each invalid field, plus the shared showAlert()
// pop-up (Pages/Shared/_AlertModal.cshtml) listing the problems. Rules and wording mirror the
// Staff/Volunteer model attributes and VolunteerCategoryValidation on the server, which still
// re-checks everything.
var FormValidation = (function () {
    var NAME_PATTERN = /^[A-Za-z]+(?: +[A-Za-z]+)*$/;
    var EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    // Important (!) so it overrides the inputs' own tw:border-slate-300, which compiles later in
    // tailwind.css and would otherwise win at equal specificity.
    var INVALID_CLASS = 'tw:border-red-500!';

    // The message element sits directly after its field, created on first use so the dialog
    // markup doesn't need one per field.
    function errorSlot(field) {
        var slot = field.parentNode.querySelector('[data-error-for="' + field.id + '"]');
        if (!slot) {
            slot = document.createElement('span');
            slot.className = 'tw:mt-1 tw:block tw:text-xs tw:text-red-600';
            slot.setAttribute('data-error-for', field.id);
            field.insertAdjacentElement('afterend', slot);
        }
        return slot;
    }

    function setError(field, message) {
        errorSlot(field).textContent = message;
        field.classList.add(INVALID_CLASS);
        field.setAttribute('aria-invalid', 'true');
    }

    function clearErrors(container) {
        container.querySelectorAll('[data-error-for]').forEach(function (slot) { slot.textContent = ''; });
        container.querySelectorAll('[aria-invalid="true"]').forEach(function (field) {
            field.classList.remove(INVALID_CLASS);
            field.removeAttribute('aria-invalid');
        });
    }

    // A field inside a hidden category group (display:none) is skipped, matching how the kiosk
    // only requires the follow-up questions for the category actually chosen.
    function isShown(field) {
        return field.offsetParent !== null;
    }

    // First failing rule's message for one field, or null. Supported rules: required (message),
    // pattern ([regex, message]), email (message), minLength / maxLength ([n, message]).
    function check(field, rule) {
        var value = (field.value || '').trim();
        if (!value) return rule.required || null;
        if (rule.pattern && !rule.pattern[0].test(value)) return rule.pattern[1];
        if (rule.email && !EMAIL_PATTERN.test(value)) return rule.email;
        if (rule.minLength && value.length < rule.minLength[0]) return rule.minLength[1];
        if (rule.maxLength && value.length > rule.maxLength[0]) return rule.maxLength[1];
        return null;
    }

    // Shows each failed field's message inline, then the pop-up listing them all; closing the
    // pop-up puts the cursor in the first one.
    function report(failures, title) {
        var messages = [];
        var first = null;
        failures.forEach(function (f) {
            if (f.field) {
                setError(f.field, f.message);
                if (!first) first = f.field;
            }
            if (messages.indexOf(f.message) < 0) messages.push(f.message);
        });
        showAlert(title, messages, 'danger', function () { if (first) first.focus(); });
    }

    // rules: { fieldId: rule, ... }. Returns true when every shown field passes.
    function validate(container, rules) {
        clearErrors(container);
        var failures = [];
        Object.keys(rules).forEach(function (id) {
            var field = document.getElementById(id);
            if (!field || !isShown(field)) return;
            var message = check(field, rules[id]);
            if (message) failures.push({ field: field, message: message });
        });
        if (failures.length) report(failures, 'Please fill the highlighted fields');
        return failures.length === 0;
    }

    // A failed save's JSON body: { errors: { PropertyName: [messages] } } from ModelState, or
    // { error: message }. fieldIds maps a property name to its input's id.
    function showServerErrors(container, json, fieldIds) {
        clearErrors(container);
        if (!json || !json.errors) {
            showAlert('Could not save', json && json.error ? json.error : 'Request failed.', 'danger');
            return;
        }
        var failures = [];
        var unplaced = [];
        Object.keys(json.errors).forEach(function (key) {
            var field = document.getElementById(fieldIds[key.replace(/^\$\./, '')] || '');
            json.errors[key].forEach(function (message) {
                if (field) failures.push({ field: field, message: message });
                else unplaced.push({ field: null, message: message });
            });
        });
        report(failures.concat(unplaced), 'Could not save');
    }

    return {
        NAME_PATTERN: NAME_PATTERN,
        clearErrors: clearErrors,
        validate: validate,
        showServerErrors: showServerErrors
    };
})();
