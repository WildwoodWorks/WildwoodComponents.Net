/**
 * WildwoodPayment Apple Pay Provider Script
 * Handles Apple Pay initialization and payment processing.
 * Works with Safari and Apple Pay enabled devices.
 */

(function (wildwoodPayment) {
    'use strict';

    // Apple Pay-specific state
    let applePayConfig = null;
    let applePayDotNetRefKey = null;

    /**
     * Check if Apple Pay is available
     * @returns {Promise<boolean>}
     */
    wildwoodPayment.isApplePayAvailable = async function () {
        try {
            if (window.ApplePaySession && ApplePaySession.canMakePayments()) {
                return true;
            }
            return false;
        } catch (error) {
            console.warn('WildwoodPayment/ApplePay: Availability check failed:', error);
            return false;
        }
    };

    /**
     * Initialize Apple Pay
     * @param {string} merchantId - Apple Pay merchant ID
     * @param {number} amount - Payment amount
     * @param {string} currency - Currency code
     * @param {string} merchantName - Display name for the merchant
     * @param {object} dotNetRef - .NET object reference for callbacks
     * @param {string} refKey - Key to store the .NET reference
     * @returns {Promise<boolean>}
     */
    wildwoodPayment.initApplePay = async function (merchantId, amount, currency, merchantName, dotNetRef, refKey) {
        try {
            console.log('WildwoodPayment/ApplePay: Initializing');

            if (!window.ApplePaySession) {
                console.warn('WildwoodPayment/ApplePay: Not available on this device/browser');
                return false;
            }

            // Store configuration
            applePayConfig = {
                merchantId: merchantId,
                amount: amount,
                currency: currency,
                merchantName: merchantName || 'Merchant'
            };

            // Store .NET reference
            if (dotNetRef && refKey) {
                applePayDotNetRefKey = refKey;
                wildwoodPayment.storeDotNetRef(refKey, dotNetRef);
            }

            // Set up the Apple Pay button
            const button = document.getElementById('apple-pay-button');
            if (button) {
                button.onclick = wildwoodPayment.startApplePaySession;
            }

            console.log('WildwoodPayment/ApplePay: Initialized successfully');
            wildwoodPayment.registerProvider('applepay');
            return true;
        } catch (error) {
            console.error('WildwoodPayment/ApplePay: Initialization error:', error);
            return false;
        }
    };

    /**
     * Start an Apple Pay payment session
     * @returns {Promise<void>}
     */
    wildwoodPayment.startApplePaySession = async function () {
        if (!applePayConfig) {
            console.error('WildwoodPayment/ApplePay: Not initialized');
            return;
        }

        const request = {
            countryCode: 'US',
            currencyCode: applePayConfig.currency,
            supportedNetworks: ['visa', 'masterCard', 'amex', 'discover'],
            merchantCapabilities: ['supports3DS'],
            total: {
                label: applePayConfig.merchantName,
                amount: applePayConfig.amount.toFixed(2)
            }
        };

        try {
            const session = new ApplePaySession(3, request);

            session.onvalidatemerchant = async function (event) {
                try {
                    console.log('WildwoodPayment/ApplePay: Validating merchant...');
                    
                    // Call server to validate merchant
                    if (applePayDotNetRefKey) {
                        const merchantSession = await wildwoodPayment.invokeDotNet(
                            applePayDotNetRefKey, 
                            'OnApplePayValidateMerchant', 
                            event.validationURL
                        );
                        
                        if (merchantSession) {
                            session.completeMerchantValidation(merchantSession);
                        } else {
                            session.abort();
                        }
                    } else {
                        console.error('WildwoodPayment/ApplePay: No .NET reference for validation');
                        session.abort();
                    }
                } catch (error) {
                    console.error('WildwoodPayment/ApplePay: Merchant validation failed:', error);
                    session.abort();
                }
            };

            session.onpaymentauthorized = async function (event) {
                console.log('WildwoodPayment/ApplePay: Payment authorized');

                try {
                    if (applePayDotNetRefKey) {
                        const result = await wildwoodPayment.invokeDotNet(
                            applePayDotNetRefKey,
                            'OnApplePayAuthorized',
                            {
                                paymentToken: JSON.stringify(event.payment.token)
                            }
                        );

                        if (result && result.success) {
                            session.completePayment(ApplePaySession.STATUS_SUCCESS);
                        } else {
                            session.completePayment(ApplePaySession.STATUS_FAILURE);
                        }
                    }
                } catch (error) {
                    console.error('WildwoodPayment/ApplePay: Authorization processing failed:', error);
                    session.completePayment(ApplePaySession.STATUS_FAILURE);
                }
            };

            session.oncancel = function () {
                console.log('WildwoodPayment/ApplePay: Session cancelled');
                if (applePayDotNetRefKey) {
                    wildwoodPayment.invokeDotNet(applePayDotNetRefKey, 'OnApplePayCancelled');
                }
            };

            session.begin();
        } catch (error) {
            console.error('WildwoodPayment/ApplePay: Session error:', error);
            if (applePayDotNetRefKey) {
                wildwoodPayment.invokeDotNet(applePayDotNetRefKey, 'OnApplePayError', {
                    errorMessage: error.message || 'Apple Pay session failed'
                });
            }
        }
    };

    /**
     * Dispose Apple Pay and clean up
     */
    wildwoodPayment.disposeApplePay = function () {
        applePayConfig = null;

        // Clean up .NET reference
        if (applePayDotNetRefKey) {
            wildwoodPayment.removeDotNetRef(applePayDotNetRefKey);
            applePayDotNetRefKey = null;
        }

        console.log('WildwoodPayment/ApplePay: Disposed');
    };

    console.log('WildwoodPayment/ApplePay: Script loaded');

})(window.wildwoodPayment);
