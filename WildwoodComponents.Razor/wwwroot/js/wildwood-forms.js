/*
 * Wildwood form shim.
 *
 * Some Wildwood components render a <form> and rely on three things only a form
 * element provides: the submit event, aggregate checkValidity(), and reset().
 *
 * That works everywhere except inside classic ASP.NET WebForms, where the whole page
 * already sits in one <form runat="server">. HTML forbids nested forms, so the browser
 * discards the inner start tag: the element vanishes from the DOM, getElementById
 * returns null, and any type="submit" button posts the WebForms page back instead.
 * There, the components render the same markup as <div data-ww-form> and load this file.
 *
 * The three helpers below work with either shape. When the container really is a form
 * they defer to the browser, so behaviour on every other stack is unchanged; when it is
 * a div they reproduce the same semantics over its fields. Nothing here is required:
 * a page that does not load this file keeps the native behaviour it always had.
 */
(function (window, document) {
    'use strict';

    if (window.WildwoodForms) {
        return; // Already loaded; a page may host more than one component.
    }

    var FIELDS = 'input, select, textarea';

    /*
     * Input types for which HTML performs implicit submission on Enter. Checkbox, radio,
     * file, range and colour are deliberately absent: pressing Enter on those does not
     * submit a real form, and treating them as if it did would submit the component
     * earlier than the same markup does on every other stack.
     */
    var IMPLICIT_SUBMIT_TYPES = {
        text: true, search: true, url: true, tel: true, email: true, password: true,
        date: true, month: true, week: true, time: true, 'datetime-local': true,
        number: true
    };

    function isForm(el) {
        return !!el && el.tagName === 'FORM';
    }

    /**
     * Fields that take part in constraint validation. Disabled fields and buttons are
     * excluded, matching what a form's own checkValidity() considers.
     */
    function candidates(container) {
        var all = container.querySelectorAll(FIELDS);
        var result = [];
        for (var i = 0; i < all.length; i++) {
            var el = all[i];
            if (el.disabled) continue;
            if (el.type === 'submit' || el.type === 'button' || el.type === 'reset') continue;
            if (typeof el.checkValidity !== 'function') continue;
            result.push(el);
        }
        return result;
    }

    /**
     * Runs handler when the container is "submitted": the real submit event for a form,
     * or activation of its submit control for a div — click, plus Enter from a single-line
     * field, which is what a form would have done.
     */
    function onSubmit(container, handler) {
        if (!container || typeof handler !== 'function') return;

        if (isForm(container)) {
            container.addEventListener('submit', handler);
            return;
        }

        var submitter = container.querySelector(
            '[data-ww-submit], button[type="submit"], input[type="submit"], .ww-submit');

        if (submitter) {
            submitter.addEventListener('click', function (e) {
                e.preventDefault();
                handler.call(container, e);
            });
        }

        container.addEventListener('keydown', function (e) {
            if (e.key !== 'Enter' && e.keyCode !== 13) return;

            var target = e.target;
            if (!target || target.tagName !== 'INPUT') return;
            if (!IMPLICIT_SUBMIT_TYPES[(target.type || 'text').toLowerCase()]) return;

            e.preventDefault();
            handler.call(container, e);
        });
    }

    /**
     * Aggregate constraint validation. An individual field's checkValidity() works
     * outside a form, including any message set with setCustomValidity(), so a div is
     * validated by asking each of its fields in turn.
     *
     * Like form.checkValidity(), this reports a verdict and shows nothing: it must not
     * call reportValidity(). The components carry novalidate and render their feedback
     * through Bootstrap's .was-validated styling, so a native validation bubble here
     * would be UI that appears on no other stack.
     */
    function checkValidity(container) {
        if (!container) return true;
        if (isForm(container)) return container.checkValidity();

        var fields = candidates(container);
        var valid = true;

        for (var i = 0; i < fields.length; i++) {
            if (!fields[i].checkValidity()) {
                valid = false;
            }
        }

        return valid;
    }

    /** Restores fields to their initial values, as form.reset() would. */
    function reset(container) {
        if (!container) return;
        if (isForm(container)) {
            container.reset();
            return;
        }

        var fields = container.querySelectorAll(FIELDS);
        for (var i = 0; i < fields.length; i++) {
            var el = fields[i];
            if (el.type === 'checkbox' || el.type === 'radio') {
                el.checked = el.defaultChecked;
            } else if (el.tagName === 'SELECT') {
                for (var j = 0; j < el.options.length; j++) {
                    el.options[j].selected = el.options[j].defaultSelected;
                }
            } else if (el.type !== 'submit' && el.type !== 'button' && el.type !== 'reset') {
                // Values only. A native reset() leaves any setCustomValidity message in
                // place, and clearing it here would be a behaviour this shim invented.
                el.value = el.defaultValue;
            }
        }
    }

    window.WildwoodForms = {
        isForm: isForm,
        onSubmit: onSubmit,
        checkValidity: checkValidity,
        reset: reset
    };
})(window, document);
