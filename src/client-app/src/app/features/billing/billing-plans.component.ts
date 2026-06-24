import { Component, computed, inject } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { RazorpayBillingService } from '../../core/services/razorpay-billing.service';

interface Plan {
  id: string;
  name: string;
  priceInRupees: number;
  features: string[];
  isCurrent: boolean;
}

/**
 * Available subscription plans (blueprint §6.5, prompt 02). Data is fetched with Angular 20
 * `httpResource` — NOT the imperative subscribe-in-an-effect anti-pattern — so it recomputes
 * reactively and cancels in-flight requests when inputs change.
 *
 * After a server-VERIFIED payment, the `RazorpayBillingService.paymentSuccess` signal increments; we
 * mirror it into a refresh key the resource depends on, so plans (and the "current tier") refresh
 * automatically without any manual subscription. The success toast is a `computed` derived from the
 * same signal — no reactive side-effect block (the v1 anti-pattern, Appendix A #7) in this feature.
 */
@Component({
  selector: 'pulseone-billing-plans',
  standalone: true,
  template: `
    <section>
      <h2 class="mb-4 text-lg font-semibold text-slate-900">Subscription plans</h2>

      <div aria-live="polite">
        @if (plans.isLoading()) {
          <p class="text-sm text-slate-500" role="status">Loading plans…</p>
        } @else if (plans.error()) {
          <p class="text-sm text-red-700" role="alert">Could not load plans.</p>
        } @else {
          <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
            @for (plan of plans.value(); track plan.id) {
              <article
                class="flex flex-col gap-2 rounded-xl border p-5"
                [class.border-slate-200]="!plan.isCurrent"
                [class.border-[var(--tenant-accent,#2563eb)]]="plan.isCurrent"
              >
                <h3 class="font-semibold text-slate-900">{{ plan.name }}</h3>
                <p class="text-2xl font-bold text-slate-900">₹{{ plan.priceInRupees }}/mo</p>
                <ul class="flex-1 list-inside list-disc text-sm text-slate-600">
                  @for (feature of plan.features; track feature) {
                    <li>{{ feature }}</li>
                  }
                </ul>
                @if (plan.isCurrent) {
                  <span class="text-xs font-semibold text-[var(--tenant-accent,#2563eb)]">
                    Current plan
                  </span>
                } @else {
                  <button
                    type="button"
                    class="mt-auto rounded-md bg-[var(--tenant-accent,#2563eb)] px-4 py-2 text-sm font-medium text-white hover:opacity-90"
                    (click)="upgrade(plan)"
                  >
                    Upgrade
                  </button>
                }
              </article>
            }
          </div>
        }
      </div>

      @if (toast()) {
        <div
          class="mt-4 rounded-md bg-emerald-50 px-4 py-3 text-sm text-emerald-800"
          role="status"
        >
          {{ toast() }}
        </div>
      }
    </section>
  `,
})
export class BillingPlansComponent {
  private readonly billing = inject(RazorpayBillingService);

  /** Bumped when the service reports a server-verified payment, driving an automatic refetch. */
  private readonly refreshKey = computed(() => this.billing.paymentSuccess());

  /** Success toast text, derived purely from the verified-payment signal (no effect/timer). */
  protected readonly toast = computed(() =>
    this.billing.paymentSuccess() > 0 ? 'Payment successful — your plan has been updated.' : null,
  );

  // Reactive fetch: re-runs when refreshKey changes (post-payment) and cancels the prior request.
  protected readonly plans = httpResource<Plan[]>(() => ({
    url: '/api/v1/billing/plans',
    // Touch the refresh key so a verified payment re-evaluates the request and re-fetches.
    params: { v: this.refreshKey() },
  }));

  protected async upgrade(plan: Plan): Promise<void> {
    // In a full flow the order id comes from a server "create order" call; the plan price seeds the
    // checkout amount. The publishable key is fetched inside the service (never hardcoded here).
    await this.billing.initiateCheckout(plan.id, plan.priceInRupees, 'PulseOne tenant');
  }
}
