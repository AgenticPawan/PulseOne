import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Access-denied surface for authenticated principals that are not platform operators. */
@Component({
  selector: 'po-forbidden',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-md rounded-lg border border-slate-200 bg-white p-8 text-center">
      <h2 class="text-lg font-semibold text-slate-900">Access denied</h2>
      <p class="mt-2 text-sm text-slate-600">
        Your account is signed in but lacks platform-operator access. The host console is
        restricted to PulseOne operators. This boundary is enforced by the API.
      </p>
    </div>
  `,
})
export class ForbiddenComponent {}
