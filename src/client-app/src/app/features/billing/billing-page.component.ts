import { Component } from '@angular/core';
import { BillingPlansComponent } from './billing-plans.component';
import { BillingHistoryComponent } from './billing-history.component';
import { SubscriptionStatusComponent } from './subscription-status.component';

/**
 * Billing feature shell (Phase 4 slice of the Phase 6 tenant portal). Composes the three billing
 * widgets. Lazy-loaded from the router so the Razorpay flow ships independently of the rest of the
 * tenant portal still to come in Phase 6.
 */
@Component({
  selector: 'pulseone-billing-page',
  standalone: true,
  imports: [BillingPlansComponent, BillingHistoryComponent, SubscriptionStatusComponent],
  template: `
    <div class="flex flex-col gap-8">
      <pulseone-subscription-status />
      <pulseone-billing-plans />
      <pulseone-billing-history />
    </div>
  `,
})
export class BillingPageComponent {}
