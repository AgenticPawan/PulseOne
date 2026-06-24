import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Small presentational pill that maps a status string to an accessible, colour-coded badge.
 * Pure utility-class styling (Tailwind) per the portal conventions — no component-scoped CSS.
 */
@Component({
  selector: 'po-status-badge',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
      [class]="classes()"
    >
      <span class="h-1.5 w-1.5 rounded-full" [class]="dotClass()" aria-hidden="true"></span>
      {{ label() }}
    </span>
  `,
})
export class StatusBadgeComponent {
  /** Raw status value (e.g. report/team-member status). */
  readonly status = input.required<string>();

  protected readonly tone = computed<'good' | 'warn' | 'bad' | 'neutral'>(() => {
    const s = this.status().toLowerCase();
    if (['active', 'healthy', 'completed', 'succeeded'].includes(s)) return 'good';
    if (['provisioning', 'processing', 'queued', 'invited', 'pending'].includes(s)) return 'warn';
    if (['suspended', 'deactivated', 'cancelled', 'failed'].includes(s)) return 'bad';
    return 'neutral';
  });

  protected readonly label = computed(() =>
    this.status().replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase()),
  );

  protected readonly classes = computed(() => {
    switch (this.tone()) {
      case 'good':
        return 'bg-emerald-50 text-emerald-700 ring-emerald-600/20';
      case 'warn':
        return 'bg-amber-50 text-amber-700 ring-amber-600/20';
      case 'bad':
        return 'bg-red-50 text-red-700 ring-red-600/20';
      default:
        return 'bg-slate-100 text-slate-700 ring-slate-500/20';
    }
  });

  protected readonly dotClass = computed(() => {
    switch (this.tone()) {
      case 'good':
        return 'bg-emerald-500';
      case 'warn':
        return 'bg-amber-500';
      case 'bad':
        return 'bg-red-500';
      default:
        return 'bg-slate-400';
    }
  });
}
