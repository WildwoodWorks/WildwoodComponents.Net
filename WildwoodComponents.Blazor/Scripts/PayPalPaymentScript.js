/**
 * WildwoodPayment PayPal Provider Script
 * Handles PayPal Buttons initialization and payment processing.
 * Requires PayPal SDK to be loaded dynamically.
 * 
 * Theme Support:
 * This script reads CSS custom properties (--ww-*) from the document
 * to apply consistent theming to PayPal buttons where possible.
 * 
 * Features:
 * - One-time payments
 * - Subscription/recurring payments
 * - Apple Pay and Google Pay funding sources (when enabled)
 */

(function (wildwoodPayment) {
    'use strict';

    // PayPal-specific state
    let paypalButtons = null;
    let paypalDotNetRefKey = null;
    let currentContainerId = null;
    let currentSettings = null;

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
     * Detect if the current theme is dark mode
     * @returns {boolean}
     */
    function isDarkTheme() {
        // Check for common dark theme indicators
        const bgPrimary = getCssVariable('--ww-bg-primary', '#ffffff');
        const textPrimary = getCssVariable('--ww-text-primary', '#212529');
        
        // Simple luminance check - dark backgrounds have low RGB values
        const bgColor = bgPrimary.toLowerCase();
        if (bgColor.startsWith('#')) {
            const hex = bgColor.slice(1);
            const r = parseInt(hex.substr(0, 2), 16);
            const g = parseInt(hex.substr(2, 2), 16);
            const b = parseInt(hex.substr(4, 2), 16);
            const luminance = (0.299 * r + 0.587 * g + 0.114 * b);
            return luminance < 128;
        }
        
        // Check for data-theme attribute
        const theme = document.documentElement.getAttribute('data-theme') || 
                     document.body.getAttribute('data-theme');
        return theme === 'dark' || theme === 'aicoach-dark';
    }

    /**
     * Build PayPal button style based on current theme
     * @returns {object} PayPal button style configuration
     */
    function buildPayPalStyle() {
        const dark = isDarkTheme();
        const borderRadius = getCssVariable('--ww-paypal-border-radius', 
            getCssVariable('--ww-border-radius-sm', '4px'));
        
        // PayPal button color options: 'gold', 'blue', 'silver', 'white', 'black'
        // Choose color based on theme
        let buttonColor = 'blue'; // Default
        
        if (dark) {
            // For dark themes, use gold or white for contrast
            buttonColor = 'gold';
        } else {
            // For light themes, blue works well
            buttonColor = 'blue';
        }

        return {
            layout: 'vertical',
            color: buttonColor,
            shape: 'rect',
            label: 'pay',
            height: 48, // Consistent with other buttons
            tagline: false
        };
    }

    /**
     * Build funding sources array based on provider capabilities
     * @param {object} options - Options including supportsApplePay, supportsGooglePay
     * @returns {string[]} Array of enabled funding sources
     */
    function buildFundingSources(options) {
        const sources = []; // PayPal is included by default, no need to specify
        
        // PayPal SDK uses underscores: apple_pay, google_pay (not applepay, googlepay)
        if (options.supportsApplePay) {
            sources.push('paylater');  // Apple Pay requires PayPal to be enabled for it in merchant account
        }
        if (options.supportsGooglePay) {
            sources.push('venmo');  // Google Pay through PayPal requires special merchant setup
        }
        
        return sources;
    }

    /**
     * Initialize PayPal buttons
     * @param {string} clientId - PayPal client ID
     * @param {string} containerId - DOM element ID for buttons
     * @param {number} amount - Payment amount
     * @param {string} currency - Currency code
     * @param {object} dotNetRef - .NET object reference for callbacks
     * @param {string} refKey - Key to store the .NET reference
     * @param {object} options - Additional options (isSubscription, billingFrequency, supportsApplePay, supportsGooglePay)
     * @returns {Promise<boolean>}
     */
    wildwoodPayment.initPayPal = async function (clientId, containerId, amount, currency, dotNetRef, refKey, options) {
        try {
            // Default options - handle both object and undefined cases
            options = options || {};
            const isSubscription = options.isSubscription || false;
            const billingFrequency = options.billingFrequency || 'MONTH';
            const supportsApplePay = options.supportsApplePay || false;
            const supportsGooglePay = options.supportsGooglePay || false;
            const planId = options.planId || null;
            const description = options.description || '';
            
            console.log('WildwoodPayment/PayPal: Initializing with container:', containerId);
            console.log('WildwoodPayment/PayPal: ClientId prefix:', clientId ? clientId.substring(0, 15) + '...' : 'MISSING');
            console.log('WildwoodPayment/PayPal: Amount:', amount, 'Currency:', currency);
            console.log('WildwoodPayment/PayPal: Options:', { 
                isSubscription, 
                billingFrequency, 
                supportsApplePay, 
                supportsGooglePay,
                planId,
                hasDescription: !!description
            });

            if (!clientId) {
                console.error('WildwoodPayment/PayPal: Missing clientId!');
                return false;
            }

            // Store .NET reference
            if (dotNetRef && refKey) {
                paypalDotNetRefKey = refKey;
                wildwoodPayment.storeDotNetRef(refKey, dotNetRef);
            }

            // Store settings for theme updates
            currentContainerId = containerId;
            currentSettings = { clientId, amount, currency, options };

            // Build SDK URL with appropriate intent and funding sources
            const intent = isSubscription && planId ? 'subscription' : 'capture';
            const fundingSources = buildFundingSources({ supportsApplePay, supportsGooglePay });
            
            console.log('WildwoodPayment/PayPal: Loading SDK with intent:', intent);
            
            // Load PayPal SDK if not already loaded
            try {
                await loadPayPalSdk(clientId, currency, intent, fundingSources);
            } catch (sdkError) {
                console.error('WildwoodPayment/PayPal: SDK loading failed:', sdkError);
                return false;
            }

            const container = document.getElementById(containerId);
            if (!container) {
                console.error('WildwoodPayment/PayPal: Container not found:', containerId);
                return false;
            }

            // Clear existing buttons
            container.innerHTML = '';

            // Verify PayPal SDK is available
            if (typeof paypal === 'undefined' || !paypal.Buttons) {
                console.error('WildwoodPayment/PayPal: PayPal SDK not available after loading');
                return false;
            }

            // Build theme-aware style
            const style = buildPayPalStyle();
            console.log('WildwoodPayment/PayPal: Using themed style:', style);

            let buttonConfig;
            
            if (isSubscription && planId) {
                // Subscription flow - requires a pre-created PayPal plan
                console.log('WildwoodPayment/PayPal: Configuring subscription mode with plan:', planId);
                buttonConfig = {
                    style: style,
                    createSubscription: function(data, actions) {
                        return actions.subscription.create({
                            plan_id: planId
                        });
                    },
                    onApprove: async function(data, actions) {
                        try {
                            console.log('WildwoodPayment/PayPal: Subscription approved:', data.subscriptionID);
                            
                            if (paypalDotNetRefKey) {
                                await wildwoodPayment.invokeDotNet(paypalDotNetRefKey, 'OnPayPalApproved', {
                                    orderId: data.orderID,
                                    subscriptionId: data.subscriptionID,
                                    payerId: data.payerID,
                                    transactionId: data.subscriptionID // For subscriptions, use subscriptionID as transaction reference
                                });
                            }
                        } catch (error) {
                            console.error('WildwoodPayment/PayPal: Subscription approval error:', error);
                            if (paypalDotNetRefKey) {
                                await wildwoodPayment.invokeDotNet(paypalDotNetRefKey, 'OnPayPalError', {
                                    errorMessage: error.message || 'Failed to process subscription'
                                });
                            }
                        }
                    },
                    onCancel: function(data) {
                        console.log('WildwoodPayment/PayPal: Subscription cancelled');
                        if (paypalDotNetRefKey) {
                            wildwoodPayment.invokeDotNet(paypalDotNetRefKey, 'OnPayPalCancelled', {
                                orderId: data.orderID
                            });
                        }
                    },
                    onError: function(err) {
                        console.error('WildwoodPayment/PayPal: Subscription error:', err);
                        if (paypalDotNetRefKey) {
                            wildwoodPayment.invokeDotNet(paypalDotNetRefKey, 'OnPayPalError', {
                                errorMessage: err.message || 'PayPal subscription failed'
                            });
                        }
                    }
                };
            } else {
                // One-time payment flow
                console.log('WildwoodPayment/PayPal: Configuring one-time payment mode');
                buttonConfig = {
                    style: style,
                    createOrder: function (data, actions) {
                        return actions.order.create({
                            purchase_units: [{
                                amount: {
                                    value: amount.toFixed(2),
                                    currency_code: currency
                                },
                                description: description || undefined
                            }]
                        });
                    },
                    onApprove: async function (data, actions) {
                        try {
                            const details = await actions.order.capture();
                            console.log('WildwoodPayment/PayPal: Payment approved:', data.orderID);

                            // Notify .NET
                            if (paypalDotNetRefKey) {
                                await wildwoodPayment.invokeDotNet(paypalDotNetRefKey, 'OnPayPalApproved', {
                                    orderId: data.orderID,
                                    payerId: data.payerID,
                                    transactionId: details.purchase_units[0].payments.captures[0].id
                                });
                            }
                        } catch (error) {
                            console.error('WildwoodPayment/PayPal: Capture error:', error);
                            if (paypalDotNetRefKey) {
                                await wildwoodPayment.invokeDotNet(paypalDotNetRefKey, 'OnPayPalError', {
                                    errorMessage: error.message || 'Failed to capture payment'
                                });
                            }
                        }
                    },
                    onCancel: function (data) {
                        console.log('WildwoodPayment/PayPal: Payment cancelled');
                        if (paypalDotNetRefKey) {
                            wildwoodPayment.invokeDotNet(paypalDotNetRefKey, 'OnPayPalCancelled', {
                                orderId: data.orderID
                            });
                        }
                    },
                    onError: function (err) {
                        console.error('WildwoodPayment/PayPal: Error:', err);
                        if (paypalDotNetRefKey) {
                            wildwoodPayment.invokeDotNet(paypalDotNetRefKey, 'OnPayPalError', {
                                errorMessage: err.message || 'PayPal payment failed'
                            });
                        }
                    }
                };
            }

            console.log('WildwoodPayment/PayPal: Creating Buttons with config');
            paypalButtons = paypal.Buttons(buttonConfig);

            console.log('WildwoodPayment/PayPal: Rendering buttons to:', '#' + containerId);
            await paypalButtons.render('#' + containerId);
            console.log('WildwoodPayment/PayPal: Buttons rendered successfully');
            wildwoodPayment.registerProvider('paypal');
            return true;
        } catch (error) {
            console.error('WildwoodPayment/PayPal: Initialization error:', error);
            console.error('WildwoodPayment/PayPal: Error stack:', error.stack);
            return false;
        }
    };

    /**
     * Update PayPal buttons when theme changes
     * Note: PayPal buttons need to be re-rendered for style changes
     */
    wildwoodPayment.updatePayPalTheme = async function () {
        if (currentContainerId && currentSettings) {
            // Close existing buttons and re-render with new style
            if (paypalButtons) {
                try {
                    paypalButtons.close();
                } catch (e) {
                    // Ignore
                }
            }

            const container = document.getElementById(currentContainerId);
            if (container) {
                container.innerHTML = '';
                
                // Re-initialize with current settings
                await wildwoodPayment.initPayPal(
                    currentSettings.clientId,
                    currentContainerId,
                    currentSettings.amount,
                    currentSettings.currency,
                    null, // dotNetRef is already stored
                    paypalDotNetRefKey,
                    currentSettings.options
                );
                
                console.log('WildwoodPayment/PayPal: Theme updated and buttons re-rendered');
            }
        }
    };

    /**
     * Load PayPal SDK dynamically
     * @param {string} clientId - PayPal client ID
     * @param {string} currency - Currency code
     * @param {string} intent - Payment intent ('capture' or 'subscription')
     * @param {string[]} fundingSources - Array of funding sources to enable
     * @returns {Promise<void>}
     */
    async function loadPayPalSdk(clientId, currency, intent, fundingSources) {
        return new Promise((resolve, reject) => {
            const scriptId = 'paypal-sdk';
            const existingScript = document.getElementById(scriptId);
            
            // Build SDK URL with components
            let sdkUrl = 'https://www.paypal.com/sdk/js?client-id=' + encodeURIComponent(clientId) + 
                         '&currency=' + encodeURIComponent(currency);
            
            // Add intent for subscriptions
            if (intent === 'subscription') {
                sdkUrl += '&intent=subscription&vault=true';
            }
            
            console.log('WildwoodPayment/PayPal: Loading SDK from:', sdkUrl);
            
            // If PayPal is already loaded and ready, just resolve
            if (typeof paypal !== 'undefined' && paypal.Buttons) {
                console.log('WildwoodPayment/PayPal: SDK already loaded, using existing instance');
                resolve();
                return;
            }
            
            // If script element exists but paypal is not defined, remove and reload
            if (existingScript) {
                console.log('WildwoodPayment/PayPal: Removing stale script element');
                existingScript.remove();
            }

            const script = document.createElement('script');
            script.id = scriptId;
            script.src = sdkUrl;
            script.async = true;
            script.onload = () => {
                console.log('WildwoodPayment/PayPal: SDK loaded successfully');
                // Small delay to ensure PayPal object is fully initialized
                setTimeout(() => {
                    if (typeof paypal !== 'undefined' && paypal.Buttons) {
                        resolve();
                    } else {
                        reject(new Error('PayPal SDK loaded but paypal.Buttons not available'));
                    }
                }, 100);
            };
            script.onerror = (e) => {
                console.error('WildwoodPayment/PayPal: Failed to load SDK script', e);
                reject(new Error('Failed to load PayPal SDK'));
            };
            document.head.appendChild(script);
        });
    }

    /**
     * Dispose PayPal buttons and clean up
     */
    wildwoodPayment.disposePayPal = function () {
        if (paypalButtons) {
            try {
                paypalButtons.close();
                console.log('WildwoodPayment/PayPal: Buttons closed');
            } catch (e) {
                // Buttons might already be closed
            }
            paypalButtons = null;
        }

        // Clean up state
        currentContainerId = null;
        currentSettings = null;

        // Clean up .NET reference
        if (paypalDotNetRefKey) {
            wildwoodPayment.removeDotNetRef(paypalDotNetRefKey);
            paypalDotNetRefKey = null;
        }
    };

    console.log('WildwoodPayment/PayPal: Script loaded');

})(window.wildwoodPayment);
