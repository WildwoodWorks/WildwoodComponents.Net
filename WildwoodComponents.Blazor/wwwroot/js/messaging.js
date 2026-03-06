// messaging.js - JavaScript interop for SecureMessagingComponent

window.wildwoodMessaging = {
    // Scroll functions
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

    // Focus functions
    focusElement: function (element) {
        if (element) {
            element.focus();
        }
    },

    // File download function
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
    },

    // Auto-resize textarea
    autoResizeTextarea: function (element) {
        if (element) {
            element.style.height = 'auto';
            element.style.height = Math.min(element.scrollHeight, 96) + 'px'; // Max 6rem
        }
    },

    // Setup textarea auto-resize
    setupTextareaAutoResize: function (element) {
        if (element) {
            element.addEventListener('input', function () {
                wildwoodMessaging.autoResizeTextarea(this);
            });
        }
    },

    // Notification functions
    requestNotificationPermission: async function () {
        if ('Notification' in window) {
            return await Notification.requestPermission();
        }
        return 'denied';
    },

    showNotification: function (title, options = {}) {
        if ('Notification' in window && Notification.permission === 'granted') {
            return new Notification(title, {
                icon: options.icon || '/favicon.ico',
                body: options.body || '',
                tag: options.tag || 'wildwood-message',
                ...options
            });
        }
        return null;
    },

    // Sound notifications
    playNotificationSound: function (soundUrl = null) {
        try {
            const audio = new Audio(soundUrl || '/sounds/notification.mp3');
            audio.volume = 0.5;
            audio.play().catch(e => console.log('Could not play notification sound:', e));
        } catch (e) {
            console.log('Could not play notification sound:', e);
        }
    },

    // Vibration (mobile)
    vibrate: function (pattern = [200]) {
        if ('vibrate' in navigator) {
            navigator.vibrate(pattern);
        }
    },

    // Clipboard functions
    copyToClipboard: async function (text) {
        if (navigator.clipboard && window.isSecureContext) {
            try {
                await navigator.clipboard.writeText(text);
                return true;
            } catch (err) {
                console.error('Failed to copy to clipboard:', err);
                return false;
            }
        } else {
            // Fallback for older browsers
            const textArea = document.createElement('textarea');
            textArea.value = text;
            textArea.style.position = 'fixed';
            textArea.style.left = '-999999px';
            textArea.style.top = '-999999px';
            document.body.appendChild(textArea);
            textArea.focus();
            textArea.select();

            try {
                const successful = document.execCommand('copy');
                document.body.removeChild(textArea);
                return successful;
            } catch (err) {
                console.error('Failed to copy to clipboard:', err);
                document.body.removeChild(textArea);
                return false;
            }
        }
    },

    // Image preview functions
    showImagePreview: function (imageUrl, filename) {
        // Create modal overlay
        const overlay = document.createElement('div');
        overlay.className = 'image-preview-overlay';
        overlay.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: rgba(0, 0, 0, 0.9);
            display: flex;
            align-items: center;
            justify-content: center;
            z-index: 10000;
            cursor: pointer;
        `;

        // Create image container
        const container = document.createElement('div');
        container.style.cssText = `
            max-width: 90%;
            max-height: 90%;
            position: relative;
        `;

        // Create image
        const img = document.createElement('img');
        img.src = imageUrl;
        img.alt = filename;
        img.style.cssText = `
            max-width: 100%;
            max-height: 100%;
            object-fit: contain;
        `;

        // Create close button
        const closeBtn = document.createElement('button');
        closeBtn.innerHTML = '×';
        closeBtn.style.cssText = `
            position: absolute;
            top: -40px;
            right: 0;
            background: white;
            border: none;
            width: 30px;
            height: 30px;
            border-radius: 50%;
            cursor: pointer;
            font-size: 20px;
            font-weight: bold;
        `;

        // Add event listeners
        const closePreview = () => document.body.removeChild(overlay);
        overlay.addEventListener('click', closePreview);
        closeBtn.addEventListener('click', closePreview);

        // Prevent closing when clicking on image
        container.addEventListener('click', (e) => e.stopPropagation());

        // Escape key to close
        const escapeHandler = (e) => {
            if (e.key === 'Escape') {
                closePreview();
                document.removeEventListener('keydown', escapeHandler);
            }
        };
        document.addEventListener('keydown', escapeHandler);

        // Assemble and show
        container.appendChild(img);
        container.appendChild(closeBtn);
        overlay.appendChild(container);
        document.body.appendChild(overlay);
    },

    // Emoji picker (basic implementation)
    showEmojiPicker: function (targetElement, callback) {
        const emojis = ['👍', '👎', '❤️', '😂', '😮', '😢', '😡', '👏', '🎉', '🔥'];

        // Remove existing picker
        const existingPicker = document.querySelector('.emoji-picker');
        if (existingPicker) {
            existingPicker.remove();
        }

        // Create picker
        const picker = document.createElement('div');
        picker.className = 'emoji-picker';
        picker.style.cssText = `
            position: absolute;
            background: white;
            border: 1px solid #e5e7eb;
            border-radius: 8px;
            padding: 8px;
            box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
            z-index: 1000;
            display: flex;
            gap: 4px;
        `;

        // Add emojis
        emojis.forEach(emoji => {
            const button = document.createElement('button');
            button.textContent = emoji;
            button.style.cssText = `
                background: none;
                border: none;
                font-size: 20px;
                padding: 4px;
                border-radius: 4px;
                cursor: pointer;
                transition: background 0.2s;
            `;
            button.addEventListener('mouseenter', () => {
                button.style.background = '#f3f4f6';
            });
            button.addEventListener('mouseleave', () => {
                button.style.background = 'none';
            });
            button.addEventListener('click', () => {
                callback(emoji);
                picker.remove();
            });
            picker.appendChild(button);
        });

        // Position picker
        if (targetElement) {
            const rect = targetElement.getBoundingClientRect();
            picker.style.left = rect.left + 'px';
            picker.style.top = (rect.top - picker.offsetHeight - 10) + 'px';
        }

        // Close on outside click
        const closeHandler = (e) => {
            if (!picker.contains(e.target)) {
                picker.remove();
                document.removeEventListener('click', closeHandler);
            }
        };
        setTimeout(() => document.addEventListener('click', closeHandler), 0);

        document.body.appendChild(picker);
    },

    // Theme management
    applyTheme: function (themeVariables) {
        const root = document.documentElement;
        Object.entries(themeVariables).forEach(([property, value]) => {
            root.style.setProperty(property, value);
        });
    },

    // Online status detection
    isOnline: function () {
        return navigator.onLine;
    },

    onOnlineStatusChange: function (callback) {
        window.addEventListener('online', () => callback(true));
        window.addEventListener('offline', () => callback(false));
    },

    // Page visibility detection (for read receipts, typing indicators)
    isPageVisible: function () {
        return !document.hidden;
    },

    onVisibilityChange: function (callback) {
        document.addEventListener('visibilitychange', () => {
            callback(!document.hidden);
        });
    },

    // Local storage helpers
    setLocalStorage: function (key, value) {
        try {
            localStorage.setItem(key, JSON.stringify(value));
            return true;
        } catch (e) {
            console.error('Failed to set localStorage:', e);
            return false;
        }
    },

    getLocalStorage: function (key) {
        try {
            const item = localStorage.getItem(key);
            return item ? JSON.parse(item) : null;
        } catch (e) {
            console.error('Failed to get localStorage:', e);
            return null;
        }
    },

    removeLocalStorage: function (key) {
        try {
            localStorage.removeItem(key);
            return true;
        } catch (e) {
            console.error('Failed to remove localStorage:', e);
            return false;
        }
    },

    // Initialize messaging component
    initialize: function () {
        console.log('WildwoodMessaging initialized');

        // Setup global styles
        if (!document.querySelector('#wildwood-messaging-styles')) {
            const style = document.createElement('style');
            style.id = 'wildwood-messaging-styles';
            style.textContent = `
                .image-preview-overlay {
                    position: fixed !important;
                    top: 0 !important;
                    left: 0 !important;
                    right: 0 !important;
                    bottom: 0 !important;
                    background: rgba(0, 0, 0, 0.9) !important;
                    display: flex !important;
                    align-items: center !important;
                    justify-content: center !important;
                    z-index: 10000 !important;
                    cursor: pointer !important;
                }
                
                .emoji-picker {
                    position: absolute !important;
                    background: white !important;
                    border: 1px solid #e5e7eb !important;
                    border-radius: 8px !important;
                    padding: 8px !important;
                    box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1) !important;
                    z-index: 1000 !important;
                    display: flex !important;
                    gap: 4px !important;
                }
            `;
            document.head.appendChild(style);
        }
    }
};

// Auto-initialize when script loads
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        wildwoodMessaging.initialize();
    });
} else {
    wildwoodMessaging.initialize();
}
