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

// Generic small dropdown menu (e.g. per-row action menus on the clocking pages). Markup shape:
// a [data-dropdown] wrapper containing one [data-dropdown-toggle] button and one
// [data-dropdown-menu] panel. Positioned with `position: fixed`, computed from the toggle
// button's rect at open time, rather than CSS `absolute` - the row it lives in sits inside a
// horizontally-scrolling table container (`overflow-x-auto`), which would otherwise clip an
// absolutely-positioned menu that extends past that container's own box.
document.addEventListener('click', function (e) {
    var toggle = e.target.closest('[data-dropdown-toggle]');
    if (toggle) {
        var root = toggle.closest('[data-dropdown]');
        var menu = root ? root.querySelector('[data-dropdown-menu]') : null;
        if (!menu) return;
        var wasOpen = !menu.classList.contains('tw:hidden');
        document.querySelectorAll('[data-dropdown-menu]').forEach(function (m) { m.classList.add('tw:hidden'); });
        if (!wasOpen) {
            var rect = toggle.getBoundingClientRect();
            menu.style.top = (rect.bottom + 4) + 'px';
            menu.style.right = (window.innerWidth - rect.right) + 'px';
            menu.classList.remove('tw:hidden');
        }
        return;
    }
    document.querySelectorAll('[data-dropdown-menu]').forEach(function (m) { m.classList.add('tw:hidden'); });
});

document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') return;
    document.querySelectorAll('[data-dropdown-menu]').forEach(function (m) { m.classList.add('tw:hidden'); });
});

// A fixed-positioned menu doesn't move with its trigger when an ancestor scrolls (e.g. the
// table's own horizontal scrollbar, or the page itself), so just close it instead of letting it
// drift away from the button that opened it.
window.addEventListener('scroll', function () {
    document.querySelectorAll('[data-dropdown-menu]:not(.tw\\:hidden)').forEach(function (m) {
        m.classList.add('tw:hidden');
    });
}, true);
