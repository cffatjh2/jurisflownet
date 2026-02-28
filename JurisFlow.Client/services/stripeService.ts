// Stripe Payment Service
// US standard payment processing

const STRIPE_PUBLISHABLE_KEY = (typeof import.meta !== 'undefined' && (import.meta as any).env?.VITE_STRIPE_PUBLISHABLE_KEY) || '';

export interface PaymentIntent {
    id: string;
    clientSecret: string;
    amount: number;
    currency: string;
    status: string;
}

export interface CreatePaymentRequest {
    invoiceId: string;
    amount: number;
    currency?: string;
    description?: string;
    customerEmail?: string;
    metadata?: Record<string, string>;
}

export interface PaymentResult {
    success: boolean;
    paymentId?: string;
    receiptUrl?: string;
    error?: string;
}

// Check if Stripe is configured
export const isStripeConfigured = (): boolean => {
    return !!STRIPE_PUBLISHABLE_KEY;
};

// Create a payment intent on the server
export const createPaymentIntent = async (request: CreatePaymentRequest): Promise<PaymentIntent | null> => {
    try {
        const response = await fetch('/api/payments/create-intent', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            'Authorization': `Bearer ${localStorage.getItem('auth_token')}`
            },
            body: JSON.stringify({
                invoiceId: request.invoiceId,
                amount: request.amount,
                currency: request.currency,
                description: request.description,
                payerEmail: request.customerEmail
            })
        });

        if (!response.ok) {
            throw new Error('Failed to create payment intent');
        }

        return await response.json();
    } catch (error) {
        console.error('Failed to create payment intent:', error);
        return null;
    }
};

// Confirm payment (after user enters card details)
export const confirmPayment = async (
    transactionId: string,
    paymentIntentId: string
): Promise<PaymentResult> => {
    try {
        const response = await fetch('/api/payments/confirm', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            'Authorization': `Bearer ${localStorage.getItem('auth_token')}`
            },
            body: JSON.stringify({ transactionId, paymentIntentId })
        });

        const result = await response.json();

        if (!response.ok) {
            return { success: false, error: result.error || 'Payment failed' };
        }

        return {
            success: true,
            paymentId: result.paymentId,
            receiptUrl: result.receiptUrl
        };
    } catch (error) {
        console.error('Payment confirmation failed:', error);
        return { success: false, error: 'Payment confirmation failed' };
    }
};

// Get payment history for an invoice
export const getPaymentHistory = async (invoiceId: string): Promise<any[]> => {
    try {
        const response = await fetch(`/api/payments/invoice/${invoiceId}`, {
            headers: {
            'Authorization': `Bearer ${localStorage.getItem('auth_token')}`
            }
        });

        if (!response.ok) {
            return [];
        }

        return await response.json();
    } catch (error) {
        console.error('Failed to get payment history:', error);
        return [];
    }
};

// Request a refund
export const requestRefund = async (
    paymentId: string,
    amount?: number,
    reason?: string
): Promise<PaymentResult> => {
    try {
        const response = await fetch(`/api/payments/${paymentId}/refund`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${localStorage.getItem('auth_token')}`
            },
            body: JSON.stringify({ amount, reason })
        });

        const result = await response.json();

        if (!response.ok) {
            return { success: false, error: result.error || 'Refund failed' };
        }

        return { success: true, paymentId };
    } catch (error) {
        console.error('Refund request failed:', error);
        return { success: false, error: 'Refund request failed' };
    }
};

// Format amount for display
export const formatCurrency = (amount: number, currency: string = 'usd'): string => {
    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: currency.toUpperCase()
    }).format(amount);
};

// Format cents to dollars
export const centsToDollars = (cents: number): number => {
    return cents / 100;
};

// Format dollars to cents (for Stripe)
export const dollarsToCents = (dollars: number): number => {
    return Math.round(dollars * 100);
};

// Schedule payment reminder
export const schedulePaymentReminder = async (
    invoiceId: string,
    scheduledAt: Date,
    type: 'email' | 'push' = 'email'
): Promise<boolean> => {
    try {
        const response = await fetch('/api/payments/schedule-reminder', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            'Authorization': `Bearer ${localStorage.getItem('auth_token')}`
            },
            body: JSON.stringify({
                invoiceId,
                scheduledAt: scheduledAt.toISOString(),
                type
            })
        });

        return response.ok;
    } catch (error) {
        console.error('Failed to schedule reminder:', error);
        return false;
    }
};

// Get Stripe publishable key (for frontend SDK)
export const getStripePublishableKey = (): string => {
    return STRIPE_PUBLISHABLE_KEY;
};
