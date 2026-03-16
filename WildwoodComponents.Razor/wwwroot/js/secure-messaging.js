/**
 * WildwoodComponents.Razor - Secure Messaging Component JavaScript
 * Handles thread navigation, message sending, typing indicators, reactions,
 * file attachments, and user search.
 * Razor Pages equivalent of the Blazor SecureMessagingComponent interactivity.
 */
(function () {
    'use strict';

    var roots = document.querySelectorAll('.ww-messaging-component');
    for (var r = 0; r < roots.length; r++) {
        initMessaging(roots[r]);
    }

    function initMessaging(root) {
        var cid = root.dataset.componentId;
        var companyAppId = root.dataset.companyAppId;
        var proxyUrl = root.dataset.proxyUrl;
        var enableReactions = root.dataset.enableReactions === 'true';
        var enableAttachments = root.dataset.enableAttachments === 'true';
        var enableTyping = root.dataset.enableTyping === 'true';

        var messageArea = root.querySelector('.ww-message-area');
        var messageInput = root.querySelector('.ww-message-input');
        var sendBtn = root.querySelector('.ww-send-message-btn');
        var messageList = root.querySelector('.ww-message-list');
        var typingIndicator = root.querySelector('.ww-typing-indicator');

        var currentThreadId = null;
        var typingTimer = null;
        var isTyping = false;

        // ===== HELPERS =====

        function apiCall(method, path, body) {
            var opts = {
                method: method,
                headers: { 'Content-Type': 'application/json' }
            };
            if (body) opts.body = JSON.stringify(body);
            return fetch(proxyUrl + path, opts).then(function (r) {
                var ct = r.headers.get('content-type') || '';
                if (ct.indexOf('application/json') !== -1) {
                    return r.json().then(function (data) {
                        if (!r.ok) throw new Error(data.error || data.message || 'Request failed');
                        return data;
                    });
                }
                if (!r.ok) throw new Error('Request failed (' + r.status + ')');
                return r.text();
            });
        }

        function showMessage(text, type) {
            var msgEl = root.querySelector('.ww-messaging-message');
            if (!msgEl) return;
            msgEl.textContent = text;
            msgEl.className = 'ww-messaging-message alert alert-' + type;
            msgEl.style.display = '';
            setTimeout(function () { msgEl.style.display = 'none'; }, 5000);
        }

        function scrollToBottom() {
            if (messageList) messageList.scrollTop = messageList.scrollHeight;
        }

        function formatTime(date) {
            var d = new Date(date);
            var now = new Date();
            var diff = now - d;
            if (diff < 60000) return 'Just now';
            if (diff < 3600000) return Math.floor(diff / 60000) + 'm ago';
            if (diff < 86400000) return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
            return d.toLocaleDateString([], { month: 'short', day: 'numeric' });
        }

        // ===== THREAD SELECTION =====

        root.addEventListener('click', function (e) {
            var threadItem = e.target.closest('.ww-thread-item');
            if (!threadItem) return;

            var threadId = threadItem.dataset.threadId;
            loadThread(threadId);

            // Update active state
            var items = root.querySelectorAll('.ww-thread-item');
            for (var i = 0; i < items.length; i++) items[i].classList.remove('active');
            threadItem.classList.add('active');
            threadItem.classList.remove('ww-thread-unread');

            // Show message area
            var emptyState = root.querySelector('.ww-chat-empty');
            if (emptyState) emptyState.style.display = 'none';
            if (messageArea) messageArea.style.display = '';
        });

        function loadThread(threadId) {
            currentThreadId = threadId;

            apiCall('GET', '/threads/' + threadId + '/messages')
                .then(function (messages) {
                    if (messageList) {
                        messageList.innerHTML = '';
                        for (var i = 0; i < messages.length; i++) {
                            appendMessage(messages[i]);
                        }
                        scrollToBottom();
                    }

                    // Update header
                    var header = root.querySelector('.ww-chat-header-name');
                    var threadItem = root.querySelector('.ww-thread-item[data-thread-id="' + threadId + '"]');
                    if (header && threadItem) {
                        var name = threadItem.querySelector('.ww-thread-name');
                        if (name) header.textContent = name.textContent;
                    }

                    // Mark as read
                    apiCall('POST', '/threads/' + threadId + '/read').catch(function () { /* non-critical: mark-read */ });
                })
                .catch(function (err) {
                    showMessage('Failed to load messages: ' + err.message, 'danger');
                });
        }

        function appendMessage(msg) {
            if (!messageList) return;
            var div = document.createElement('div');
            div.className = 'ww-msg-item d-flex gap-2 mb-3' + (msg.isOwnMessage ? ' flex-row-reverse' : '');
            div.dataset.messageId = msg.id;

            var bubble = document.createElement('div');
            bubble.className = 'ww-msg-bubble p-2 rounded ' + (msg.isOwnMessage ? 'bg-primary text-white' : 'bg-light');
            bubble.style.maxWidth = '70%';

            var content = document.createElement('div');
            content.className = 'ww-msg-content';
            content.textContent = msg.content || msg.text || '';
            bubble.appendChild(content);

            var meta = document.createElement('div');
            meta.className = 'ww-msg-meta small ' + (msg.isOwnMessage ? 'text-white-50' : 'text-muted');
            meta.textContent = formatTime(msg.createdAt || msg.sentAt);
            if (msg.senderName && !msg.isOwnMessage) {
                meta.textContent = msg.senderName + ' · ' + meta.textContent;
            }
            bubble.appendChild(meta);

            // Reactions
            if (enableReactions && msg.reactions && msg.reactions.length > 0) {
                var reactionsDiv = document.createElement('div');
                reactionsDiv.className = 'ww-msg-reactions mt-1';
                for (var i = 0; i < msg.reactions.length; i++) {
                    var r = msg.reactions[i];
                    var badge = document.createElement('span');
                    badge.className = 'badge bg-light text-dark me-1';
                    badge.textContent = r.emoji + ' ' + (r.count || 1);
                    reactionsDiv.appendChild(badge);
                }
                bubble.appendChild(reactionsDiv);
            }

            div.appendChild(bubble);
            messageList.appendChild(div);
        }

        // ===== SEND MESSAGE =====

        function sendMsg() {
            if (!currentThreadId || !messageInput) return;
            var text = messageInput.value.trim();
            if (!text) return;

            messageInput.value = '';
            updateSendButton();

            // Stop typing indicator
            if (isTyping) {
                apiCall('POST', '/threads/' + currentThreadId + '/typing/stop').catch(function () { /* non-critical: typing indicator */ });
                isTyping = false;
            }

            apiCall('POST', '/threads/' + currentThreadId + '/messages', { content: text })
                .then(function (msg) {
                    appendMessage(Object.assign(msg, { isOwnMessage: true }));
                    scrollToBottom();

                    // Update thread preview
                    var threadItem = root.querySelector('.ww-thread-item[data-thread-id="' + currentThreadId + '"]');
                    if (threadItem) {
                        var preview = threadItem.querySelector('.ww-thread-preview');
                        if (preview) preview.textContent = text;
                        var time = threadItem.querySelector('.ww-thread-time');
                        if (time) time.textContent = 'Just now';
                    }
                })
                .catch(function (err) {
                    showMessage('Failed to send: ' + err.message, 'danger');
                });
        }

        function updateSendButton() {
            if (sendBtn && messageInput) {
                sendBtn.disabled = !messageInput.value.trim();
            }
        }

        if (messageInput) {
            messageInput.addEventListener('input', function () {
                updateSendButton();
                handleTyping();
            });
            messageInput.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    sendMsg();
                }
            });
        }

        if (sendBtn) {
            sendBtn.addEventListener('click', sendMsg);
        }

        // ===== TYPING INDICATORS =====

        function handleTyping() {
            if (!enableTyping || !currentThreadId) return;

            if (!isTyping) {
                isTyping = true;
                apiCall('POST', '/threads/' + currentThreadId + '/typing/start').catch(function () { /* non-critical: typing indicator */ });
            }

            if (typingTimer) clearTimeout(typingTimer);
            typingTimer = setTimeout(function () {
                isTyping = false;
                apiCall('POST', '/threads/' + currentThreadId + '/typing/stop').catch(function () { /* non-critical: typing indicator */ });
            }, 3000);
        }

        // ===== NEW THREAD =====

        var newThreadBtn = root.querySelector('.ww-new-thread-btn');
        if (newThreadBtn) {
            newThreadBtn.addEventListener('click', function () {
                var recipient = prompt('Enter recipient username or email:');
                if (!recipient) return;

                apiCall('POST', '/threads', {
                    companyAppId: companyAppId,
                    participants: [recipient],
                    threadType: 'Direct'
                })
                    .then(function (thread) {
                        window.location.reload();
                    })
                    .catch(function (err) {
                        showMessage('Failed to create conversation: ' + err.message, 'danger');
                    });
            });
        }

        // ===== USER SEARCH =====

        var searchInput = root.querySelector('.ww-user-search-input');
        var searchResults = root.querySelector('.ww-user-search-results');
        var searchTimer = null;

        if (searchInput && searchResults) {
            searchInput.addEventListener('input', function () {
                var query = searchInput.value.trim();
                if (query.length < 2) {
                    searchResults.style.display = 'none';
                    return;
                }
                if (searchTimer) clearTimeout(searchTimer);
                searchTimer = setTimeout(function () {
                    apiCall('GET', '/users/search?q=' + encodeURIComponent(query) + '&companyAppId=' + companyAppId)
                        .then(function (users) {
                            searchResults.innerHTML = '';
                            if (users.length === 0) {
                                searchResults.innerHTML = '<div class="p-2 text-muted small">No users found</div>';
                            } else {
                                for (var i = 0; i < users.length; i++) {
                                    var user = users[i];
                                    var item = document.createElement('div');
                                    item.className = 'ww-search-result-item p-2 border-bottom';
                                    item.style.cursor = 'pointer';
                                    item.textContent = user.userName || user.email || user.id;
                                    item.dataset.userId = user.id;
                                    item.addEventListener('click', function () {
                                        apiCall('POST', '/threads', {
                                            companyAppId: companyAppId,
                                            participants: [this.dataset.userId],
                                            threadType: 'Direct'
                                        })
                                            .then(function () { window.location.reload(); })
                                            .catch(function (err) { showMessage('Failed: ' + err.message, 'danger'); });
                                    });
                                    searchResults.appendChild(item);
                                }
                            }
                            searchResults.style.display = '';
                        })
                        .catch(function () {
                            searchResults.style.display = 'none';
                        });
                }, 300);
            });

            // Hide results on click outside
            document.addEventListener('click', function (e) {
                if (!searchInput.contains(e.target) && !searchResults.contains(e.target)) {
                    searchResults.style.display = 'none';
                }
            });
        }

        // ===== FILE ATTACHMENTS =====

        if (enableAttachments) {
            var attachBtn = root.querySelector('.ww-attach-file-btn');
            var fileInput = root.querySelector('.ww-file-input');

            if (attachBtn && fileInput) {
                attachBtn.addEventListener('click', function () { fileInput.click(); });
                fileInput.addEventListener('change', function () {
                    var file = this.files[0];
                    if (!file || !currentThreadId) return;

                    var formData = new FormData();
                    formData.append('file', file);
                    formData.append('threadId', currentThreadId);

                    fetch(proxyUrl + '/attachments', { method: 'POST', body: formData })
                        .then(function (r) {
                            if (!r.ok) throw new Error('Upload failed');
                            return r.json();
                        })
                        .then(function () {
                            showMessage('File attached successfully.', 'success');
                            loadThread(currentThreadId);
                        })
                        .catch(function (err) {
                            showMessage('Upload failed: ' + err.message, 'danger');
                        });

                    fileInput.value = '';
                });
            }
        }

        // Initialize send button state
        updateSendButton();
    }
})();
