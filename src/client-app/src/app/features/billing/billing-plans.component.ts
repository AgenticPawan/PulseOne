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
    <section class="billing-plans">
      <h2 class="billing-plans__title">Subscription plans</h2>

      @if (plans.isLoading()) {
        <p class="billing-plans__status">Loading plans…</p>
      } @else if (plans.error()) {
        <p class="billing-plans__status billing-plans__status--error">Could not load plans.</p>
      } @else {
        <div class="billing-plans__grid">
          @for (plan of plans.value(); track plan.id) {
            <article class="plan-card" [class.plan-card--current]="plan.isCurrent">
              <h3 class="plan-card__name">{{ plan.name }}</h3>
              <p class="plan-card__price">₹{{ plan.priceInRupees }}/mo</p>
              <ul class="plan-card__features">
                @for (feature of plan.features; track feature) {
                  <li>{{ feature }}</li>
                }
              </ul>
              @if (plan.isCurrent) {
                <span class="plan-card__badge">Current plan</span>
              } @else {
                <button type="button" class="plan-card__upgrade" (click)="upgrade(plan)">
                  Upgrade
                </button>
              }
            </article>
          }
        </div>
      }

      @if (toast()) {
        <div class="billing-plans__toast" role="status">{{ toast() }}</div>
      }
    </section>
  `,
  styleUrl: './billing-plans.component.scss',
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
