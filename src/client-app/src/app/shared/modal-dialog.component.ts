import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';

/**
 * Accessible modal dialog shell used by the report-create and team-invite flows (prompt 01
 * constraint: "Focus management in dialogs: trap focus, restore on close").
 *
 * - role="dialog" + aria-modal + aria-labelledby wire it to assistive tech.
 * - On open it captures the previously-focused element and moves focus to the first focusable
 *   control inside the panel; on close it restores focus to the original element.
 * - Tab/Shift+Tab are trapped within the panel; Escape requests close.
 *
 * Content is projected, so each feature owns its own form. Styling is Tailwind utility classes
 * only. No `innerHTML`/`bypassSecurityTrust*` is used anywhere.
 */
@Component({
  selector: 'po-modal-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/50 p-4"
      (click)="onBackdrop($event)"
    >
      <div
        #panel
        role="dialog"
        aria-modal="true"
        [attr.aria-labelledby]="titleId()"
        (keydown)="onKeydown($event)"
        class="w-full max-w-lg rounded-xl bg-white shadow-xl ring-1 ring-slate-200"
      >
        <div class="flex items-center justify-between border-b border-slate-200 px-5 py-4">
          <h2 [id]="titleId()" class="text-base font-semibold text-slate-900">{{ heading() }}</h2>
          <button
            type="button"
            (click)="closed.emit()"
            class="rounded-md p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
            aria-label="Close dialog"
          >
            <span aria-hidden="true" class="text-lg leading-none">×</span>
          </button>
        </div>
        <div class="px-5 py-4">
          <ng-content />
        </div>
      </div>
    </div>
  `,
})
export class ModalDialogComponent {
  readonly heading = input.required<string>();
  /** Stable id used to associate the heading with the dialog for screen readers. */
  readonly titleId = input<string>('po-dialog-title');

  readonly closed = output<void>();

  private readonly panel = viewChild.required<ElementRef<HTMLElement>>('panel');
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  private previouslyFocused: HTMLElement | null = null;

  constructor() {
    // Capture the trigger element so focus can be restored when the dialog is destroyed.
    this.previouslyFocused = (document.activeElement as HTMLElement | null) ?? null;
    // Move focus into the dialog after it renders.
    queueMicrotask(() => this.focusFirst());
  }

  ngOnDestroy(): void {
    this.previouslyFocused?.focus?.();
  }

  protected onBackdrop(event: MouseEvent): void {
    // Only the backdrop itself (not bubbled clicks from the panel) closes the dialog.
    if (event.target === event.currentTarget) {
      this.closed.emit();
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.closed.emit();
      return;
    }
    if (event.key !== 'Tab') {
      return;
    }
    const focusable = this.focusableElements();
    if (focusable.length === 0) {
      return;
    }
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement as HTMLElement | null;

    if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  private focusFirst(): void {
    const focusable = this.focusableElements();
    (focusable[0] ?? this.panel().nativeElement).focus();
  }

  private focusableElements(): HTMLElement[] {
    const selector =
      'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';
    return Array.from(this.panel().nativeElement.querySelectorAll<HTMLElement>(selector)).filter(
      (el) => el.offsetParent !== null || el === document.activeElement,
    );
  }
}
