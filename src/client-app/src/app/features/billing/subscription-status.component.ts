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
    <section class="rounded-xl border border-slate-200 bg-white p-5" aria-live="polite">
      <h2 class="text-lg font-semibold text-slate-900">Your subscription</h2>

      @if (status.isLoading()) {
        <p class="mt-2 text-sm text-slate-500" role="status">Loading…</p>
      } @else if (status.error()) {
        <p class="mt-2 text-sm text-red-700" role="alert">Could not load subscription.</p>
      } @else if (status.value(); as s) {
        <p class="mt-2 text-xl font-semibold text-slate-900">{{ s.tierName }}</p>
        <p class="text-sm text-slate-500">Renews on {{ s.renewalDate }}</p>

        <div class="mt-4 space-y-3">
          @for (meter of s.meters; track meter.label) {
            <div class="space-y-1">
              <div class="flex items-center justify-between text-sm">
                <span class="text-slate-700">{{ meter.label }}</span>
                <span class="text-slate-500">{{ meter.used }} / {{ meter.limit }}</span>
              </div>
              <progress
                class="h-2 w-full overflow-hidden rounded"
                [value]="meter.used"
                [max]="meter.limit"
              ></progress>
            </div>
          }
        </div>
      }
    </section>
  `,
})
export class SubscriptionStatusComponent {
  private readonly billing = inject(RazorpayBillingService);

  private readonly refreshKey = computed(() => this.billing.paymentSuccess());

  protected readonly status = httpResource<SubscriptionStatus>(() => ({
    url: '/api/v1/billing/subscription',
    params: { v: this.refreshKey() },
  }));
}
