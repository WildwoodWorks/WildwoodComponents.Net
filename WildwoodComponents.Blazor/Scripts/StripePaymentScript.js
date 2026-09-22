/**
 * WildwoodPayment Stripe Provider Script
 * Handles Stripe Elements initialization and payment processing.
 * Requires Stripe.js to be loaded externally first.
 *
 * Theme Support:
 * This script reads CSS custom properties (--ww-*) from the document
 * to apply consistent theming to Stripe Elements.
 *
 * INSTANCE KEYS
 * -------------
 * The script used to keep ONE module-level Stripe instance and ONE card element, so a page could
 * host exactly one card form. The registration + subscription component breaks that assumption: a
 * manage view can open the payment modal over a pack checkout's card form, and a signup can mount
 * the pack checkout's SetupIntent form while PaymentComponent is still on the page.
 *
 * Every entry point therefore takes an INSTANCE KEY and looks its Stripe instance up in a map. The
 * key is the LAST parameter of each function and is optional: a caller that omits it (or passes
 * null/empty) gets the 'default' instance, which is exactly the single instance the script used to
 * keep — so PaymentComponent's existing calls behave as they always did while a second form runs
 * beside it under a key of its own.
 */

(function (wildwoodPayment) {
    'use strict';

    /** The instance every caller that names no key shares — the old module-level singleton. */
    const DEFAULT_KEY = 'default';

    /** key -> { stripe, elements, cardElement, dotNetRefKey, elementId, attempts } */
    const instances = {};

    const MAX_INIT_ATTEMPTS = 3;

    /**
     * Normalise an instance key. Anything that is not a non-empty string is the default instance,
     * so an omitted argument, a null and an empty string all name the one the script always had.
     * @param {string|null|undefined} instanceKey
     * @returns {string}
     */
    function keyOf(instanceKey) {
        return (typeof instanceKey === 'string' && instanceKey.length > 0) ? instanceKey : DEFAULT_KEY;
    }

    /**
     * The instance under a key, created empty if it is new.
     * @param {string} key
     */
    function ensureInstance(key) {
        if (!instances[key]) {
            instances[key] = {
                stripe: null,
                elements: null,
                cardElement: null,
                dotNetRefKey: null,
                elementId: null,
                attempts: 0
            };
        }
        return instances[key];
    }

    /**
     * The instance under a key, or null when nothing was ever initialized there.
     * @param {string} key
     */
    function getInstance(key) {
        return instances[key] || null;
    }

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
     * The element a card form writes its validation message into: the one named after the card
     * container, falling back to the historic shared id for the default instance only — a second
     * form must never write its error into the first form's message line.
     * @param {string} key - Instance key
     * @param {string} elementId - The card container's element ID
     */
    function errorElementFor(key, elementId) {
        const own = elementId ? document.getElementById(elementId + '-errors') : null;
        if (own) {
            return own;
        }
        return key === DEFAULT_KEY ? document.getElementById('stripe-card-errors') : null;
    }

    /**
     * Show an error message in the Stripe container
     * @param {string} key - Instance key
     * @param {string} elementId - The container element ID
     * @param {string} message - Error message to display
     */
    function showStripeError(key, elementId, message) {
        const container = document.getElementById(elementId);
        if (container) {
            container.classList.add('stripe-error');
            container.innerHTML = '<div class="stripe-error-message" style="color: var(--ww-danger, #dc3545); padding: 12px; text-align: center;">' +
                '<i class="bi bi-exclamation-triangle me-2"></i>' + message + '</div>';
        }

        // Also update the dedicated error element if it exists
        const errorElement = errorElementFor(key, elementId);
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
     * @param {string} [instanceKey] - Which card form this is. Omitted: the default instance.
     * @returns {Promise<boolean>}
     */
    wildwoodPayment.initStripe = async function (publishableKey, elementId, dotNetRef, refKey, instanceKey) {
        const key = keyOf(instanceKey);
        const instance = ensureInstance(key);
        instance.attempts++;
        instance.elementId = elementId;

        console.log('WildwoodPayment/Stripe: ========================================');
        console.log('WildwoodPayment/Stripe: Starting initialization');
        console.log('WildwoodPayment/Stripe: Instance:', key);
        console.log('WildwoodPayment/Stripe: Element ID:', elementId);
        console.log('WildwoodPayment/Stripe: Attempt:', instance.attempts, 'of', MAX_INIT_ATTEMPTS);
        console.log('WildwoodPayment/Stripe: Has publishable key:', !!publishableKey);

        try {
            if (!publishableKey) {
                console.error('WildwoodPayment/Stripe: FAILED - Publishable key not provided');
                showStripeError(key, elementId, 'Stripe configuration error: Missing publishable key.');
                return false;
            }

            // Store .NET reference
            if (dotNetRef && refKey) {
                instance.dotNetRefKey = refKey;
                wildwoodPayment.storeDotNetRef(refKey, dotNetRef);
                console.log('WildwoodPayment/Stripe: .NET reference stored with key:', refKey);
            }

            // Check if Stripe.js is loaded
            console.log('WildwoodPayment/Stripe: Checking if Stripe.js is loaded...');
            console.log('WildwoodPayment/Stripe: typeof Stripe =', typeof Stripe);

            if (typeof Stripe === 'undefined') {
                console.error('WildwoodPayment/Stripe: Stripe.js is NOT loaded');

                // Try to wait for Stripe to load
                if (instance.attempts < MAX_INIT_ATTEMPTS) {
                    console.log('WildwoodPayment/Stripe: Waiting 1 second for Stripe.js to load...');
                    await new Promise(resolve => setTimeout(resolve, 1000));

                    if (typeof Stripe !== 'undefined') {
                        console.log('WildwoodPayment/Stripe: Stripe.js loaded after wait');
                    } else {
                        console.error('WildwoodPayment/Stripe: Stripe.js still not loaded after wait');
                        showStripeError(key, elementId, 'Payment system is loading. Please wait a moment and try again.');
                        return false;
                    }
                } else {
                    console.error('WildwoodPayment/Stripe: FAILED - Max retry attempts reached');
                    showStripeError(key, elementId, 'Unable to load payment system. Please check your internet connection or try disabling ad blockers.');
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
                notifyTrackingPrevention(instance.dotNetRefKey);
            } else {
                console.log('WildwoodPayment/Stripe: Third-party storage access OK');
            }

            // Create Stripe instance
            console.log('WildwoodPayment/Stripe: Creating Stripe instance...');
            try {
                instance.stripe = Stripe(publishableKey);
                console.log('WildwoodPayment/Stripe: Stripe instance created ?');
            } catch (stripeError) {
                console.error('WildwoodPayment/Stripe: FAILED to create Stripe instance:', stripeError);
                showStripeError(key, elementId, 'Payment initialization failed. Please refresh the page and try again.');
                return false;
            }

            // Create Stripe Elements
            console.log('WildwoodPayment/Stripe: Creating Stripe Elements...');
            const style = buildStripeStyle();
            instance.elements = instance.stripe.elements();
            console.log('WildwoodPayment/Stripe: Stripe Elements created ?');

            // Create card element
            console.log('WildwoodPayment/Stripe: Creating card element...');
            instance.cardElement = instance.elements.create('card', {
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
                instance.cardElement.mount('#' + elementId);

                // Handle validation changes
                instance.cardElement.on('change', function (event) {
                    const errorElement = errorElementFor(key, elementId);
                    if (errorElement) {
                        errorElement.textContent = event.error ? event.error.message : '';
                    }
                    if (instance.dotNetRefKey) {
                        wildwoodPayment.invokeDotNet(instance.dotNetRefKey, 'OnCardChange',
                            event.complete, event.error?.message || null);
                    }
                });

                instance.cardElement.on('ready', function () {
                    console.log('WildwoodPayment/Stripe: Card element READY ?');
                    console.log('WildwoodPayment/Stripe: ========================================');
                    instance.attempts = 0;
                });

                instance.cardElement.on('loaderror', function (event) {
                    console.error('WildwoodPayment/Stripe: Card element LOAD ERROR:', event.error);
                    showStripeError(key, elementId, 'Card input failed to load. This may be due to browser privacy settings or ad blockers.');
                });

                console.log('WildwoodPayment/Stripe: Initialization complete ?');
                wildwoodPayment.registerProvider('stripe');
                return true;
            }

            console.error('WildwoodPayment/Stripe: Container element not found:', elementId);

            // Retry if container not found
            if (instance.attempts < MAX_INIT_ATTEMPTS) {
                console.log('WildwoodPayment/Stripe: Retrying after 500ms...');
                await new Promise(resolve => setTimeout(resolve, 500));
                return await wildwoodPayment.initStripe(publishableKey, elementId, dotNetRef, refKey, key);
            }

            console.error('WildwoodPayment/Stripe: FAILED - Container not found after all retries');
            return false;
        } catch (error) {
            console.error('WildwoodPayment/Stripe: INITIALIZATION ERROR:', error);
            showStripeError(key, elementId, 'Payment initialization failed: ' + (error.message || 'Unknown error'));
            return false;
        }
    };

    /**
     * Check if Stripe is properly initialized
     * @param {string} [instanceKey] - Which card form. Omitted: the default instance.
     * @returns {boolean}
     */
    wildwoodPayment.isStripeReady = function (instanceKey) {
        const instance = getInstance(keyOf(instanceKey));
        return instance !== null && instance.stripe !== null && instance.cardElement !== null;
    };

    /**
     * Update Stripe Elements styling when theme changes
     * @param {string} [instanceKey] - Which card form. Omitted: every mounted one.
     */
    wildwoodPayment.updateStripeTheme = function (instanceKey) {
        const style = buildStripeStyle();

        // No key given: every card form on the page follows the theme, not just the first.
        const keys = (typeof instanceKey === 'string' && instanceKey.length > 0)
            ? [instanceKey]
            : Object.keys(instances);

        keys.forEach(function (key) {
            const instance = getInstance(key);
            if (instance && instance.cardElement) {
                instance.cardElement.update({ style: style });
                console.log('WildwoodPayment/Stripe: Theme updated for instance', key);
            }
        });
    };

    /**
     * Create a payment method from the card element
     * @param {string} cardholderName - Name on the card (optional)
     * @param {string} [instanceKey] - Which card form. Omitted: the default instance.
     * @returns {Promise<object>}
     */
    wildwoodPayment.createPaymentMethod = async function (cardholderName, instanceKey) {
        try {
            const instance = getInstance(keyOf(instanceKey));
            if (!instance || !instance.stripe || !instance.cardElement) {
                console.error('WildwoodPayment/Stripe: Not initialized');
                return { success: false, error: 'Stripe not initialized. Please refresh the page.' };
            }

            console.log('WildwoodPayment/Stripe: Creating payment method...');
            const { paymentMethod, error } = await instance.stripe.createPaymentMethod({
                type: 'card',
                card: instance.cardElement,
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
     * Confirm a Stripe payment with client secret, charging the MOUNTED card element.
     * @param {string} clientSecret - Payment intent client secret
     * @param {string} [instanceKey] - Which card form. Omitted: the default instance.
     * @returns {Promise<object>}
     */
    wildwoodPayment.confirmStripePayment = async function (clientSecret, instanceKey) {
        try {
            const instance = getInstance(keyOf(instanceKey));
            if (!instance || !instance.stripe || !instance.cardElement) {
                return { success: false, errorMessage: 'Stripe not initialized' };
            }

            console.log('WildwoodPayment/Stripe: Confirming payment...');
            const { paymentIntent, error } = await instance.stripe.confirmCardPayment(clientSecret, {
                payment_method: {
                    card: instance.cardElement
                }
            });

            return readPaymentIntent(paymentIntent, error);
        } catch (error) {
            console.error('WildwoodPayment/Stripe: Confirmation error:', error);
            return {
                success: false,
                errorMessage: error.message || 'Payment confirmation failed'
            };
        }
    };

    /**
     * Confirm a payment intent for a card ALREADY ON FILE - 3-D Secure on a plan change or a pack
     * bought against the saved card. There is no card field to mount for these, so no element is
     * passed to Stripe: the intent already carries its payment method and the customer is only
     * being asked to authenticate it.
     *
     * Because there may be no mounted form at all, a publishable key may be supplied: the named
     * instance is used when it exists, otherwise a bare Stripe instance is created from the key and
     * kept under that key for the next confirmation. Answers the same
     * { success, paymentIntentId, errorMessage, errorCode } shape as confirmStripePayment.
     *
     * @param {string} clientSecret - Payment intent client secret
     * @param {string} [instanceKey] - Which Stripe instance to use. Omitted: the default instance.
     * @param {string} [publishableKey] - Used only when that instance does not exist yet.
     * @returns {Promise<object>}
     */
    wildwoodPayment.confirmStripeCardPayment = async function (clientSecret, instanceKey, publishableKey) {
        try {
            if (!clientSecret) {
                return { success: false, errorMessage: 'Stripe not initialized' };
            }

            const key = keyOf(instanceKey);
            let instance = getInstance(key);

            if (!instance || !instance.stripe) {
                if (!publishableKey || typeof Stripe === 'undefined') {
                    return { success: false, errorMessage: 'Stripe not initialized' };
                }

                instance = ensureInstance(key);
                instance.stripe = Stripe(publishableKey);
            }

            console.log('WildwoodPayment/Stripe: Confirming saved-card payment...');
            const { paymentIntent, error } = await instance.stripe.confirmCardPayment(clientSecret);

            return readPaymentIntent(paymentIntent, error);
        } catch (error) {
            console.error('WildwoodPayment/Stripe: Confirmation error:', error);
            return {
                success: false,
                errorMessage: error.message || 'Payment confirmation failed'
            };
        }
    };

    /**
     * One reading of a confirmCardPayment answer, so the mounted-element path and the saved-card
     * path cannot report the same outcome differently.
     * @param {object} paymentIntent
     * @param {object} error
     */
    function readPaymentIntent(paymentIntent, error) {
        if (error) {
            console.error('WildwoodPayment/Stripe: Confirmation error:', error);
            return {
                success: false,
                errorMessage: error.message,
                errorCode: error.code
            };
        }

        if (paymentIntent && paymentIntent.status === 'succeeded') {
            console.log('WildwoodPayment/Stripe: Payment confirmed:', paymentIntent.id);
            return {
                success: true,
                paymentIntentId: paymentIntent.id
            };
        }

        console.log('WildwoodPayment/Stripe: Payment status:', paymentIntent ? paymentIntent.status : 'unknown');
        return {
            success: false,
            errorMessage: 'Payment status: ' + (paymentIntent ? paymentIntent.status : 'unknown'),
            paymentIntentId: paymentIntent ? paymentIntent.id : null
        };
    }

    /**
     * Confirm a Stripe SetupIntent with client secret.
     *
     * Used for a free trial and for the pack checkout's one-off card entry: nothing is charged now,
     * but the card is saved so it can be charged afterwards. Mirrors confirmStripePayment's shape
     * exactly - the same not-initialized guard, the same { success, errorMessage, errorCode } error
     * object - so the .NET side reads one result type either way.
     *
     * @param {string} clientSecret - Setup intent client secret
     * @param {string} [instanceKey] - Which card form. Omitted: the default instance.
     * @returns {Promise<object>}
     */
    wildwoodPayment.confirmStripeSetup = async function (clientSecret, instanceKey) {
        try {
            const instance = getInstance(keyOf(instanceKey));
            if (!instance || !instance.stripe || !instance.cardElement) {
                return { success: false, errorMessage: 'Stripe not initialized' };
            }

            console.log('WildwoodPayment/Stripe: Confirming card setup...');
            const { setupIntent, error } = await instance.stripe.confirmCardSetup(clientSecret, {
                payment_method: {
                    card: instance.cardElement
                }
            });

            if (error) {
                console.error('WildwoodPayment/Stripe: Card setup error:', error);
                return {
                    success: false,
                    errorMessage: error.message,
                    errorCode: error.code
                };
            }

            if (setupIntent.status === 'succeeded') {
                console.log('WildwoodPayment/Stripe: Card setup confirmed:', setupIntent.id);
                return {
                    success: true,
                    setupIntentId: setupIntent.id
                };
            }

            console.log('WildwoodPayment/Stripe: Card setup status:', setupIntent.status);
            return {
                success: false,
                errorMessage: 'Card setup status: ' + setupIntent.status + '. Please try again.',
                setupIntentId: setupIntent.id
            };
        } catch (error) {
            console.error('WildwoodPayment/Stripe: Card setup error:', error);
            return {
                success: false,
                errorMessage: error.message || 'Card setup failed'
            };
        }
    };

    /**
     * Dispose Stripe elements and clean up.
     *
     * Every owner of a key disposes its own: the card form its element's key, the pack checkout
     * the key its 3-D Secure challenges created, PaymentComponent the default one. A key nothing
     * was ever initialized under is a no-op, so disposing twice, or disposing a checkout that
     * never needed a card, costs nothing.
     *
     * @param {string} [instanceKey] - Which card form. Omitted: the default instance ONLY, so one
     * component leaving the page never tears down another's card field.
     */
    wildwoodPayment.disposeStripe = function (instanceKey) {
        const key = keyOf(instanceKey);
        const instance = getInstance(key);
        console.log('WildwoodPayment/Stripe: Disposing instance', key);

        if (!instance) {
            return;
        }

        if (instance.cardElement) {
            try {
                instance.cardElement.unmount();
                console.log('WildwoodPayment/Stripe: Card element unmounted');
            } catch (e) {
                // Element might already be unmounted
            }

            try {
                // unmount() only takes the iframe out of the page - the element, and the
                // 'change'/'ready'/'loaderror' handlers closing over this instance, stay alive
                // until it is destroyed.
                if (typeof instance.cardElement.destroy === 'function') {
                    instance.cardElement.destroy();
                }
            } catch (e) {
                // Element might already be destroyed
            }
        }

        if (instance.dotNetRefKey) {
            wildwoodPayment.removeDotNetRef(instance.dotNetRefKey);
        }

        instance.cardElement = null;
        instance.elements = null;
        instance.stripe = null;

        delete instances[key];
        console.log('WildwoodPayment/Stripe: Disposed');
    };

    console.log('WildwoodPayment/Stripe: Script loaded and ready');

})(window.wildwoodPayment);
