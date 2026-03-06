/**
 * WildwoodPayment Base Script
 * Provides shared utilities and namespace for payment provider scripts.
 * This script is loaded first before any provider-specific scripts.
 */

window.wildwoodPayment = window.wildwoodPayment || {};

(function (wildwoodPayment) {
    'use strict';

    // Track loaded providers
    wildwoodPayment._loadedProviders = wildwoodPayment._loadedProviders || {};
    wildwoodPayment._dotNetRefs = wildwoodPayment._dotNetRefs || {};

    /**
     * Registers a provider as loaded
     * @param {string} providerName - The provider name (e.g., 'stripe', 'paypal')
     */
    wildwoodPayment.registerProvider = function (providerName) {
        wildwoodPayment._loadedProviders[providerName] = true;
        console.log('WildwoodPayment: Provider registered:', providerName);
    };

    /**
     * Checks if a provider is loaded
     * @param {string} providerName - The provider name
     * @returns {boolean}
     */
    wildwoodPayment.isProviderLoaded = function (providerName) {
        return wildwoodPayment._loadedProviders[providerName] === true;
    };

    /**
     * Stores a .NET object reference for callbacks
     * @param {string} key - Unique key for the reference
     * @param {object} dotNetRef - The .NET object reference
     */
    wildwoodPayment.storeDotNetRef = function (key, dotNetRef) {
        wildwoodPayment._dotNetRefs[key] = dotNetRef;
    };

    /**
     * Gets a stored .NET object reference
     * @param {string} key - Unique key for the reference
     * @returns {object|null}
     */
    wildwoodPayment.getDotNetRef = function (key) {
        return wildwoodPayment._dotNetRefs[key] || null;
    };

    /**
     * Removes a stored .NET object reference
     * @param {string} key - Unique key for the reference
     */
    wildwoodPayment.removeDotNetRef = function (key) {
        delete wildwoodPayment._dotNetRefs[key];
    };

    /**
     * Safely invokes a method on a .NET object reference
     * @param {string} key - Key for the stored reference
     * @param {string} methodName - Method name to invoke
     * @param {...any} args - Arguments to pass
     * @returns {Promise}
     */
    wildwoodPayment.invokeDotNet = async function (key, methodName, ...args) {
        const ref = wildwoodPayment.getDotNetRef(key);
        if (ref) {
            try {
                return await ref.invokeMethodAsync(methodName, ...args);
            } catch (e) {
                console.warn('WildwoodPayment: Failed to invoke .NET method:', methodName, e);
            }
        }
        return null;
    };

    /**
     * Formats an amount for display
     * @param {number} amount - The amount
     * @param {string} currency - Currency code (e.g., 'USD')
     * @returns {string}
     */
    wildwoodPayment.formatAmount = function (amount, currency) {
        try {
            return new Intl.NumberFormat(undefined, {
                style: 'currency',
                currency: currency
            }).format(amount);
        } catch (e) {
            return currency + ' ' + amount.toFixed(2);
        }
    };

    /**
     * Generates a unique ID for elements
     * @param {string} prefix - Prefix for the ID
     * @returns {string}
     */
    wildwoodPayment.generateId = function (prefix) {
        return (prefix || 'ww') + '-' + Math.random().toString(36).substring(2, 10);
    };

    console.log('WildwoodPayment: Base script loaded');

})(window.wildwoodPayment);
