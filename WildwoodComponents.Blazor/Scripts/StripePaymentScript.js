/**
 * WildwoodPayment Stripe Provider Script
 * Handles Stripe Elements initialization and payment processing.
 * Requires Stripe.js to be loaded externally first.
 * 
 * Theme Support:
 * This script reads CSS custom properties (--ww-*) from the document
 * to apply consistent theming to Stripe Elements.
 */

(function (wildwoodPayment) {
    'use strict';

    // Stripe-specific state
    let stripe = null;
    let stripeElements = null;
    let stripeCardElement = null;
    let stripeDotNetRefKey = null;
    let initializationAttempts = 0;
    const MAX_INIT_ATTEMPTS = 3;

    /**
     * Check if the browser supports the Storage Access API
     * @returns {boolean}
     */
    function hasStorageAccessAPI() {
        return typeof document.hasStorageAccess === 'function' && 
               typeof document.requestStorageAccess === 'function';
    }

    /**
     * Request storage access using the Storage Access API
     * This is the recommended mitigation for browser tracking prevention
     * Note: This API requires user interaction to work in most browsers
     * @returns {Promise<boolean>} True if storage access was granted
     */
    async function requestStorageAccess() {
        if (!hasStorageAccessAPI()) {
            console.log('WildwoodPayment/Stripe: Storage Access API not available in this browser');
            return false;
        }

        try {
            // First check if we already have storage access
            const hasAccess = await document.hasStorageAccess();
            if (hasAccess) {
                console.log('WildwoodPayment/Stripe: Storage access already granted');
                return true;
            }

            // Request storage access - this may show a user prompt
            // Note: This only works after user interaction (click, etc.)
            console.log('WildwoodPayment/Stripe: Requesting storage access via Storage Access API...');
            await document.requestStorageAccess();
            console.log('WildwoodPayment/Stripe: Storage access granted via Storage Access API');
            return true;
        } catch (error) {
            // This is expected in most cases:
            // - User denied access
            // - API called before user interaction
            // - Browser doesn't support in this context
            console.info('WildwoodPayment/Stripe: Storage Access API request not granted:', error.message || error);
            console.info('WildwoodPayment/Stripe: This is normal - Stripe will still work with limited fraud detection features');
            return false;
        }
    }

    /**
     * Check if third-party cookies/storage are likely blocked
     * This is a heuristic check - browsers don't expose this directly
     * @returns {boolean}
     */
    function isThirdPartyStorageBlocked() {
        try {
            // Check if we can access storage in a third-party context
            const testKey = '__ww_storage_test__';
            localStorage.setItem(testKey, '1');
            localStorage.removeItem(testKey);
            return false;
        } catch (e) {
            return true;
        }
    }

    /**
     * Log tracking prevention warning and notify Blazor component
     * @param {string} refKey - The .NET reference key for callback
     */
    function notifyTrackingPrevention(refKey) {
        console.info('WildwoodPayment/Stripe: Browser tracking prevention detected. ' +
            'Stripe payments will still work, but some fraud detection features may be limited. ' +
            'If you experience issues, try disabling tracking prevention for this site.');
        
        // Notify the Blazor component so it can show a warning to the user
        if (refKey) {
            wildwoodPayment.invokeDotNet(refKey, 'OnTrackingPreventionDetected');
        }
    }

    /**
     * Show an error message in the Stripe container
     * @param {string} elementId - The container element ID
     * @param {string} message - Error message to display
     */
    function showStripeError(elementId, message) {
        const container = document.getElementById(elementId);
        if (container) {
            container.classList.add('stripe-error');
            container.innerHTML = '<div class="stripe-error-message" style="color: var(--ww-danger, #dc3545); padding: 12px; text-align: center;">' +
                '<i class="bi bi-exclamation-triangle me-2"></i>' + message + '</div>';
        }
        
        // Also update the dedicated error element if it exists
        const errorElement = document.getElementById('stripe-card-errors');
        if (errorElement) {
            errorElement.textContent = message;
        }
    }

    /**
     * Get CSS variable value from the document
     * @param {string} varName - CSS variable name (e.g., '--ww-primary')
     * @param {string} fallback - Fallback value if variable not set
     * @returns {string}
     */
    function getCssVariable(varName, fallback) {
        const value = getComputedStyle(document.documentElement).getPropertyValue(varName).trim();
        return value || fallback;
    }

    /**
     * Build Stripe Elements style object from CSS variables
     * @returns {object} Stripe-compatible style object
     */
    function buildStripeStyle() {
        const textColor = getCssVariable('--ww-stripe-text-color', 
            getCssVariable('--ww-text-primary', '#212529'));
        const placeholderColor = getCssVariable('--ww-stripe-placeholder-color', 
            getCssVariable('--ww-text-muted', '#6c757d'));
        const errorColor = getCssVariable('--ww-stripe-error-color', 
            getCssVariable('--ww-danger', '#dc3545'));
        const successColor = getCssVariable('--ww-success', '#28a745');
        const fontFamily = getCssVariable('--ww-stripe-font-family', 
            "'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif");
        const fontSize = getCssVariable('--ww-stripe-font-size', '16px');

        return {
            base: {
                fontSize: fontSize,
                color: textColor,
                fontFamily: fontFamily,
                fontSmoothing: 'antialiased',
                '::placeholder': {
                    color: placeholderColor
                },
                ':-webkit-autofill': {
                    color: textColor
                }
            },
            invalid: {
                color: errorColor,
                iconColor: errorColor
            },
            complete: {
                iconColor: successColor
            }
        };
    }

    /**
     * Initialize Stripe Elements with retry logic and Storage Access API support
     * @param {string} publishableKey - Stripe publishable key
     * @param {string} elementId - DOM element ID to mount the card element
     * @param {object} dotNetRef - .NET object reference for callbacks
     * @param {string} refKey - Key to store the .NET reference
     * @returns {Promise<boolean>}
     */
    wildwoodPayment.initStripe = async function (publishableKey, elementId, dotNetRef, refKey) {
        initializationAttempts++;
        
        console.log('WildwoodPayment/Stripe: ========================================');
        console.log('WildwoodPayment/Stripe: Starting initialization');
        console.log('WildwoodPayment/Stripe: Element ID:', elementId);
        console.log('WildwoodPayment/Stripe: Attempt:', initializationAttempts, 'of', MAX_INIT_ATTEMPTS);
        console.log('WildwoodPayment/Stripe: Has publishable key:', !!publishableKey);
        
        try {
            if (!publishableKey) {
                console.error('WildwoodPayment/Stripe: FAILED - Publishable key not provided');
                showStripeError(elementId, 'Stripe configuration error: Missing publishable key.');
                return false;
            }

            // Store .NET reference
            if (dotNetRef && refKey) {
                stripeDotNetRefKey = refKey;
                wildwoodPayment.storeDotNetRef(refKey, dotNetRef);
                console.log('WildwoodPayment/Stripe: .NET reference stored with key:', refKey);
            }

            // Check if Stripe.js is loaded
            console.log('WildwoodPayment/Stripe: Checking if Stripe.js is loaded...');
            console.log('WildwoodPayment/Stripe: typeof Stripe =', typeof Stripe);
            
            if (typeof Stripe === 'undefined') {
                console.error('WildwoodPayment/Stripe: Stripe.js is NOT loaded');
                
                // Try to wait for Stripe to load
                if (initializationAttempts < MAX_INIT_ATTEMPTS) {
                    console.log('WildwoodPayment/Stripe: Waiting 1 second for Stripe.js to load...');
                    await new Promise(resolve => setTimeout(resolve, 1000));
                    
                    if (typeof Stripe !== 'undefined') {
                        console.log('WildwoodPayment/Stripe: Stripe.js loaded after wait');
                    } else {
                        console.error('WildwoodPayment/Stripe: Stripe.js still not loaded after wait');
                        showStripeError(elementId, 'Payment system is loading. Please wait a moment and try again.');
                        return false;
                    }
                } else {
                    console.error('WildwoodPayment/Stripe: FAILED - Max retry attempts reached');
                    showStripeError(elementId, 'Unable to load payment system. Please check your internet connection or try disabling ad blockers.');
                    return false;
                }
            } else {
                console.log('WildwoodPayment/Stripe: Stripe.js is loaded ?');
            }

            // Try to request storage access (optional - improves fraud detection)
            console.log('WildwoodPayment/Stripe: Attempting Storage Access API...');
            const storageAccessGranted = await requestStorageAccess();
            console.log('WildwoodPayment/Stripe: Storage access granted:', storageAccessGranted);

            // Check for tracking prevention (informational only)
            if (isThirdPartyStorageBlocked()) {
                console.log('WildwoodPayment/Stripe: Third-party storage appears blocked');
                notifyTrackingPrevention(stripeDotNetRefKey);
            } else {
                console.log('WildwoodPayment/Stripe: Third-party storage access OK');
            }

            // Create Stripe instance
            console.log('WildwoodPayment/Stripe: Creating Stripe instance...');
            try {
                stripe = Stripe(publishableKey);
                console.log('WildwoodPayment/Stripe: Stripe instance created ?');
            } catch (stripeError) {
                console.error('WildwoodPayment/Stripe: FAILED to create Stripe instance:', stripeError);
                showStripeError(elementId, 'Payment initialization failed. Please refresh the page and try again.');
                return false;
            }
            
            // Create Stripe Elements
            console.log('WildwoodPayment/Stripe: Creating Stripe Elements...');
            const style = buildStripeStyle();
            stripeElements = stripe.elements();
            console.log('WildwoodPayment/Stripe: Stripe Elements created ?');

            // Create card element
            console.log('WildwoodPayment/Stripe: Creating card element...');
            stripeCardElement = stripeElements.create('card', {
                style: style,
                hidePostalCode: false,
                disableLink: true
            });
            console.log('WildwoodPayment/Stripe: Card element created ?');

            // Mount the card element
            const container = document.getElementById(elementId);
            console.log('WildwoodPayment/Stripe: Looking for container element:', elementId);
            console.log('WildwoodPayment/Stripe: Container found:', !!container);
            
            if (container) {
                container.innerHTML = '';
                container.classList.remove('stripe-loading');
                container.classList.remove('stripe-error');

                console.log('WildwoodPayment/Stripe: Mounting card element...');
                stripeCardElement.mount('#' + elementId);

                // Handle validation changes
                stripeCardElement.on('change', function (event) {
                    const errorElement = document.getElementById('stripe-card-errors');
                    if (errorElement) {
                        errorElement.textContent = event.error ? event.error.message : '';
                    }
                    if (stripeDotNetRefKey) {
                        wildwoodPayment.invokeDotNet(stripeDotNetRefKey, 'OnCardChange', 
                            event.complete, event.error?.message || null);
                    }
                });

                stripeCardElement.on('ready', function () {
                    console.log('WildwoodPayment/Stripe: Card element READY ?');
                    console.log('WildwoodPayment/Stripe: ========================================');
                    initializationAttempts = 0;
                });
                
                stripeCardElement.on('loaderror', function (event) {
                    console.error('WildwoodPayment/Stripe: Card element LOAD ERROR:', event.error);
                    showStripeError(elementId, 'Card input failed to load. This may be due to browser privacy settings or ad blockers.');
                });

                console.log('WildwoodPayment/Stripe: Initialization complete ?');
                wildwoodPayment.registerProvider('stripe');
                return true;
            }

            console.error('WildwoodPayment/Stripe: Container element not found:', elementId);
            
            // Retry if container not found
            if (initializationAttempts < MAX_INIT_ATTEMPTS) {
                console.log('WildwoodPayment/Stripe: Retrying after 500ms...');
                await new Promise(resolve => setTimeout(resolve, 500));
                return await wildwoodPayment.initStripe(publishableKey, elementId, dotNetRef, refKey);
            }
            
            console.error('WildwoodPayment/Stripe: FAILED - Container not found after all retries');
            return false;
        } catch (error) {
            console.error('WildwoodPayment/Stripe: INITIALIZATION ERROR:', error);
            showStripeError(elementId, 'Payment initialization failed: ' + (error.message || 'Unknown error'));
            return false;
        }
    };

    /**
     * Check if Stripe is properly initialized
     * @returns {boolean}
     */
    wildwoodPayment.isStripeReady = function () {
        return stripe !== null && stripeCardElement !== null;
    };

    /**
     * Update Stripe Elements styling when theme changes
     */
    wildwoodPayment.updateStripeTheme = function () {
        if (stripeCardElement) {
            const style = buildStripeStyle();
            stripeCardElement.update({ style: style });
            console.log('WildwoodPayment/Stripe: Theme updated');
        }
    };

    /**
     * Create a payment method from the card element
     * @param {string} cardholderName - Name on the card (optional)
     * @returns {Promise<object>}
     */
    wildwoodPayment.createPaymentMethod = async function (cardholderName) {
        try {
            if (!stripe || !stripeCardElement) {
                console.error('WildwoodPayment/Stripe: Not initialized');
                return { success: false, error: 'Stripe not initialized. Please refresh the page.' };
            }

            console.log('WildwoodPayment/Stripe: Creating payment method...');
            const { paymentMethod, error } = await stripe.createPaymentMethod({
                type: 'card',
                card: stripeCardElement,
                billing_details: {
                    name: cardholderName || undefined
                }
            });

            if (error) {
                console.error('WildwoodPayment/Stripe: Payment method error:', error);
                return { success: false, error: error.message };
            }

            console.log('WildwoodPayment/Stripe: Payment method created:', paymentMethod.id);
            return { success: true, paymentMethodId: paymentMethod.id };
        } catch (error) {
            console.error('WildwoodPayment/Stripe: Error creating payment method:', error);
            return { success: false, error: error.message || 'Failed to create payment method' };
        }
    };

    /**
     * Confirm a Stripe payment with client secret
     * @param {string} clientSecret - Payment intent client secret
     * @returns {Promise<object>}
     */
    wildwoodPayment.confirmStripePayment = async function (clientSecret) {
        try {
            if (!stripe || !stripeCardElement) {
                return { success: false, errorMessage: 'Stripe not initialized' };
            }

            console.log('WildwoodPayment/Stripe: Confirming payment...');
            const { paymentIntent, error } = await stripe.confirmCardPayment(clientSecret, {
                payment_method: {
                    card: stripeCardElement
                }
            });

            if (error) {
                console.error('WildwoodPayment/Stripe: Confirmation error:', error);
                return {
                    success: false,
                    errorMessage: error.message,
                    errorCode: error.code
                };
            }

            if (paymentIntent.status === 'succeeded') {
                console.log('WildwoodPayment/Stripe: Payment confirmed:', paymentIntent.id);
                return {
                    success: true,
                    paymentIntentId: paymentIntent.id
                };
            }

            console.log('WildwoodPayment/Stripe: Payment status:', paymentIntent.status);
            return {
                success: false,
                errorMessage: 'Payment status: ' + paymentIntent.status,
                paymentIntentId: paymentIntent.id
            };
        } catch (error) {
            console.error('WildwoodPayment/Stripe: Confirmation error:', error);
            return {
                success: false,
                errorMessage: error.message || 'Payment confirmation failed'
            };
        }
    };

    /**
     * Dispose Stripe elements and clean up
     */
    wildwoodPayment.disposeStripe = function () {
        console.log('WildwoodPayment/Stripe: Disposing...');
        if (stripeCardElement) {
            try {
                stripeCardElement.unmount();
                console.log('WildwoodPayment/Stripe: Card element unmounted');
            } catch (e) {
                // Element might already be unmounted
            }
            stripeCardElement = null;
        }
        stripeElements = null;
        stripe = null;
        initializationAttempts = 0;

        if (stripeDotNetRefKey) {
            wildwoodPayment.removeDotNetRef(stripeDotNetRefKey);
            stripeDotNetRefKey = null;
        }
        console.log('WildwoodPayment/Stripe: Disposed');
    };

    console.log('WildwoodPayment/Stripe: Script loaded and ready');

})(window.wildwoodPayment);
