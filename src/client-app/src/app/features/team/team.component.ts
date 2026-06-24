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
  PermissionDescriptor,
  TeamMember,
} from '../../core/models/tenant-models';
import { TenantApiService } from '../../core/services/tenant-api.service';
import { StatusBadgeComponent } from '../../shared/status-badge.component';
import { ModalDialogComponent } from '../../shared/modal-dialog.component';

/**
 * Team management (prompt 01): lists tenant users with roles/status, invites users (email sent by a
 * background job), edits PBAC permissions grouped by category, and deactivates/reactivates users.
 *
 * Reads (member list + permission catalogue) use `httpResource`; writes go through TenantApiService,
 * whose mutationVersion signal the member-list resource reads so the list refetches after a change.
 * The invite and edit-permissions flows use the focus-trapping ModalDialogComponent.
 */
@Component({
  selector: 'po-team',
  standalone: true,
  imports: [DatePipe, FormsModule, StatusBadgeComponent, ModalDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-labelledby="team-heading" class="space-y-4">
      <div class="flex items-center justify-between">
        <h2 id="team-heading" class="text-lg font-semibold text-slate-900">Team</h2>
        <button
          type="button"
          (click)="openInvite()"
          class="rounded-md bg-[var(--tenant-primary,#2563eb)] px-3 py-2 text-sm font-medium text-white hover:opacity-90"
        >
          Invite user
        </button>
      </div>

      <div class="overflow-hidden rounded-lg border border-slate-200 bg-white">
        <div aria-live="polite">
          @if (members.isLoading()) {
            <p class="p-6 text-sm text-slate-500" role="status">Loading team…</p>
          } @else if (members.error()) {
            <p class="p-6 text-sm text-red-600" role="alert">Could not load team members.</p>
          } @else {
            <table class="min-w-full divide-y divide-slate-200 text-sm">
              <caption class="sr-only">Tenant users</caption>
              <thead class="bg-slate-50">
                <tr>
                  <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">User</th>
                  <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Role</th>
                  <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Status</th>
                  <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">
                    Last login
                  </th>
                  <th scope="col" class="px-4 py-2 text-left font-medium text-slate-500">Actions</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-100">
                @for (m of members.value() ?? []; track m.userId) {
                  <tr class="hover:bg-slate-50">
                    <th scope="row" class="px-4 py-2 text-left">
                      <span class="font-medium text-slate-800">{{ m.displayName }}</span>
                      <span class="block text-xs text-slate-500">{{ m.email }}</span>
                    </th>
                    <td class="px-4 py-2 text-slate-600">{{ m.role }}</td>
                    <td class="px-4 py-2"><po-status-badge [status]="m.status" /></td>
                    <td class="px-4 py-2 text-slate-500">
                      {{ m.lastLoginUtc ? (m.lastLoginUtc | date: 'short') : 'Never' }}
                    </td>
                    <td class="px-4 py-2">
                      <div class="flex items-center gap-3">
                        <button
                          type="button"
                          (click)="openPermissions(m)"
                          [disabled]="busy() === m.userId"
                          class="text-indigo-600 hover:underline disabled:opacity-40"
                          [attr.aria-label]="'Edit permissions for ' + m.displayName"
                        >
                          Permissions
                        </button>
                        @if (m.status === 'Deactivated') {
                          <button
                            type="button"
                            (click)="reactivate(m)"
                            [disabled]="busy() === m.userId"
                            class="text-emerald-600 hover:underline disabled:opacity-40"
                          >
                            Reactivate
                          </button>
                        } @else {
                          <button
                            type="button"
                            (click)="deactivate(m)"
                            [disabled]="busy() === m.userId"
                            class="text-amber-600 hover:underline disabled:opacity-40"
                          >
                            Deactivate
                          </button>
                        }
                      </div>
                    </td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="5" class="px-4 py-6 text-center text-slate-500">No team members.</td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </div>
      </div>
    </section>

    <!-- Invite dialog -->
    @if (showInvite()) {
      <po-modal-dialog heading="Invite user" titleId="invite-title" (closed)="showInvite.set(false)">
        <form (ngSubmit)="sendInvite()" class="space-y-4">
          <div>
            <label for="invite-email" class="block text-sm font-medium text-slate-700">Email</label>
            <input
              id="invite-email"
              type="email"
              required
              [ngModel]="inviteEmail()"
              (ngModelChange)="inviteEmail.set($event)"
              name="email"
              class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <div>
            <label for="invite-role" class="block text-sm font-medium text-slate-700">Role</label>
            <select
              id="invite-role"
              [ngModel]="inviteRole()"
              (ngModelChange)="inviteRole.set($event)"
              name="role"
              class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none"
            >
              <option value="Member">Member</option>
              <option value="Manager">Manager</option>
              <option value="Admin">Admin</option>
            </select>
          </div>
          <div class="flex justify-end gap-2 pt-2">
            <button
              type="button"
              (click)="showInvite.set(false)"
              class="rounded-md border border-slate-300 px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100"
            >
              Cancel
            </button>
            <button
              type="submit"
              [disabled]="!inviteValid() || busy() === 'invite'"
              class="rounded-md bg-[var(--tenant-primary,#2563eb)] px-3 py-2 text-sm font-medium text-white hover:opacity-90 disabled:opacity-50"
            >
              Send invite
            </button>
          </div>
        </form>
      </po-modal-dialog>
    }

    <!-- Permissions dialog -->
    @if (editing(); as member) {
      <po-modal-dialog
        [heading]="'Permissions — ' + member.displayName"
        titleId="permissions-title"
        (closed)="editing.set(null)"
      >
        @if (permissions.isLoading()) {
          <p class="text-sm text-slate-500" role="status">Loading permissions…</p>
        } @else if (permissions.error()) {
          <p class="text-sm text-red-600" role="alert">Could not load the permission catalogue.</p>
        } @else {
          <form (ngSubmit)="savePermissions(member)" class="space-y-4">
            @for (group of groupedPermissions(); track group.category) {
              <fieldset class="space-y-2">
                <legend class="text-sm font-semibold text-slate-700">{{ group.category }}</legend>
                @for (perm of group.items; track perm.key) {
                  <label class="flex items-center gap-2 text-sm text-slate-700">
                    <input
                      type="checkbox"
                      [checked]="selectedPermissions().has(perm.key)"
                      (change)="togglePermission(perm.key)"
                      class="rounded border-slate-300 text-indigo-600 focus:ring-indigo-500"
                    />
                    {{ perm.label }}
                  </label>
                }
              </fieldset>
            }
            <div class="flex justify-end gap-2 pt-2">
              <button
                type="button"
                (click)="editing.set(null)"
                class="rounded-md border border-slate-300 px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100"
              >
                Cancel
              </button>
              <button
                type="submit"
                [disabled]="busy() === member.userId"
                class="rounded-md bg-[var(--tenant-primary,#2563eb)] px-3 py-2 text-sm font-medium text-white hover:opacity-90 disabled:opacity-50"
              >
                Save
              </button>
            </div>
          </form>
        }
      </po-modal-dialog>
    }
  `,
})
export class TeamComponent {
  private readonly api = inject(TenantApiService);

  protected readonly members = httpResource<readonly TeamMember[]>(() => {
    this.api.mutationVersion();
    return { url: '/api/v1/team' };
  });

  protected readonly permissions = httpResource<readonly PermissionDescriptor[]>(() => ({
    url: '/api/v1/team/permissions',
  }));

  protected readonly busy = signal<string | null>(null);

  // Invite dialog state.
  protected readonly showInvite = signal(false);
  protected readonly inviteEmail = signal('');
  protected readonly inviteRole = signal('Member');
  protected readonly inviteValid = computed(() => /\S+@\S+\.\S+/.test(this.inviteEmail()));

  // Permissions dialog state.
  protected readonly editing = signal<TeamMember | null>(null);
  protected readonly selectedPermissions = signal<Set<string>>(new Set());

  protected readonly groupedPermissions = computed(() => {
    const catalogue = this.permissions.value() ?? [];
    const byCategory = new Map<string, PermissionDescriptor[]>();
    for (const p of catalogue) {
      const list = byCategory.get(p.category) ?? [];
      list.push(p);
      byCategory.set(p.category, list);
    }
    return Array.from(byCategory, ([category, items]) => ({ category, items }));
  });

  protected openInvite(): void {
    this.inviteEmail.set('');
    this.inviteRole.set('Member');
    this.showInvite.set(true);
  }

  protected async sendInvite(): Promise<void> {
    if (!this.inviteValid()) {
      return;
    }
    this.busy.set('invite');
    try {
      await this.api.inviteUser({ email: this.inviteEmail(), role: this.inviteRole() });
      this.showInvite.set(false);
    } finally {
      this.busy.set(null);
    }
  }

  protected openPermissions(member: TeamMember): void {
    this.selectedPermissions.set(new Set(member.permissions));
    this.editing.set(member);
  }

  protected togglePermission(key: string): void {
    this.selectedPermissions.update((set) => {
      const next = new Set(set);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  }

  protected async savePermissions(member: TeamMember): Promise<void> {
    this.busy.set(member.userId);
    try {
      await this.api.updatePermissions(member.userId, Array.from(this.selectedPermissions()));
      this.editing.set(null);
    } finally {
      this.busy.set(null);
    }
  }

  protected async deactivate(member: TeamMember): Promise<void> {
    this.busy.set(member.userId);
    try {
      await this.api.deactivateUser(member.userId);
    } finally {
      this.busy.set(null);
    }
  }

  protected async reactivate(member: TeamMember): Promise<void> {
    this.busy.set(member.userId);
    try {
      await this.api.reactivateUser(member.userId);
    } finally {
      this.busy.set(null);
    }
  }
}
