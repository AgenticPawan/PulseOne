# Prompt: Tenant Portal — Angular 20

## Context
The tenant portal (`client-app`) is the self-service interface for each tenant's users. It is served at `{tenantId}.pulseone.io`. Tenants are completely isolated from each other and cannot access host-admin routes.

## Task

### App Shell (`src/client-app/src/app/`)
- `app.config.ts`: `provideZonelessChangeDetection()`, MSAL providers, `provideHttpClient`, `provideRouter`
- Theming: read `--tenant-primary` and `--tenant-accent` CSS variables injected by the server (per-subdomain config endpoint)
- Sidebar: Dashboard, Reports, Billing, Team, Settings
- Skip link for accessibility: `<a href="#main-content" class="sr-only focus:not-sr-only">Skip to main content</a>`

### Dashboard (`features/dashboard/`)
- KPI cards: Active Users, Reports Generated, Storage Used, Current Plan
- Recent Activity feed: last 10 audit events for the current user
- All via `httpResource` with auto-refresh every 60 seconds (`refreshInterval` signal)

### Reports Grid (`features/reports/`) — blueprint §6.5
Implement `ReportGridComponent` exactly as in blueprint §6.5:
- Signals: `pageIndex`, `searchFilter`, `sortColumn`, `sortDirection`
- `httpResource<PagedResult>()` — reactive, cancels in-flight on signal change
- Table with sort headers, search input, pagination controls
- Row actions: View, Download (SAS URL), Delete (soft-delete via API)
- `onSearch(v)` resets page to 1; `onSort(col)` toggles direction

**`report-create.component.ts`** — modal/dialog form
- Report type selector, parameters form (dynamic based on type)
- On submit: `POST /api/v1/reports` → receives `reportId` → polls status via SignalR

### SignalR Integration (`core/services/report-hub.service.ts`)
```typescript
@Injectable({ providedIn: 'root' })
export class ReportHubService {
  private hub = new HubConnectionBuilder()
    .withUrl('/hubs/reports', { accessTokenFactory: () => this.auth.getToken() })
    .withAutomaticReconnect()
    .build();

  readonly reportCompleted$ = new Subject<{ reportId: string; downloadUrl: string }>();

  async connect() {
    this.hub.on('ReportCompleted', (data) => this.reportCompleted$.next(data));
    await this.hub.start();
  }
}
```

### Team Management (`features/team/`)
- List tenant users with roles/permissions
- Invite user (sends email via background job)
- Edit permissions: PBAC permission checkboxes grouped by category
- Deactivate/reactivate user

### Settings (`features/settings/`)
- Company profile: name, logo upload, contact info
- API Keys: generate/revoke API keys for integrations
- Notification preferences: email/SMS/WhatsApp per event type
- Danger zone: export all data, request account deletion

## Output Location
`src/client-app/src/app/`

## Constraints
- `httpResource` for ALL reactive HTTP — never `effect()` + subscribe pattern
- No hardcoded tenant IDs or API keys
- All inputs sanitized via Angular's built-in sanitization — no `bypassSecurityTrust*` unless through explicit Trusted Types
- `aria-live="polite"` on loading states so screen readers announce data changes
- Focus management in dialogs: trap focus, restore on close
