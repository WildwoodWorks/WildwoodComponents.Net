/**
 * WildwoodPayment Google Pay Provider Script
 * Handles Google Pay initialization and payment processing.
 * Requires Google Pay API to be loaded.
 */

(function (wildwoodPayment) {
    'use strict';

    // Google Pay-specific state
    let googlePayClient = null;
    let googlePayConfig = null;
    let googlePayDotNetRefKey = null;

    /**
     * Check if Google Pay is available
     * @returns {Promise<boolean>}
     */
    wildwoodPayment.isGooglePayAvailable = async function () {
        try {
            if (typeof google === 'undefined' || !google.payments || !google.payments.api) {
                return false;
            }

            const paymentsClient = new google.payments.api.PaymentsClient({
                environment: 'TEST'
            });

            const isReadyToPay = await paymentsClient.isReadyToPay({
                apiVersion: 2,
                apiVersionMinor: 0,
                allowedPaymentMethods: [{
                    type: 'CARD',
                    parameters: {
                        allowedAuthMethods: ['PAN_ONLY', 'CRYPTOGRAM_3DS'],
                        allowedCardNetworks: ['VISA', 'MASTERCARD', 'AMEX', 'DISCOVER']
                    }
                }]
            });

            return isReadyToPay.result;
        } catch (error) {
            console.warn('WildwoodPayment/GooglePay: Availability check failed:', error);
            return false;
        }
    };

    /**
     * Initialize Google Pay
     * @param {string} merchantId - Google Pay merchant ID
     * @param {number} amount - Payment amount
     * @param {string} currency - Currency code
     * @param {string} environment - 'TEST' or 'PRODUCTION'
     * @param {string} gatewayMerchantId - Gateway-specific merchant ID
     * @param {object} dotNetRef - .NET object reference for callbacks
     * @param {string} refKey - Key to store the .NET reference
     * @returns {Promise<boolean>}
     */
    wildwoodPayment.initGooglePay = async function (merchantId, amount, currency, environment, gatewayMerchantId, dotNetRef, refKey) {
        try {
            console.log('WildwoodPayment/GooglePay: Initializing');

            // Load Google Pay API if not already loaded
            if (typeof google === 'undefined' || !google.payments) {
                await loadGooglePayApi();
            }

            // Store configuration
            googlePayConfig = {
                merchantId: merchantId,
                amount: amount,
                currency: currency,
                environment: environment || 'TEST',
                gatewayMerchantId: gatewayMerchantId
            };

            // Store .NET reference
            if (dotNetRef && refKey) {
                googlePayDotNetRefKey = refKey;
                wildwoodPayment.storeDotNetRef(refKey, dotNetRef);
            }

            googlePayClient = new google.payments.api.PaymentsClient({
                environment: googlePayConfig.environment
            });

            // Create the Google Pay button
            const buttonContainer = document.getElementById('google-pay-button');
            if (buttonContainer) {
                const button = googlePayClient.createButton({
                    onClick: wildwoodPayment.startGooglePaySession,
                    buttonColor: 'black',
                    buttonType: 'pay'
                });
                buttonContainer.innerHTML = '';
                buttonContainer.appendChild(button);
            }

            console.log('WildwoodPayment/GooglePay: Initialized successfully');
            wildwoodPayment.registerProvider('googlepay');
            return true;
        } catch (error) {
            console.error('WildwoodPayment/GooglePay: Initialization error:', error);
            return false;
        }
    };

    /**
     * Load Google Pay API dynamically
     * @returns {Promise<void>}
     */
    async function loadGooglePayApi() {
        return new Promise((resolve, reject) => {
            const scriptId = 'google-pay-api';
            if (document.getElementById(scriptId)) {
                resolve();
                return;
            }

            const script = document.createElement('script');
            script.id = scriptId;
            script.src = 'https://pay.google.com/gp/p/js/pay.js';
            script.async = true;
            script.onload = resolve;
            script.onerror = () => reject(new Error('Failed to load Google Pay API'));
            document.head.appendChild(script);
        });
    }

    /**
     * Start a Google Pay payment session
     * @returns {Promise<void>}
     */
    wildwoodPayment.startGooglePaySession = async function () {
        if (!googlePayConfig || !googlePayClient) {
            console.error('WildwoodPayment/GooglePay: Not initialized');
            return;
        }

        const paymentDataRequest = {
            apiVersion: 2,
            apiVersionMinor: 0,
            merchantInfo: {
                merchantId: googlePayConfig.merchantId,
                merchantName: 'Merchant'
            },
            allowedPaymentMethods: [{
                type: 'CARD',
                parameters: {
                    allowedAuthMethods: ['PAN_ONLY', 'CRYPTOGRAM_3DS'],
                    allowedCardNetworks: ['VISA', 'MASTERCARD', 'AMEX', 'DISCOVER']
                },
                tokenizationSpecification: {
                    type: 'PAYMENT_GATEWAY',
                    parameters: {
                        gateway: 'stripe',
                        'stripe:version': '2020-08-27',
                        'stripe:publishableKey': googlePayConfig.gatewayMerchantId || ''
                    }
                }
            }],
            transactionInfo: {
                totalPriceStatus: 'FINAL',
                totalPrice: googlePayConfig.amount.toFixed(2),
                currencyCode: googlePayConfig.currency,
                countryCode: 'US'
            }
        };

        try {
            const paymentData = await googlePayClient.loadPaymentData(paymentDataRequest);
            console.log('WildwoodPayment/GooglePay: Payment authorized');

            // Notify .NET
            if (googlePayDotNetRefKey) {
                await wildwoodPayment.invokeDotNet(googlePayDotNetRefKey, 'OnGooglePayAuthorized', {
                    paymentToken: paymentData.paymentMethodData.tokenizationData.token
                });
            }
        } catch (error) {
            if (error.statusCode === 'CANCELED') {
                console.log('WildwoodPayment/GooglePay: Payment cancelled by user');
                if (googlePayDotNetRefKey) {
                    wildwoodPayment.invokeDotNet(googlePayDotNetRefKey, 'OnGooglePayCancelled');
                }
            } else {
                console.error('WildwoodPayment/GooglePay: Payment error:', error);
                if (googlePayDotNetRefKey) {
                    wildwoodPayment.invokeDotNet(googlePayDotNetRefKey, 'OnGooglePayError', {
                        errorMessage: error.message || 'Google Pay payment failed'
                    });
                }
            }
        }
    };

    /**
     * Dispose Google Pay and clean up
     */
    wildwoodPayment.disposeGooglePay = function () {
        googlePayClient = null;
        googlePayConfig = null;

        // Clean up .NET reference
        if (googlePayDotNetRefKey) {
            wildwoodPayment.removeDotNetRef(googlePayDotNetRefKey);
            googlePayDotNetRefKey = null;
        }

        console.log('WildwoodPayment/GooglePay: Disposed');
    };

    console.log('WildwoodPayment/GooglePay: Script loaded');

})(window.wildwoodPayment);
