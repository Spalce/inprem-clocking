// Shared chrome behavior for the Tailwind-based sidebar/topbar (see UI-modification.md).
// Deliberately independent of AdminLTE's JS widgets (data-widget="pushmenu"/"treeview"),
// which the old chrome used for the same two interactions.
(function () {
    var sidebar = document.getElementById('appSidebar');
    var backdrop = document.querySelector('[data-sidebar-backdrop]');

    function openSidebar() {
        if (!sidebar) return;
        sidebar.classList.remove('tw:-translate-x-full');
        if (backdrop) backdrop.classList.remove('tw:hidden');
    }

    function closeSidebar() {
        if (!sidebar) return;
        sidebar.classList.add('tw:-translate-x-full');
        if (backdrop) backdrop.classList.add('tw:hidden');
    }

    document.querySelectorAll('[data-sidebar-open]').forEach(function (btn) {
        btn.addEventListener('click', openSidebar);
    });
    if (backdrop) backdrop.addEventListener('click', closeSidebar);

    // Collapsible sidebar sections (e.g. "Reports"), toggled by a button whose
    // data-sidebar-toggle value matches the id of the panel to show/hide.
    document.querySelectorAll('[data-sidebar-toggle]').forEach(function (btn) {
        var panel = document.getElementById(btn.getAttribute('data-sidebar-toggle'));
        var chevron = btn.querySelector('[data-sidebar-chevron]');
        if (!panel) return;

        btn.addEventListener('click', function () {
            var isHidden = panel.classList.contains('tw:hidden');
            panel.classList.toggle('tw:hidden', !isHidden);
            if (chevron) chevron.classList.toggle('tw:rotate-180', isHidden);
        });
    });
})();

// Generic modal dialog show/hide - replaces Bootstrap's $(...).modal('show'/'hide') for the
// Edit/Add dialogs on Manage Staff/Volunteer. A modal is any element with a unique id; open it
// with openModal(id)/closeModal(id) from page script, or close it via a
// data-modal-dismiss="<id>" button/backdrop anywhere inside it.
function openModal(id) {
    var modal = document.getElementById(id);
    if (modal) modal.classList.remove('tw:hidden');
}

function closeModal(id) {
    var modal = document.getElementById(id);
    if (modal) modal.classList.add('tw:hidden');
}

document.addEventListener('click', function (e) {
    var dismiss = e.target.closest('[data-modal-dismiss]');
    if (dismiss) closeModal(dismiss.getAttribute('data-modal-dismiss'));
});

document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') return;
    document.querySelectorAll('[data-modal-root]').forEach(function (modal) {
        if (!modal.classList.contains('tw:hidden')) modal.classList.add('tw:hidden');
    });
});
