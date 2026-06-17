import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'po-root',
  standalone: true,
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="p-4 text-xl font-semibold">{{ title() }}</header>
    <main class="p-4">
      <router-outlet />
    </main>
  `,
})
export class App {
  protected readonly title = signal('PulseOne — Host Admin Portal');
}
