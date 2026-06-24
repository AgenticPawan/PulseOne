import { Injectable, inject, signal } from '@angular/core';
import { Subject } from 'rxjs';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { MsalService } from '@azure/msal-angular';
import { getAuthConfig } from '../auth/auth.config';

/** Shape pushed by the worker on the `ReportCompleted` server event (mirrors ReportNotification). */
export interface ReportCompletedEvent {
  readonly reportId: string;
  readonly downloadUrl: string;
}

/**
 * SignalR client for the producer `ReportHub` (prompt 01 / blueprint §6.5; server hub at
 * `/hubs/reports`). The hub joins each connection to the group named after its OWN validated
 * `tenant_id` claim server-side — a client cannot choose another tenant's group — so this service
 * only needs to start the connection and relay the `ReportCompleted` event.
 *
 * Token: the access token is fetched via MSAL `acquireTokenSilent` so the connection (which is NOT
 * an HttpClient request and therefore bypasses the MSAL interceptor) carries a fresh bearer token,
 * including across automatic reconnects.
 *
 * Lifecycle: started from `provideAppInitializer` (app.config.ts). Failures are swallowed and the
 * connection state is surfaced as a signal — a missing realtime channel must not block app render;
 * the reports grid still works via its `httpResource` polling fallback.
 */
@Injectable({ providedIn: 'root' })
export class ReportHubService {
  private readonly msal = inject(MsalService);

  private hub: HubConnection | null = null;

  /** Emits when the backend reports a completed report for this tenant. */
  readonly reportCompleted$ = new Subject<ReportCompletedEvent>();

  /**
   * Monotonic counter bumped on every completion event. Components depend on this signal inside
   * their `httpResource` request fns so a completed report transparently refetches the grid — no
   * manual subscription churn (mirrors the HostApiService.mutationVersion pattern).
   */
  private readonly _completionTick = signal(0);
  readonly completionTick = this._completionTick.asReadonly();

  private readonly _connected = signal(false);
  readonly connected = this._connected.asReadonly();

  /** Starts the hub connection. Idempotent; safe to call once at app init. */
  async connect(): Promise<void> {
    if (this.hub && this.hub.state !== HubConnectionState.Disconnected) {
      return;
    }

    this.hub = new HubConnectionBuilder()
      .withUrl('/hubs/reports', {
        accessTokenFactory: () => this.acquireToken(),
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.hub.on('ReportCompleted', (data: ReportCompletedEvent) => {
      this.reportCompleted$.next(data);
      this._completionTick.update((n) => n + 1);
    });

    this.hub.onreconnected(() => this._connected.set(true));
    this.hub.onreconnecting(() => this._connected.set(false));
    this.hub.onclose(() => this._connected.set(false));

    try {
      await this.hub.start();
      this._connected.set(true);
    } catch {
      // Realtime is best-effort; the grid degrades to its httpResource read. Never block render.
      this._connected.set(false);
    }
  }

  async disconnect(): Promise<void> {
    if (this.hub) {
      await this.hub.stop();
      this._connected.set(false);
    }
  }

  /** Silently acquires the API access token for the SignalR negotiate/connect handshake. */
  private async acquireToken(): Promise<string> {
    const cfg = getAuthConfig();
    const account = this.msal.instance.getActiveAccount() ?? this.msal.instance.getAllAccounts()[0];
    if (!account) {
      return '';
    }
    try {
      const result = await this.msal.instance.acquireTokenSilent({
        scopes: [cfg.apiScope],
        account,
      });
      return result.accessToken;
    } catch {
      return '';
    }
  }
}
