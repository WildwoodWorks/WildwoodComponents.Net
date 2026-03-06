// webauthn.js - JavaScript interop for WebAuthn/FIDO2 passkey operations
// Used by AuthenticationComponent.razor for passkey login and registration

window.wildwoodWebAuthn = {
    /**
     * Check if WebAuthn is supported in the current browser
     * @returns {boolean} True if WebAuthn is supported
     */
    isSupported: function () {
        return window.PublicKeyCredential !== undefined &&
            typeof window.PublicKeyCredential === 'function';
    },

    /**
     * Check if platform authenticator (Face ID, Touch ID, Windows Hello) is available
     * @returns {Promise<boolean>} True if platform authenticator is available
     */
    isPlatformAuthenticatorAvailable: async function () {
        if (!this.isSupported()) {
            return false;
        }
        try {
            return await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
        } catch (e) {
            console.warn('Error checking platform authenticator availability:', e);
            return false;
        }
    },

    /**
     * Check if conditional UI (autofill) is available
     * @returns {Promise<boolean>} True if conditional UI is available
     */
    isConditionalMediationAvailable: async function () {
        if (!this.isSupported()) {
            return false;
        }
        try {
            return await PublicKeyCredential.isConditionalMediationAvailable();
        } catch (e) {
            return false;
        }
    },

    /**
     * Register a new passkey credential
     * @param {object} options - Registration options from server (PasskeyRegistrationOptionsResponse)
     * @returns {Promise<object>} Credential response to send to server
     */
    register: async function (options) {
        if (!this.isSupported()) {
            throw new Error('WebAuthn is not supported in this browser.');
        }

        try {
            // Convert Base64URL-encoded values to ArrayBuffer
            const publicKeyCredentialCreationOptions = {
                challenge: this._base64UrlToArrayBuffer(options.challenge),
                rp: {
                    id: options.relyingParty.id,
                    name: options.relyingParty.name
                },
                user: {
                    id: this._base64UrlToArrayBuffer(options.user.id),
                    name: options.user.name,
                    displayName: options.user.displayName
                },
                pubKeyCredParams: options.pubKeyCredParams.map(p => ({
                    type: p.type,
                    alg: p.alg
                })),
                timeout: options.timeout || 60000,
                attestation: options.attestation || 'none',
                authenticatorSelection: options.authenticatorSelection ? {
                    authenticatorAttachment: options.authenticatorSelection.authenticatorAttachment || undefined,
                    requireResidentKey: options.authenticatorSelection.requireResidentKey || false,
                    residentKey: options.authenticatorSelection.residentKey || 'preferred',
                    userVerification: options.authenticatorSelection.userVerification || 'preferred'
                } : undefined,
                excludeCredentials: options.excludeCredentials ? options.excludeCredentials.map(c => ({
                    type: c.type,
                    id: this._base64UrlToArrayBuffer(c.id),
                    transports: c.transports
                })) : undefined
            };

            // Call WebAuthn API
            const credential = await navigator.credentials.create({
                publicKey: publicKeyCredentialCreationOptions
            });

            // Convert credential response to JSON-serializable object
            const response = credential.response;
            return {
                id: this._arrayBufferToBase64Url(credential.rawId),
                rawId: this._arrayBufferToBase64Url(credential.rawId),
                type: credential.type,
                response: {
                    clientDataJSON: this._arrayBufferToBase64Url(response.clientDataJSON),
                    attestationObject: this._arrayBufferToBase64Url(response.attestationObject),
                    transports: response.getTransports ? response.getTransports() : undefined
                },
                clientExtensionResults: credential.getClientExtensionResults()
            };
        } catch (e) {
            console.error('WebAuthn registration error:', e);
            if (e.name === 'NotAllowedError') {
                throw new Error('Registration was cancelled or timed out.');
            } else if (e.name === 'InvalidStateError') {
                throw new Error('This authenticator is already registered.');
            } else if (e.name === 'NotSupportedError') {
                throw new Error('This authenticator is not supported.');
            }
            throw e;
        }
    },

    /**
     * Authenticate with a passkey credential
     * @param {object} options - Authentication options from server (PasskeyAuthenticationOptionsResponse)
     * @returns {Promise<object>} Assertion response to send to server
     */
    authenticate: async function (options) {
        if (!this.isSupported()) {
            throw new Error('WebAuthn is not supported in this browser.');
        }

        try {
            // Convert Base64URL-encoded values to ArrayBuffer
            const publicKeyCredentialRequestOptions = {
                challenge: this._base64UrlToArrayBuffer(options.challenge),
                rpId: options.rpId,
                timeout: options.timeout || 60000,
                userVerification: options.userVerification || 'preferred',
                allowCredentials: options.allowCredentials ? options.allowCredentials.map(c => ({
                    type: c.type,
                    id: this._base64UrlToArrayBuffer(c.id),
                    transports: c.transports
                })) : undefined
            };

            // Call WebAuthn API
            const assertion = await navigator.credentials.get({
                publicKey: publicKeyCredentialRequestOptions
            });

            // Convert assertion response to JSON-serializable object
            const response = assertion.response;
            return {
                id: this._arrayBufferToBase64Url(assertion.rawId),
                rawId: this._arrayBufferToBase64Url(assertion.rawId),
                type: assertion.type,
                response: {
                    clientDataJSON: this._arrayBufferToBase64Url(response.clientDataJSON),
                    authenticatorData: this._arrayBufferToBase64Url(response.authenticatorData),
                    signature: this._arrayBufferToBase64Url(response.signature),
                    userHandle: response.userHandle ? this._arrayBufferToBase64Url(response.userHandle) : null
                },
                clientExtensionResults: assertion.getClientExtensionResults()
            };
        } catch (e) {
            console.error('WebAuthn authentication error:', e);
            if (e.name === 'NotAllowedError') {
                throw new Error('Authentication was cancelled or timed out.');
            } else if (e.name === 'SecurityError') {
                throw new Error('Security error during authentication.');
            }
            throw e;
        }
    },

    /**
     * Abort any ongoing WebAuthn operation
     */
    abort: function () {
        // WebAuthn operations can be aborted via AbortController in newer implementations
        // For now, users can simply close the browser dialog
    },

    // ============================================
    // Helper Functions
    // ============================================

    /**
     * Convert Base64URL string to ArrayBuffer
     * @param {string} base64url - Base64URL encoded string
     * @returns {ArrayBuffer}
     */
    _base64UrlToArrayBuffer: function (base64url) {
        // Convert Base64URL to Base64
        let base64 = base64url.replace(/-/g, '+').replace(/_/g, '/');

        // Add padding
        while (base64.length % 4 !== 0) {
            base64 += '=';
        }

        // Decode Base64
        const binaryString = atob(base64);
        const bytes = new Uint8Array(binaryString.length);
        for (let i = 0; i < binaryString.length; i++) {
            bytes[i] = binaryString.charCodeAt(i);
        }
        return bytes.buffer;
    },

    /**
     * Convert ArrayBuffer to Base64URL string
     * @param {ArrayBuffer} buffer - ArrayBuffer to convert
     * @returns {string} Base64URL encoded string
     */
    _arrayBufferToBase64Url: function (buffer) {
        const bytes = new Uint8Array(buffer);
        let binary = '';
        for (let i = 0; i < bytes.byteLength; i++) {
            binary += String.fromCharCode(bytes[i]);
        }

        // Convert to Base64 and then to Base64URL
        return btoa(binary)
            .replace(/\+/g, '-')
            .replace(/\//g, '_')
            .replace(/=+$/, '');
    }
};

// Expose global functions for simpler Blazor interop
window.webauthnIsSupported = function () {
    return window.wildwoodWebAuthn.isSupported();
};

window.webauthnIsPlatformAuthenticatorAvailable = async function () {
    return await window.wildwoodWebAuthn.isPlatformAuthenticatorAvailable();
};

window.webauthnRegister = async function (options) {
    return await window.wildwoodWebAuthn.register(options);
};

window.webauthnAuthenticate = async function (options) {
    return await window.wildwoodWebAuthn.authenticate(options);
};
