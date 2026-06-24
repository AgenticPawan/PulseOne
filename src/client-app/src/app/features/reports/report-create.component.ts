import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { httpResource } from '@angular/common/http';
import { ReportTypeDescriptor } from '../../core/models/tenant-models';
import { TenantApiService } from '../../core/services/tenant-api.service';
import { ModalDialogComponent } from '../../shared/modal-dialog.component';

/**
 * Report creation dialog (prompt 01): a report-type selector plus a parameters form rendered
 * dynamically from the selected type's descriptor. On submit it POSTs to /api/v1/reports and emits
 * the returned `reportId`; the parent grid then tracks completion over the SignalR ReportHub.
 *
 * The available report types are read with `httpResource` (reactive). The dynamic parameter values
 * are local signal state. Focus trapping/restore is handled by ModalDialogComponent.
 */
@Component({
  selector: 'po-report-create',
  standalone: true,
  imports: [FormsModule, ModalDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <po-modal-dialog heading="Generate report" titleId="report-create-title" (closed)="cancelled.emit()">
      @if (types.isLoading()) {
        <p class="text-sm text-slate-500" role="status">Loading report types…</p>
      } @else if (types.error()) {
        <p class="text-sm text-red-600" role="alert">Could not load report types.</p>
      } @else {
        <form (ngSubmit)="submit()" class="space-y-4">
          <div>
            <label for="report-type" class="block text-sm font-medium text-slate-700">
              Report type
            </label>
            <select
              id="report-type"
              [ngModel]="selectedType()"
              (ngModelChange)="onTypeChange($event)"
              name="reportType"
              class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            >
              <option value="" disabled>Select a type…</option>
              @for (t of types.value() ?? []; track t.key) {
                <option [value]="t.key">{{ t.label }}</option>
              }
            </select>
          </div>

          @for (p of selectedDescriptor()?.parameters ?? []; track p.key) {
            <div>
              <label [for]="'param-' + p.key" class="block text-sm font-medium text-slate-700">
                {{ p.label }}@if (p.required) {<span class="text-red-500" aria-hidden="true"> *</span>}
              </label>
              @if (p.kind === 'select') {
                <select
                  [id]="'param-' + p.key"
                  [ngModel]="paramValue(p.key)"
                  (ngModelChange)="setParam(p.key, $event)"
                  [name]="'param-' + p.key"
                  [required]="p.required"
                  class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none"
                >
                  <option value="" disabled>Select…</option>
                  @for (opt of p.options ?? []; track opt) {
                    <option [value]="opt">{{ opt }}</option>
                  }
                </select>
              } @else {
                <input
                  [id]="'param-' + p.key"
                  [type]="p.kind"
                  [ngModel]="paramValue(p.key)"
                  (ngModelChange)="setParam(p.key, $event)"
                  [name]="'param-' + p.key"
                  [required]="p.required"
                  class="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                />
              }
            </div>
          }

          @if (submitError()) {
            <p class="text-sm text-red-600" role="alert">{{ submitError() }}</p>
          }

          <div class="flex justify-end gap-2 pt-2">
            <button
              type="button"
              (click)="cancelled.emit()"
              class="rounded-md border border-slate-300 px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100"
            >
              Cancel
            </button>
            <button
              type="submit"
              [disabled]="!canSubmit() || submitting()"
              class="rounded-md bg-[var(--tenant-primary,#2563eb)] px-3 py-2 text-sm font-medium text-white hover:opacity-90 disabled:opacity-50"
            >
              {{ submitting() ? 'Submitting…' : 'Generate' }}
            </button>
          </div>
        </form>
      }
    </po-modal-dialog>
  `,
})
export class ReportCreateComponent {
  private readonly api = inject(TenantApiService);

  /** Emits the new report id once the server has accepted the create request. */
  readonly created = output<string>();
  readonly cancelled = output<void>();

  protected readonly types = httpResource<readonly ReportTypeDescriptor[]>(() => ({
    url: '/api/v1/reports/types',
  }));

  protected readonly selectedType = signal('');
  private readonly params = signal<Record<string, string>>({});
  protected readonly submitting = signal(false);
  protected readonly submitError = signal<string | null>(null);

  protected readonly selectedDescriptor = computed(() =>
    (this.types.value() ?? []).find((t) => t.key === this.selectedType()),
  );

  protected readonly canSubmit = computed(() => {
    const desc = this.selectedDescriptor();
    if (!desc) {
      return false;
    }
    const values = this.params();
    return desc.parameters
      .filter((p) => p.required)
      .every((p) => (values[p.key] ?? '').trim().length > 0);
  });

  protected paramValue(key: string): string {
    return this.params()[key] ?? '';
  }

  protected onTypeChange(key: string): void {
    this.selectedType.set(key);
    this.params.set({}); // reset parameter values when switching types
    this.submitError.set(null);
  }

  protected setParam(key: string, value: string): void {
    this.params.update((p) => ({ ...p, [key]: value }));
  }

  protected async submit(): Promise<void> {
    if (!this.canSubmit() || this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.submitError.set(null);
    try {
      const res = await this.api.createReport({
        reportType: this.selectedType(),
        parameters: this.params(),
      });
      this.created.emit(res.reportId);
    } catch {
      this.submitError.set('Failed to queue the report. Please try again.');
    } finally {
      this.submitting.set(false);
    }
  }
}
