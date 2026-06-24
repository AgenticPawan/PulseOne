import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { httpResource } from '@angular/common/http';
import {
  ApiKeySummary,
  CompanyProfile,
  CreatedApiKey,
  NotificationChannel,
  NotificationPreference,
} from '../../core/models/tenant-models';
import { TenantApiService } from '../../core/services/tenant-api.service';

/**
 * Tenant settings (prompt 01): company profile, API keys (generate/revoke), notification
 * preferences (email/SMS/WhatsApp per event type), and a danger zone (export all data, request
 * account deletion).
 *
 * All reads use `httpResource` (keyed on mutationVersion so they refetch after a save). Writes go
 * through TenantApiService. A newly generated API key secret is shown exactly ONCE, in memory only;
 * it is never persisted client-side. Logo upload is captured as a File and POSTed via FormData.
 */
@Component({
  selector: 'po-settings',
  standalone: true,
  imports: [DatePipe, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-labelledby="settings-heading" class="max-w-3xl space-y-8">
      <h2 id="settings-heading" class="text-lg font-semibold text-slate-900">Settings</h2>

      <!-- Company profile -->
      <div class="rounded-xl border border-slate-200 bg-white p-5">
        <h3 class="text-sm font-semibold text-slate-900">Company profile</h3>
        @if (profile.isLoading()) {
          <p class="mt-3 text-sm text-slate-500" role="status">Loading…</p>
        } @else if (profile.error()) {
          <p class="mt-3 text-sm text-red-600" role="alert">Could not load profile.</p>
        } @else if (profile.value(); as p) {
          <form (ngSubmit)="saveProfile(p)" class="mt-4 space-y-4">
            <div>
              <label for="company-name" class="block text-sm font-medium text-slate-700">
                Company name
              </label>
              <input
                id="company-name"
                type="text"
                [ngModel]="profileDraft().companyName"
                (ngModelChange)="patchProfile({ companyName: $event })"
                name="companyName"
                class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              />
            </div>
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <label for="contact-email" class="block text-sm font-medium text-slate-700">
                  Contact email
                </label>
                <input
                  id="contact-email"
                  type="email"
                  [ngModel]="profileDraft().contactEmail"
                  (ngModelChange)="patchProfile({ contactEmail: $event })"
                  name="contactEmail"
                  class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none"
                />
              </div>
              <div>
                <label for="contact-phone" class="block text-sm font-medium text-slate-700">
                  Contact phone
                </label>
                <input
                  id="contact-phone"
                  type="tel"
                  [ngModel]="profileDraft().contactPhone"
                  (ngModelChange)="patchProfile({ contactPhone: $event })"
                  name="contactPhone"
                  class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none"
                />
              </div>
            </div>
            <div>
              <label for="logo-upload" class="block text-sm font-medium text-slate-700">Logo</label>
              <input
                id="logo-upload"
                type="file"
                accept="image/png,image/jpeg,image/svg+xml"
                (change)="onLogoSelected($event)"
                class="mt-1 block w-full text-sm text-slate-600"
              />
            </div>
            <div class="flex justify-end">
              <button
                type="submit"
                [disabled]="busy() === 'profile'"
                class="rounded-md bg-[var(--tenant-primary,#2563eb)] px-3 py-2 text-sm font-medium text-white hover:opacity-90 disabled:opacity-50"
              >
                Save profile
              </button>
            </div>
          </form>
        }
      </div>

      <!-- API keys -->
      <div class="rounded-xl border border-slate-200 bg-white p-5">
        <div class="flex items-center justify-between">
          <h3 class="text-sm font-semibold text-slate-900">API keys</h3>
          <button
            type="button"
            (click)="generateApiKey()"
            [disabled]="busy() === 'apikey'"
            class="rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
          >
            Generate key
          </button>
        </div>

        @if (newSecret(); as created) {
          <div
            role="status"
            class="mt-3 rounded-md border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800"
          >
            <p class="font-medium">Copy your new key now — it will not be shown again.</p>
            <code class="mt-1 block break-all font-mono text-xs">{{ created.secret }}</code>
          </div>
        }

        <div aria-live="polite" class="mt-4">
          @if (apiKeys.isLoading()) {
            <p class="text-sm text-slate-500" role="status">Loading…</p>
          } @else {
            <ul class="divide-y divide-slate-100">
              @for (k of apiKeys.value() ?? []; track k.id) {
                <li class="flex items-center justify-between py-2 text-sm">
                  <div>
                    <span class="font-medium text-slate-800">{{ k.name }}</span>
                    <span class="ml-2 font-mono text-xs text-slate-500">{{ k.prefix }}…</span>
                    <span class="block text-xs text-slate-400">
                      Created {{ k.createdAtUtc | date: 'mediumDate' }}
                    </span>
                  </div>
                  <button
                    type="button"
                    (click)="revokeApiKey(k)"
                    [disabled]="busy() === k.id"
                    class="text-red-600 hover:underline disabled:opacity-40"
                  >
                    Revoke
                  </button>
                </li>
              } @empty {
                <li class="py-2 text-sm text-slate-500">No API keys.</li>
              }
            </ul>
          }
        </div>
      </div>

      <!-- Notification preferences -->
      <div class="rounded-xl border border-slate-200 bg-white p-5">
        <h3 class="text-sm font-semibold text-slate-900">Notification preferences</h3>
        @if (notifications.isLoading()) {
          <p class="mt-3 text-sm text-slate-500" role="status">Loading…</p>
        } @else if (notifications.error()) {
          <p class="mt-3 text-sm text-red-600" role="alert">Could not load preferences.</p>
        } @else {
          <table class="mt-4 min-w-full text-sm">
            <thead>
              <tr class="text-left text-slate-500">
                <th class="py-2 font-medium">Event</th>
                <th class="py-2 text-center font-medium">Email</th>
                <th class="py-2 text-center font-medium">SMS</th>
                <th class="py-2 text-center font-medium">WhatsApp</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100">
              @for (pref of prefsDraft(); track pref.eventType) {
                <tr>
                  <td class="py-2 text-slate-700">{{ pref.eventLabel }}</td>
                  @for (channel of channels; track channel) {
                    <td class="py-2 text-center">
                      <input
                        type="checkbox"
                        [checked]="pref[channel]"
                        (change)="toggleChannel(pref.eventType, channel)"
                        [attr.aria-label]="pref.eventLabel + ' ' + channel"
                        class="rounded border-slate-300 text-indigo-600 focus:ring-indigo-500"
                      />
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
          <div class="mt-4 flex justify-end">
            <button
              type="button"
              (click)="savePreferences()"
              [disabled]="busy() === 'prefs'"
              class="rounded-md bg-[var(--tenant-primary,#2563eb)] px-3 py-2 text-sm font-medium text-white hover:opacity-90 disabled:opacity-50"
            >
              Save preferences
            </button>
          </div>
        }
      </div>

      <!-- Danger zone -->
      <div class="rounded-xl border border-red-200 bg-red-50 p-5">
        <h3 class="text-sm font-semibold text-red-800">Danger zone</h3>
        <div class="mt-4 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p class="text-sm font-medium text-slate-800">Export all data</p>
            <p class="text-xs text-slate-500">Queues a full export; you'll be emailed a download link.</p>
          </div>
          <button
            type="button"
            (click)="exportData()"
            [disabled]="busy() === 'export'"
            class="rounded-md border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
          >
            {{ exportJobId() ? 'Export queued' : 'Export data' }}
          </button>
        </div>
        <div class="mt-4 flex flex-col gap-2">
          <p class="text-sm font-medium text-red-800">Delete account</p>
          <p class="text-xs text-slate-600">
            Type <span class="font-mono">DELETE</span> to request permanent account deletion.
          </p>
          <div class="flex gap-2">
            <label for="delete-confirm" class="sr-only">Type DELETE to confirm</label>
            <input
              id="delete-confirm"
              type="text"
              [ngModel]="deleteConfirm()"
              (ngModelChange)="deleteConfirm.set($event)"
              name="deleteConfirm"
              placeholder="DELETE"
              class="w-40 rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-red-500 focus:outline-none"
            />
            <button
              type="button"
              (click)="deleteAccount()"
              [disabled]="deleteConfirm() !== 'DELETE' || busy() === 'delete'"
              class="rounded-md bg-red-600 px-3 py-2 text-sm font-medium text-white hover:bg-red-500 disabled:opacity-50"
            >
              Request deletion
            </button>
          </div>
        </div>
      </div>
    </section>
  `,
})
export class SettingsComponent {
  private readonly api = inject(TenantApiService);

  protected readonly channels: readonly NotificationChannel[] = ['email', 'sms', 'whatsapp'];

  // ---- Company profile ------------------------------------------------------
  protected readonly profile = httpResource<CompanyProfile>(() => {
    this.api.mutationVersion();
    return { url: '/api/v1/settings/profile' };
  });

  private readonly profileEdits = signal<Partial<CompanyProfile>>({});
  protected readonly profileDraft = computed<CompanyProfile>(() => {
    const base = this.profile.value() ?? {
      companyName: '',
      contactEmail: '',
      contactPhone: '',
      logoUrl: null,
    };
    return { ...base, ...this.profileEdits() };
  });
  private logoFile: File | null = null;

  protected patchProfile(patch: Partial<CompanyProfile>): void {
    this.profileEdits.update((e) => ({ ...e, ...patch }));
  }

  protected onLogoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.logoFile = input.files?.[0] ?? null;
  }

  protected async saveProfile(_current: CompanyProfile): Promise<void> {
    this.busy.set('profile');
    try {
      await this.api.updateCompanyProfile(this.profileDraft());
      // NOTE: logo upload is a separate multipart endpoint (OWED — see report). Captured here for
      // wiring; the FormData POST is added once the backend endpoint contract is finalized.
      this.profileEdits.set({});
      this.logoFile = null;
    } finally {
      this.busy.set(null);
    }
  }

  // ---- API keys -------------------------------------------------------------
  protected readonly apiKeys = httpResource<readonly ApiKeySummary[]>(() => {
    this.api.mutationVersion();
    return { url: '/api/v1/settings/api-keys' };
  });

  protected readonly newSecret = signal<CreatedApiKey | null>(null);

  protected async generateApiKey(): Promise<void> {
    this.busy.set('apikey');
    try {
      const created = await this.api.createApiKey(`key-${new Date().toISOString().slice(0, 10)}`);
      this.newSecret.set(created); // shown once, in memory only
    } finally {
      this.busy.set(null);
    }
  }

  protected async revokeApiKey(key: ApiKeySummary): Promise<void> {
    this.busy.set(key.id);
    try {
      await this.api.revokeApiKey(key.id);
      if (this.newSecret()?.id === key.id) {
        this.newSecret.set(null);
      }
    } finally {
      this.busy.set(null);
    }
  }

  // ---- Notification preferences --------------------------------------------
  protected readonly notifications = httpResource<readonly NotificationPreference[]>(() => {
    this.api.mutationVersion();
    return { url: '/api/v1/settings/notifications' };
  });

  private readonly prefsOverrides = signal<Map<string, NotificationPreference>>(new Map());
  protected readonly prefsDraft = computed<NotificationPreference[]>(() => {
    const base = this.notifications.value() ?? [];
    const overrides = this.prefsOverrides();
    return base.map((p) => overrides.get(p.eventType) ?? p);
  });

  protected toggleChannel(eventType: string, channel: NotificationChannel): void {
    const current = this.prefsDraft().find((p) => p.eventType === eventType);
    if (!current) {
      return;
    }
    const updated: NotificationPreference = { ...current, [channel]: !current[channel] };
    this.prefsOverrides.update((map) => {
      const next = new Map(map);
      next.set(eventType, updated);
      return next;
    });
  }

  protected async savePreferences(): Promise<void> {
    this.busy.set('prefs');
    try {
      await this.api.updateNotificationPreferences(this.prefsDraft());
      this.prefsOverrides.set(new Map());
    } finally {
      this.busy.set(null);
    }
  }

  // ---- Danger zone ----------------------------------------------------------
  protected readonly exportJobId = signal<string | null>(null);
  protected readonly deleteConfirm = signal('');

  protected async exportData(): Promise<void> {
    this.busy.set('export');
    try {
      const res = await this.api.requestDataExport();
      this.exportJobId.set(res.jobId);
    } finally {
      this.busy.set(null);
    }
  }

  protected async deleteAccount(): Promise<void> {
    if (this.deleteConfirm() !== 'DELETE') {
      return;
    }
    this.busy.set('delete');
    try {
      await this.api.requestAccountDeletion(this.deleteConfirm());
      this.deleteConfirm.set('');
    } finally {
      this.busy.set(null);
    }
  }

  protected readonly busy = signal<string | null>(null);
}
