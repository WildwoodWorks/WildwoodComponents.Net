/**
 * WildwoodComponents.Razor - Two-Factor Settings Component JavaScript
 * Handles authenticator enrollment, email 2FA, credential management,
 * trusted device revocation, and recovery code regeneration.
 * Razor Pages equivalent of the Blazor TwoFactorSettingsComponent interactivity.
 */
(function () {
    'use strict';

    var roots = document.querySelectorAll('.ww-2fa-settings-component');
    for (var r = 0; r < roots.length; r++) {
        init2FA(roots[r]);
    }

    function init2FA(root) {
        var cid = root.dataset.componentId;
        var proxyUrl = root.dataset.proxyUrl;
        var messageEl = document.getElementById('ww-2fa-message-' + cid);

        // ===== HELPERS =====

        function showMessage(text, type) {
            if (!messageEl) return;
            messageEl.textContent = text;
            messageEl.className = 'ww-alert ww-alert-' + type;
            messageEl.style.display = '';
            root.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }

        function clearMessage() {
            if (messageEl) messageEl.style.display = 'none';
        }

        function setButtonLoading(btn, loading) {
            if (!btn) return;
            var text = btn.querySelector('.ww-btn-text');
            var spinner = btn.querySelector('.ww-btn-spinner');
            if (text) text.style.display = loading ? 'none' : '';
            if (spinner) spinner.style.display = loading ? '' : 'none';
            btn.disabled = loading;
        }

        function apiCall(method, path, body) {
            var opts = {
                method: method,
                headers: { 'Content-Type': 'application/json' }
            };
            if (body) opts.body = JSON.stringify(body);
            return fetch(proxyUrl + path, opts).then(function (r) {
                if (!r.ok) {
                    return r.text().then(function (t) {
                        throw new Error(t || 'Request failed (' + r.status + ')');
                    });
                }
                var ct = r.headers.get('content-type') || '';
                if (ct.indexOf('application/json') !== -1) return r.json();
                return r.text();
            });
        }

        // ===== CONFIRMATION MODAL =====

        var confirmModal = null;
        var confirmModalEl = document.getElementById('ww-2fa-confirm-modal-' + cid);
        var confirmTitleEl = document.getElementById('ww-2fa-confirm-title-' + cid);
        var confirmMessageEl = document.getElementById('ww-2fa-confirm-message-' + cid);
        var confirmActionBtn = document.getElementById('ww-2fa-confirm-action-' + cid);
        var pendingConfirmAction = null;

        // Bootstrap Modal initialization (if Bootstrap is available)
        if (confirmModalEl && typeof bootstrap !== 'undefined') {
            confirmModal = new bootstrap.Modal(confirmModalEl);
        }

        function showConfirm(title, message, action) {
            if (confirmTitleEl) confirmTitleEl.textContent = title;
            if (confirmMessageEl) confirmMessageEl.textContent = message;
            pendingConfirmAction = action;
            if (confirmModal) {
                confirmModal.show();
            } else if (confirmModalEl) {
                // Fallback: show modal manually
                confirmModalEl.classList.add('show');
                confirmModalEl.style.display = 'block';
                confirmModalEl.setAttribute('aria-hidden', 'false');
            }
        }

        function hideConfirm() {
            pendingConfirmAction = null;
            if (confirmModal) {
                confirmModal.hide();
            } else if (confirmModalEl) {
                confirmModalEl.classList.remove('show');
                confirmModalEl.style.display = 'none';
                confirmModalEl.setAttribute('aria-hidden', 'true');
            }
        }

        if (confirmActionBtn) {
            confirmActionBtn.addEventListener('click', function () {
                if (pendingConfirmAction) {
                    setButtonLoading(confirmActionBtn, true);
                    pendingConfirmAction()
                        .finally(function () {
                            setButtonLoading(confirmActionBtn, false);
                            hideConfirm();
                        });
                }
            });
        }

        // ===== AUTHENTICATOR ENROLLMENT =====

        var authEnrollPanel = document.getElementById('ww-2fa-authenticator-enroll-' + cid);
        var authBeginSection = document.getElementById('ww-2fa-authenticator-begin-' + cid);
        var authQRArea = document.getElementById('ww-2fa-qr-area-' + cid);
        var authVerifySection = document.getElementById('ww-2fa-authenticator-verify-' + cid);
        var authNameInput = document.getElementById('ww-2fa-authenticator-name-' + cid);
        var authCodeInput = document.getElementById('ww-2fa-authenticator-code-' + cid);
        var authCredentialIdInput = document.getElementById('ww-2fa-authenticator-credential-id-' + cid);
        var authQRImg = document.getElementById('ww-2fa-qr-img-' + cid);
        var authManualKey = document.getElementById('ww-2fa-manual-key-value-' + cid);

        // "Add Authenticator" button - shows enrollment panel
        var addAuthBtn = root.querySelector('.ww-2fa-add-authenticator-btn');
        if (addAuthBtn) {
            addAuthBtn.addEventListener('click', function () {
                clearMessage();
                if (authEnrollPanel) authEnrollPanel.style.display = '';
                if (authBeginSection) authBeginSection.style.display = '';
                if (authQRArea) authQRArea.style.display = 'none';
                if (authVerifySection) authVerifySection.style.display = 'none';
                if (authNameInput) authNameInput.value = '';
                addAuthBtn.style.display = 'none';
            });
        }

        // "Generate QR Code" button
        var beginAuthBtn = root.querySelector('.ww-2fa-begin-authenticator-btn');
        if (beginAuthBtn) {
            beginAuthBtn.addEventListener('click', function () {
                clearMessage();
                setButtonLoading(beginAuthBtn, true);
                var friendlyName = authNameInput ? authNameInput.value.trim() : '';

                apiCall('POST', '/authenticator/enroll', friendlyName ? { friendlyName: friendlyName } : {})
                    .then(function (result) {
                        if (authCredentialIdInput) authCredentialIdInput.value = result.credentialId || '';
                        if (authQRImg) authQRImg.src = result.qrCodeUri || result.qrCodeDataUri || '';
                        if (authManualKey) authManualKey.textContent = result.manualEntryKey || result.secretKey || '';
                        if (authBeginSection) authBeginSection.style.display = 'none';
                        if (authQRArea) authQRArea.style.display = '';
                        if (authVerifySection) authVerifySection.style.display = '';
                        if (authCodeInput) authCodeInput.focus();
                    })
                    .catch(function (err) {
                        showMessage('Failed to start authenticator enrollment: ' + err.message, 'danger');
                    })
                    .finally(function () {
                        setButtonLoading(beginAuthBtn, false);
                    });
            });
        }

        // "Verify & Enable" authenticator button
        var verifyAuthBtn = root.querySelector('.ww-2fa-verify-authenticator-btn');
        if (verifyAuthBtn) {
            verifyAuthBtn.addEventListener('click', function () {
                var code = authCodeInput ? authCodeInput.value.trim() : '';
                var credentialId = authCredentialIdInput ? authCredentialIdInput.value : '';

                if (!code || code.length !== 6) {
                    if (authCodeInput) authCodeInput.classList.add('is-invalid');
                    return;
                }
                if (authCodeInput) authCodeInput.classList.remove('is-invalid');

                clearMessage();
                setButtonLoading(verifyAuthBtn, true);

                apiCall('POST', '/authenticator/verify', { credentialId: credentialId, code: code })
                    .then(function () {
                        showMessage('Authenticator enrolled successfully!', 'success');
                        resetEnrollment();
                        reloadPage();
                    })
                    .catch(function (err) {
                        showMessage('Verification failed: ' + err.message, 'danger');
                    })
                    .finally(function () {
                        setButtonLoading(verifyAuthBtn, false);
                    });
            });
        }

        // ===== EMAIL 2FA ENROLLMENT =====

        var emailEnrollPanel = document.getElementById('ww-2fa-email-enroll-' + cid);
        var emailBeginSection = document.getElementById('ww-2fa-email-begin-' + cid);
        var emailVerifySection = document.getElementById('ww-2fa-email-verify-' + cid);
        var emailAddressInput = document.getElementById('ww-2fa-email-address-' + cid);
        var emailCodeInput = document.getElementById('ww-2fa-email-code-' + cid);
        var emailCredentialIdInput = document.getElementById('ww-2fa-email-credential-id-' + cid);
        var emailSentTo = document.getElementById('ww-2fa-email-sent-to-' + cid);

        // "Add Email" button
        var addEmailBtn = root.querySelector('.ww-2fa-add-email-btn');
        if (addEmailBtn) {
            addEmailBtn.addEventListener('click', function () {
                clearMessage();
                if (emailEnrollPanel) emailEnrollPanel.style.display = '';
                if (emailBeginSection) emailBeginSection.style.display = '';
                if (emailVerifySection) emailVerifySection.style.display = 'none';
                if (emailAddressInput) emailAddressInput.value = '';
                addEmailBtn.style.display = 'none';
            });
        }

        // "Send Verification Code" button
        var sendEmailBtn = root.querySelector('.ww-2fa-send-email-btn');
        if (sendEmailBtn) {
            sendEmailBtn.addEventListener('click', function () {
                clearMessage();
                setButtonLoading(sendEmailBtn, true);
                var email = emailAddressInput ? emailAddressInput.value.trim() : '';

                apiCall('POST', '/email/enroll', email ? { email: email } : {})
                    .then(function (result) {
                        if (emailCredentialIdInput) emailCredentialIdInput.value = result.credentialId || '';
                        if (emailSentTo) emailSentTo.textContent = result.email || email || 'your account email';
                        if (emailBeginSection) emailBeginSection.style.display = 'none';
                        if (emailVerifySection) emailVerifySection.style.display = '';
                        if (emailCodeInput) emailCodeInput.focus();
                    })
                    .catch(function (err) {
                        showMessage('Failed to send verification code: ' + err.message, 'danger');
                    })
                    .finally(function () {
                        setButtonLoading(sendEmailBtn, false);
                    });
            });
        }

        // "Verify" email button
        var verifyEmailBtn = root.querySelector('.ww-2fa-verify-email-btn');
        if (verifyEmailBtn) {
            verifyEmailBtn.addEventListener('click', function () {
                var code = emailCodeInput ? emailCodeInput.value.trim() : '';
                var credentialId = emailCredentialIdInput ? emailCredentialIdInput.value : '';

                if (!code || code.length !== 6) {
                    if (emailCodeInput) emailCodeInput.classList.add('is-invalid');
                    return;
                }
                if (emailCodeInput) emailCodeInput.classList.remove('is-invalid');

                clearMessage();
                setButtonLoading(verifyEmailBtn, true);

                apiCall('POST', '/email/verify', { credentialId: credentialId, code: code })
                    .then(function () {
                        showMessage('Email verification enrolled successfully!', 'success');
                        resetEnrollment();
                        reloadPage();
                    })
                    .catch(function (err) {
                        showMessage('Email verification failed: ' + err.message, 'danger');
                    })
                    .finally(function () {
                        setButtonLoading(verifyEmailBtn, false);
                    });
            });
        }

        // ===== CANCEL ENROLLMENT =====

        var cancelBtns = root.querySelectorAll('.ww-2fa-cancel-enroll-btn');
        for (var i = 0; i < cancelBtns.length; i++) {
            cancelBtns[i].addEventListener('click', function () {
                resetEnrollment();
            });
        }

        function resetEnrollment() {
            if (authEnrollPanel) authEnrollPanel.style.display = 'none';
            if (emailEnrollPanel) emailEnrollPanel.style.display = 'none';
            if (addAuthBtn) addAuthBtn.style.display = '';
            if (addEmailBtn) addEmailBtn.style.display = '';
        }

        // ===== TOGGLE MANUAL KEY =====

        var toggleManualBtns = root.querySelectorAll('.ww-2fa-toggle-manual');
        for (var i = 0; i < toggleManualBtns.length; i++) {
            (function (btn) {
                btn.addEventListener('click', function () {
                    var target = document.getElementById(btn.dataset.target);
                    if (target) {
                        target.style.display = target.style.display === 'none' ? '' : 'none';
                    }
                });
            })(toggleManualBtns[i]);
        }

        // ===== CREDENTIAL ACTIONS =====

        // Set as primary
        root.addEventListener('click', function (e) {
            var btn = e.target.closest('.ww-2fa-set-primary-btn');
            if (!btn) return;
            var credentialId = btn.dataset.credentialId;
            clearMessage();

            apiCall('POST', '/credentials/' + credentialId + '/set-primary')
                .then(function () {
                    showMessage('Primary method updated.', 'success');
                    reloadPage();
                })
                .catch(function (err) {
                    showMessage('Failed to set primary: ' + err.message, 'danger');
                });
        });

        // Remove credential
        root.addEventListener('click', function (e) {
            var btn = e.target.closest('.ww-2fa-remove-btn');
            if (!btn) return;
            var credentialId = btn.dataset.credentialId;

            showConfirm(
                'Remove 2FA Method',
                'Are you sure you want to remove this two-factor authentication method? You may lose access if this is your only method.',
                function () {
                    return apiCall('DELETE', '/credentials/' + credentialId)
                        .then(function () {
                            showMessage('Method removed.', 'success');
                            reloadPage();
                        })
                        .catch(function (err) {
                            showMessage('Failed to remove: ' + err.message, 'danger');
                        });
                }
            );
        });

        // ===== TRUSTED DEVICES =====

        // Revoke single device
        root.addEventListener('click', function (e) {
            var btn = e.target.closest('.ww-2fa-revoke-device-btn');
            if (!btn) return;
            var deviceId = btn.dataset.deviceId;

            showConfirm(
                'Revoke Trusted Device',
                'Are you sure you want to revoke trust for this device? You will need to verify 2FA on this device next time.',
                function () {
                    return apiCall('DELETE', '/trusted-devices/' + deviceId)
                        .then(function () {
                            showMessage('Device revoked.', 'success');
                            // Remove the row from the table
                            var row = root.querySelector('tr[data-device-id="' + deviceId + '"]');
                            if (row) row.remove();
                        })
                        .catch(function (err) {
                            showMessage('Failed to revoke device: ' + err.message, 'danger');
                        });
                }
            );
        });

        // Revoke all devices
        var revokeAllBtn = root.querySelector('.ww-2fa-revoke-all-devices-btn');
        if (revokeAllBtn) {
            revokeAllBtn.addEventListener('click', function () {
                showConfirm(
                    'Revoke All Trusted Devices',
                    'Are you sure you want to revoke trust for all devices? You will need to verify 2FA on every device.',
                    function () {
                        return apiCall('DELETE', '/trusted-devices')
                            .then(function () {
                                showMessage('All devices revoked.', 'success');
                                reloadPage();
                            })
                            .catch(function (err) {
                                showMessage('Failed to revoke devices: ' + err.message, 'danger');
                            });
                    }
                );
            });
        }

        // ===== RECOVERY CODES =====

        var regenerateBtn = root.querySelector('.ww-2fa-regenerate-codes-btn');
        var codesDisplay = document.getElementById('ww-2fa-recovery-codes-display-' + cid);
        var codesList = document.getElementById('ww-2fa-recovery-codes-list-' + cid);

        if (regenerateBtn) {
            regenerateBtn.addEventListener('click', function () {
                showConfirm(
                    'Regenerate Recovery Codes',
                    'This will invalidate all existing recovery codes and generate new ones. Make sure to save the new codes.',
                    function () {
                        return apiCall('POST', '/recovery-codes/regenerate')
                            .then(function (result) {
                                var codes = result.codes || result.recoveryCodes || [];
                                if (codesList) {
                                    codesList.innerHTML = '';
                                    for (var i = 0; i < codes.length; i++) {
                                        var col = document.createElement('div');
                                        col.className = 'col';
                                        var code = document.createElement('code');
                                        code.className = 'd-block p-2 bg-light rounded text-center user-select-all';
                                        code.textContent = codes[i];
                                        col.appendChild(code);
                                        codesList.appendChild(col);
                                    }
                                }
                                if (codesDisplay) codesDisplay.style.display = '';
                                showMessage('Recovery codes regenerated. Save them now!', 'success');
                            })
                            .catch(function (err) {
                                showMessage('Failed to regenerate codes: ' + err.message, 'danger');
                            });
                    }
                );
            });
        }

        // Copy codes
        var copyCodesBtn = root.querySelector('.ww-2fa-copy-codes-btn');
        if (copyCodesBtn) {
            copyCodesBtn.addEventListener('click', function () {
                if (!codesList) return;
                var codes = codesList.querySelectorAll('code');
                var text = [];
                for (var i = 0; i < codes.length; i++) {
                    text.push(codes[i].textContent);
                }
                navigator.clipboard.writeText(text.join('\n'))
                    .then(function () {
                        showMessage('Recovery codes copied to clipboard.', 'success');
                    })
                    .catch(function () {
                        showMessage('Failed to copy. Please select and copy manually.', 'danger');
                    });
            });
        }

        // ===== ENTER KEY SUPPORT =====

        if (authCodeInput) {
            authCodeInput.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' && verifyAuthBtn) verifyAuthBtn.click();
            });
        }
        if (emailCodeInput) {
            emailCodeInput.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' && verifyEmailBtn) verifyEmailBtn.click();
            });
        }

        // ===== RELOAD HELPER =====

        function reloadPage() {
            setTimeout(function () { window.location.reload(); }, 1500);
        }
    }
})();
