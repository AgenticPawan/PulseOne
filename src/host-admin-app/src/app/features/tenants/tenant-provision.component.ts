import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HostApiService } from '../../core/services/host-api.service';
import { ProvisionTenantRequest } from '../../core/models/host-models';

/**
 * Onboards a new tenant. On submit POSTs to /api/v1/host/tenants (HostOperatorsOnly), which
 * transactionally writes the Tenant Catalog shard entry AND enqueues the welcome-email background
 * job (blueprint: compute-heavy / side-effecting work is queued, not done inline).
 *
 * The Tenant ID is slugified live from the company name but stays editable.
 */
@Component({
  selector: 'po-tenant-provision',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-labelledby="provision-heading" class="mx-auto max-w-2xl space-y-6">
      <nav class="text-sm" aria-label="Breadcrumb">
        <a routerLink="/tenants" class="text-indigo-600 hover:underline">Tenants</a>
        <span class="px-1 text-slate-400" aria-hidden="true">/</span>
        <span class="text-slate-600">Provision</span>
      </nav>

      <h2 id="provision-heading" class="text-lg font-semibold">Provision a new tenant</h2>

      <form
        [formGroup]="form"
        (ngSubmit)="submit()"
        class="space-y-5 rounded-lg border border-slate-200 bg-white p-6"
        novalidate
      >
        <div>
          <label for="companyName" class="block text-sm font-medium text-slate-700">Company name</label>
          <input
            id="companyName"
            type="text"
            formControlName="companyName"
            (input)="onCompanyNameInput($any($event.target).value)"
            class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            [attr.aria-invalid]="isInvalid('companyName')"
          />
          @if (isInvalid('companyName')) {
            <p class="mt-1 text-xs text-red-600" role="alert">Company name is required.</p>
          }
        </div>

        <div>
          <label for="tenantId" class="block text-sm font-medium text-slate-700">Tenant ID (slug)</label>
          <input
            id="tenantId"
            type="text"
            formControlName="tenantId"
            class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 font-mono text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            [attr.aria-invalid]="isInvalid('tenantId')"
            aria-describedby="tenantId-help"
          />
          <p id="tenantId-help" class="mt-1 text-xs text-slate-500">
            Lowercase letters, numbers and hyphens only. Derived from the company name; edit if needed.
          </p>
          @if (isInvalid('tenantId')) {
            <p class="mt-1 text-xs text-red-600" role="alert">
              A valid slug is required (e.g. acme-corp).
            </p>
          }
        </div>

        <div class="grid grid-cols-1 gap-5 md:grid-cols-2">
          <div>
            <label for="planTier" class="block text-sm font-medium text-slate-700">Plan tier</label>
            <select
              id="planTier"
              formControlName="planTier"
              class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none"
            >
              <option value="Starter">Starter</option>
              <option value="Growth">Growth</option>
              <option value="Enterprise">Enterprise</option>
            </select>
          </div>

          <div>
            <label for="assignedShard" class="block text-sm font-medium text-slate-700">Assigned shard</label>
            <select
              id="assignedShard"
              formControlName="assignedShard"
              class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none"
            >
              <option value="shard-01">shard-01</option>
              <option value="shard-02">shard-02</option>
              <option value="shard-03">shard-03</option>
            </select>
          </div>
        </div>

        <div>
          <label for="adminEmail" class="block text-sm font-medium text-slate-700">Admin email</label>
          <input
            id="adminEmail"
            type="email"
            formControlName="adminEmail"
            class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            [attr.aria-invalid]="isInvalid('adminEmail')"
          />
          @if (isInvalid('adminEmail')) {
            <p class="mt-1 text-xs text-red-600" role="alert">A valid admin email is required.</p>
          }
        </div>

        @if (submitError()) {
          <p class="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700" role="alert">
            {{ submitError() }}
          </p>
        }

        <div class="flex items-center justify-end gap-3">
          <a routerLink="/tenants" class="rounded-md border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100">
            Cancel
          </a>
          <button
            type="submit"
            [disabled]="form.invalid || submitting()"
            class="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-500 disabled:opacity-50"
          >
            {{ submitting() ? 'Provisioning…' : 'Provision tenant' }}
          </button>
        </div>
      </form>
    </section>
  `,
})
export class TenantProvisionComponent {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(HostApiService);
  private readonly router = inject(Router);

  protected readonly submitting = signal(false);
  protected readonly submitError = signal<string | null>(null);
  private tenantIdEdited = false;

  protected readonly form = this.fb.nonNullable.group({
    companyName: ['', [Validators.required, Validators.maxLength(200)]],
    tenantId: ['', [Validators.required, Validators.pattern(/^[a-z0-9]+(-[a-z0-9]+)*$/)]],
    planTier: ['Starter', [Validators.required]],
    assignedShard: ['shard-01', [Validators.required]],
    adminEmail: ['', [Validators.required, Validators.email]],
  });

  protected isInvalid(control: string): boolean {
    const c = this.form.get(control);
    return !!c && c.invalid && (c.dirty || c.touched);
  }

  protected onCompanyNameInput(value: string): void {
    // Keep tenantId in sync with the company name until the operator edits the slug directly.
    const slugControl = this.form.controls.tenantId;
    if (!this.tenantIdEdited && !slugControl.dirty) {
      slugControl.setValue(this.slugify(value));
    } else {
      this.tenantIdEdited = true;
    }
  }

  private slugify(input: string): string {
    return input
      .toLowerCase()
      .trim()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 63);
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.submitError.set(null);
    const request: ProvisionTenantRequest = this.form.getRawValue();
    try {
      await this.api.provisionTenant(request);
      await this.router.navigate(['/tenants', request.tenantId]);
    } catch {
      this.submitError.set(
        'Provisioning failed. The tenant ID may already exist or the request was rejected by the host policy.',
      );
    } finally {
      this.submitting.set(false);
    }
  }
}
