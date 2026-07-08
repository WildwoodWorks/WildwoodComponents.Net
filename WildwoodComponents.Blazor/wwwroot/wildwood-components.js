// WildwoodComponents JavaScript Interop Functions

window.wildwoodComponents = {
    // Local Storage Functions
    localStorage: {
        getItem: (key) => {
            try {
                const item = localStorage.getItem(key);
                return item ? JSON.parse(item) : null;
            } catch (e) {
                console.warn('Failed to get item from localStorage:', key, e);
                return null;
            }
        },
        setItem: (key, value) => {
            try {
                localStorage.setItem(key, JSON.stringify(value));
                return true;
            } catch (e) {
                console.warn('Failed to set item in localStorage:', key, e);
                return false;
            }
        },
        removeItem: (key) => {
            try {
                localStorage.removeItem(key);
                return true;
            } catch (e) {
                console.warn('Failed to remove item from localStorage:', key, e);
                return false;
            }
        }
    },

    // Scroll Functions
    scrollToBottom: (element) => {
        if (element) {
            element.scrollTop = element.scrollHeight;
        }
    },

    // Auto-resize textarea
    autoResizeTextarea: (element) => {
        if (element) {
            element.style.height = 'auto';
            element.style.height = Math.min(element.scrollHeight, 120) + 'px';
        }
    },

    // CAPTCHA Functions
    captcha: {
        // Google reCAPTCHA
        initializeGoogleRecaptcha: (siteKey, containerId) => {
            return new Promise((resolve, reject) => {
                if (typeof grecaptcha !== 'undefined') {
                    try {
                        const widgetId = grecaptcha.render(containerId, {
                            'sitekey': siteKey,
                            'theme': 'light',
                            'size': 'normal'
                        });
                        resolve(widgetId);
                    } catch (e) {
                        reject(e);
                    }
                } else {
                    // Load reCAPTCHA script
                    const script = document.createElement('script');
                    script.src = 'https://www.google.com/recaptcha/api.js';
                    script.onload = () => {
                        try {
                            const widgetId = grecaptcha.render(containerId, {
                                'sitekey': siteKey,
                                'theme': 'light',
                                'size': 'normal'
                            });
                            resolve(widgetId);
                        } catch (e) {
                            reject(e);
                        }
                    };
                    script.onerror = reject;
                    document.head.appendChild(script);
                }
            });
        },

        getGoogleRecaptchaResponse: () => {
            if (typeof grecaptcha !== 'undefined') {
                return grecaptcha.getResponse();
            }
            return null;
        },

        resetGoogleRecaptcha: () => {
            if (typeof grecaptcha !== 'undefined') {
                grecaptcha.reset();
            }
        },

        // hCaptcha
        initializeHCaptcha: (siteKey, containerId) => {
            return new Promise((resolve, reject) => {
                if (typeof hcaptcha !== 'undefined') {
                    try {
                        const widgetId = hcaptcha.render(containerId, {
                            'sitekey': siteKey,
                            'theme': 'light',
                            'size': 'normal'
                        });
                        resolve(widgetId);
                    } catch (e) {
                        reject(e);
                    }
                } else {
                    // Load hCaptcha script
                    const script = document.createElement('script');
                    script.src = 'https://js.hcaptcha.com/1/api.js';
                    script.onload = () => {
                        try {
                            const widgetId = hcaptcha.render(containerId, {
                                'sitekey': siteKey,
                                'theme': 'light',
                                'size': 'normal'
                            });
                            resolve(widgetId);
                        } catch (e) {
                            reject(e);
                        }
                    };
                    script.onerror = reject;
                    document.head.appendChild(script);
                }
            });
        },

        getHCaptchaResponse: () => {
            if (typeof hcaptcha !== 'undefined') {
                return hcaptcha.getResponse();
            }
            return null;
        },

        resetHCaptcha: () => {
            if (typeof hcaptcha !== 'undefined') {
                hcaptcha.reset();
            }
        }
    },

    // File Upload Functions
    fileUpload: {
        selectFile: (accept, multiple = false) => {
            return new Promise((resolve) => {
                const input = document.createElement('input');
                input.type = 'file';
                input.accept = accept || '*/*';
                input.multiple = multiple;

                input.onchange = (event) => {
                    const files = Array.from(event.target.files || []);
                    resolve(files.map(file => ({
                        name: file.name,
                        size: file.size,
                        type: file.type,
                        lastModified: file.lastModified
                    })));
                };

                input.click();
            });
        },

        readFileAsBase64: (file) => {
            return new Promise((resolve, reject) => {
                const reader = new FileReader();
                reader.onload = () => resolve(reader.result);
                reader.onerror = reject;
                reader.readAsDataURL(file);
            });
        }
    },

    // Theme Functions
    theme: {
        applyTheme: (theme) => {
            const root = document.documentElement;

            if (theme.primaryColor) {
                root.style.setProperty('--wildwood-primary', theme.primaryColor);
            }
            if (theme.secondaryColor) {
                root.style.setProperty('--wildwood-secondary', theme.secondaryColor);
            }
            if (theme.successColor) {
                root.style.setProperty('--wildwood-success', theme.successColor);
            }
            if (theme.warningColor) {
                root.style.setProperty('--wildwood-warning', theme.warningColor);
            }
            if (theme.dangerColor) {
                root.style.setProperty('--wildwood-danger', theme.dangerColor);
            }
            if (theme.infoColor) {
                root.style.setProperty('--wildwood-info', theme.infoColor);
            }
            if (theme.lightColor) {
                root.style.setProperty('--wildwood-light', theme.lightColor);
            }
            if (theme.darkColor) {
                root.style.setProperty('--wildwood-dark', theme.darkColor);
            }
            if (theme.fontFamily) {
                root.style.setProperty('--wildwood-font-family', theme.fontFamily);
            }
            if (theme.borderRadius) {
                root.style.setProperty('--wildwood-border-radius', theme.borderRadius);
            }
            if (theme.boxShadow) {
                root.style.setProperty('--wildwood-box-shadow', theme.boxShadow);
            }
        }
    },

    // Utility Functions
    utils: {
        // Copy text to clipboard
        copyToClipboard: async (text) => {
            try {
                await navigator.clipboard.writeText(text);
                return true;
            } catch (e) {
                // Fallback for older browsers
                const textArea = document.createElement('textarea');
                textArea.value = text;
                document.body.appendChild(textArea);
                textArea.focus();
                textArea.select();
                try {
                    document.execCommand('copy');
                    return true;
                } catch (err) {
                    return false;
                } finally {
                    document.body.removeChild(textArea);
                }
            }
        },

        // Show browser notification
        showNotification: (title, options = {}) => {
            if ('Notification' in window) {
                if (Notification.permission === 'granted') {
                    return new Notification(title, options);
                } else if (Notification.permission !== 'denied') {
                    Notification.requestPermission().then(permission => {
                        if (permission === 'granted') {
                            return new Notification(title, options);
                        }
                    });
                }
            }
            return null;
        },

        // Focus element
        focusElement: (element) => {
            if (element && element.focus) {
                element.focus();
            }
        },        // Get element dimensions
        getElementDimensions: (element) => {
            if (element) {
                const rect = element.getBoundingClientRect();
                return {
                    width: rect.width,
                    height: rect.height,
                    top: rect.top,
                    left: rect.left
                };
            }
            return null;
        }
    },

    // Messaging Functions
    messaging: {
        scrollToBottom: function (element) {
            if (element) {
                element.scrollTop = element.scrollHeight;
            }
        },

        scrollToMessage: function (messageId) {
            const element = document.querySelector(`[data-message-id="${messageId}"]`);
            if (element) {
                element.scrollIntoView({ behavior: 'smooth', block: 'center' });
                // Highlight the message briefly
                element.style.backgroundColor = '#fef3cd';
                setTimeout(() => {
                    element.style.backgroundColor = '';
                }, 2000);
            }
        },

        focusElement: function (element) {
            if (element) {
                element.focus();
            }
        },

        downloadFile: function (filename, base64Data, mimeType) {
            const byteCharacters = atob(base64Data);
            const byteNumbers = new Array(byteCharacters.length);
            for (let i = 0; i < byteCharacters.length; i++) {
                byteNumbers[i] = byteCharacters.charCodeAt(i);
            }
            const byteArray = new Uint8Array(byteNumbers);
            const blob = new Blob([byteArray], { type: mimeType });

            const link = document.createElement('a');
            link.href = window.URL.createObjectURL(blob);
            link.download = filename;
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
            window.URL.revokeObjectURL(link.href);
        }
    }
},

    scrollToMessage: function (messageId) {
        const element = document.querySelector(`[data-message-id="${messageId}"]`);
        if (element) {
            element.scrollIntoView({ behavior: 'smooth', block: 'center' });
            // Highlight the message briefly
            element.style.backgroundColor = '#fef3cd';
            setTimeout(() => {
                element.style.backgroundColor = '';
            }, 2000);
        }
    },

