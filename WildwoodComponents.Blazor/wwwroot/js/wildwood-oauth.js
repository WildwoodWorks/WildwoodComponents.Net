/**
 * WildwoodComponents OAuth popup management.
 * Opens provider authorization URLs in a popup window and listens for callback results.
 */
window.wildwoodOAuth = {

    _popup: null,
    _pollTimer: null,

    /**
     * Opens an OAuth popup window and returns a promise that resolves with the auth result.
     * @param {string} authorizationUrl - The full OAuth authorization URL to open
     * @returns {Promise<object>} - Resolves with { success, response, error }
     */
    openPopup: function (authorizationUrl) {
        return new Promise(function (resolve, reject) {
            // Close any existing popup
            if (window.wildwoodOAuth._popup && !window.wildwoodOAuth._popup.closed) {
                window.wildwoodOAuth._popup.close();
            }

            // Calculate centered popup position
            var width = 500;
            var height = 650;
            var left = (window.screen.width - width) / 2;
            var top = (window.screen.height - height) / 2;

            // Open the popup
            var popup = window.open(
                authorizationUrl,
                'wildwood-oauth-popup',
                'width=' + width + ',height=' + height + ',left=' + left + ',top=' + top +
                ',menubar=no,toolbar=no,location=yes,status=no,scrollbars=yes,resizable=yes'
            );

            if (!popup) {
                reject('Popup was blocked by the browser. Please allow popups for this site.');
                return;
            }

            window.wildwoodOAuth._popup = popup;
            var resolved = false;

            // Listen for postMessage from the callback page
            function onMessage(event) {
                if (event.data && event.data.type === 'wildwood-oauth-callback') {
                    resolved = true;
                    cleanup();
                    resolve(event.data);
                }
            }

            function cleanup() {
                window.removeEventListener('message', onMessage);
                if (window.wildwoodOAuth._pollTimer) {
                    clearInterval(window.wildwoodOAuth._pollTimer);
                    window.wildwoodOAuth._pollTimer = null;
                }
            }

            window.addEventListener('message', onMessage);

            // Poll to detect if the popup was closed without completing
            window.wildwoodOAuth._pollTimer = setInterval(function () {
                if (popup.closed && !resolved) {
                    resolved = true;
                    cleanup();
                    resolve({ success: false, error: 'Login popup was closed' });
                }
            }, 500);
        });
    },

    /**
     * Checks if popups are likely supported/allowed.
     * @returns {boolean}
     */
    isPopupSupported: function () {
        // Test by trying to detect known popup-blocking scenarios
        return typeof window.open === 'function';
    }
};
