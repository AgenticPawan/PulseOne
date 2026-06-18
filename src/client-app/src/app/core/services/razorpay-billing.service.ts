import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/**
 * Public, non-secret runtime configuration served by GET /api/v1/config/public. Carries ONLY the
 * publishable Razorpay checkout key id — never the webhook/key secret (blueprint §6.5).
 */
interface PublicConfig {
  razorpayKeyId: string;
}

/** The Razorpay Checkout success callback shape (a subset of what checkout.js emits). */
interface RazorpayCheckoutResult {
  razorpay_payment_id: string;
  razorpay_order_id: string;
  razorpay_signature: string;
}

/**
 * Tenant-portal Razorpay integration (blueprint §6.5). Closes two v1 defects (Appendix A #6/#7):
 *
 *  1. The publishable key is NEVER hardcoded — it is fetched from /api/v1/config/public at checkout
 *     time. No publishable-key literal exists anywhere in the SPA source (hard constraint / checklist).
 *  2. HTTP fetching across the billing feature uses Angular 20 `httpResource` (in the components),
 *     not `effect()` + manual subscribe. This service only orchestrates the one imperative,
 *     user-gesture-driven flow (opening Razorpay Checkout), which is not a reactive read.
 *
 * SECURITY: the Razorpay callback result is UNTRUSTED. We forward it to /api/v1/billing/verify-payment
 * for server-authoritative signature verification and only then treat the payment as successful.
 */
@Injectable({ providedIn: 'root' })
export class RazorpayBillingService {
  private readonly http = inject(HttpClient);

  /**
   * Emits (increments) when the BACKEND has verified a payment. Components listen via a signal change
   * to refresh their `httpResource`s — no event bus, no manual subscription churn.
   */
  private readonly _paymentSuccess = signal(0);
  readonly paymentSuccess = this._paymentSuccess.asReadonly();

  /** Loads the Razorpay checkout.js script exactly once (idempotent). */
  private async loadScript(): Promise<void> {
    if ((window as unknown as { Razorpay?: unknown }).Razorpay) {
      return;
    }
    await new Promise<void>((resolve, reject) => {
      const s = document.createElement('script');
      s.src = 'https://checkout.razorpay.com/v1/checkout.js';
      s.onload = () => resolve();
      s.onerror = () => reject(new Error('Razorpay checkout failed to load'));
      document.head.appendChild(s);
    });
  }

  /**
   * Opens Razorpay Checkout for the given order. The publishable key is fetched at this moment (not at
   * service init) so a rotated key is always current and the SPA never embeds it.
   */
  async initiateCheckout(orderId: string, amountInRupees: number, tenantName: string): Promise<void> {
    await this.loadScript();
    const cfg = await firstValueFrom(this.http.get<PublicConfig>('/api/v1/config/public'));

    const accent = getComputedStyle(document.documentElement)
      .getPropertyValue('--tenant-accent')
      .trim();

    const RazorpayCtor = (window as unknown as { Razorpay: new (opts: unknown) => { open(): void } }).Razorpay;
    const checkout = new RazorpayCtor({
      key: cfg.razorpayKeyId, // from the server, never hardcoded
      amount: amountInRupees * 100, // Razorpay expects the smallest currency unit (paise)
      currency: 'INR',
      name: 'PulseOne',
      description: `Subscription for ${tenantName}`,
      order_id: orderId,
      handler: (r: RazorpayCheckoutResult) => void this.verifyOnBackend(r),
      theme: { color: accent },
    });
    checkout.open();
  }

  /**
   * Server-authoritative verification. The client's Razorpay result is untrusted until the backend
   * recomputes the signature; only a verified response flips `paymentSuccess`.
   */
  private async verifyOnBackend(p: RazorpayCheckoutResult): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<{ verified: boolean }>('/api/v1/billing/verify-payment', {
        razorpayPaymentId: p.razorpay_payment_id,
        razorpayOrderId: p.razorpay_order_id,
        razorpaySignature: p.razorpay_signature,
      }),
    );

    if (res.verified) {
      // Bump the signal so listening components re-fetch their httpResource (plans/subscription).
      this._paymentSuccess.update((n) => n + 1);
    }
  }
}