focusElement: function (element) {
    if (element) {
        element.focus();
    }
},

downloadFile: function (filename, base64Data, mimeType) {
    const byteCharacters = atob(base64Data);
    const byteNumbers = new Array(byteCharacters.length);
    for (let i = 0; i < byteCharacters.length; i++) {
        byteNumbers[i] = byteCharacters.charCodeAt(i);
    }
    const byteArray = new Uint8Array(byteNumbers);
    const blob = new Blob([byteArray], { type: mimeType });

    const link = document.createElement('a');
    link.href = window.URL.createObjectURL(blob);
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    window.URL.revokeObjectURL(link.href);
}
    }
};

// Global functions for backward compatibility
window.scrollToBottom = window.wildwoodComponents.scrollToBottom;
window.autoResizeTextarea = window.wildwoodComponents.autoResizeTextarea;

// Messaging global functions
window.scrollToMessage = window.wildwoodComponents.messaging.scrollToMessage;
window.focusElement = window.wildwoodComponents.messaging.focusElement;
window.downloadFile = window.wildwoodComponents.messaging.downloadFile;

// ────────────────────────────────────────────────────────────────
// Browser notifications (Web Notifications API) for the inbox "browser" channel.
// Mirrors @wildwood/core browserNotifications.ts. Web Notifications API only —
// no service worker / VAPID. All members are safe to call anywhere (no-op when
// the API is unavailable) and never throw. Called from the Blazor
// NotificationsBell + NotificationPreferences components via JSRuntime.
// ────────────────────────────────────────────────────────────────
window.wildwoodBrowserNotifications = {
    // True only when the Web Notifications API is present (browser main thread).
    isSupported: function () {
        return typeof window !== 'undefined' && 'Notification' in window;
    },

    // Current permission ('default' | 'granted' | 'denied'), or 'unsupported'.
    getPermission: function () {
        if (!window.wildwoodBrowserNotifications.isSupported()) return 'unsupported';
        return Notification.permission;
    },

    // Prompts for permission; resolves to the resulting permission or 'unsupported'.
    requestPermission: async function () {
        if (!window.wildwoodBrowserNotifications.isSupported()) return 'unsupported';
        try {
            // Modern browsers return a promise; older Safari used a callback (ignored here).
            return await Notification.requestPermission();
        } catch (e) {
            return window.wildwoodBrowserNotifications.getPermission();
        }
    },

    // Raises a native notification. No-op when unsupported or permission !== 'granted'.
    show: function (title, body, tag) {
        if (!window.wildwoodBrowserNotifications.isSupported()) return;
        if (Notification.permission !== 'granted') return;
        try {
            const notification = new Notification(title, {
                body: body || undefined,
                tag: tag || undefined
            });
            notification.onclick = function () {
                try { window.focus(); } catch (e) { /* focus can throw in embedded contexts */ }
            };
        } catch (e) {
            // Constructing a Notification can throw on platforms that require a service
            // worker (e.g. mobile Chrome). Degrade silently — the in-app inbox row remains.
        }
    }
};
