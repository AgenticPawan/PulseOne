import { Component, computed, inject } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { RazorpayBillingService } from '../../core/services/razorpay-billing.service';

interface UsageMeter {
  label: string;
  used: number;
  limit: number;
}

interface SubscriptionStatus {
  tierName: string;
  renewalDate: string;
  meters: UsageMeter[];
}

/**
 * Current-plan widget (prompt 02): tier name, renewal date, and usage meters. Reads from
 * `httpResource<SubscriptionStatus>` so it refreshes reactively. It also depends on the billing
 * service's `paymentSuccess` signal, so a server-verified upgrade refreshes the widget automatically.
 */
@Component({
  selector: 'pulseone-subscription-status',
  standalone: true,
  template: `
    <section class="subscription-status">
      <h2 class="subscription-status__title">Your subscription</h2>

      @if (status.isLoading()) {
        <p>Loading…</p>
      } @else if (status.error()) {
        <p class="subscription-status__error">Could not load subscription.</p>
      } @else if (status.value(); as s) {
        <p class="subscription-status__tier">{{ s.tierName }}</p>
        <p class="subscription-status__renewal">Renews on {{ s.renewalDate }}</p>

        <div class="subscription-status__meters">
          @for (meter of s.meters; track meter.label) {
            <div class="meter">
              <span class="meter__label">{{ meter.label }}</span>
              <progress class="meter__bar" [value]="meter.used" [max]="meter.limit"></progress>
              <span class="meter__value">{{ meter.used }} / {{ meter.limit }}</span>
            </div>
          }
        </div>
      }
    </section>
  `,
  styleUrl: './subscription-status.component.scss',
})
export class SubscriptionStatusComponent {
  private readonly billing = inject(RazorpayBillingService);

  private readonly refreshKey = computed(() => this.billing.paymentSuccess());

  protected readonly status = httpResource<SubscriptionStatus>(() => ({
    url: '/api/v1/billing/subscription',
    params: { v: this.refreshKey() },
  }));
}
