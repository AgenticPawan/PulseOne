# Prompt: Module 1 — Authentication & Account

## Context
PulseOne uses OpenID Connect with Authorization Code Flow + PKCE. Tokens are stored in `SameSite=Strict; HttpOnly; Secure` cookies with refresh-token rotation. There are two distinct principals: **tenant users** and **host operators**.

## Task

### Backend: JWT + Cookie Auth Setup (`PulseOne.WebApi/Program.cs` additions)
```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => {
        o.Authority = builder.Configuration["AzureAd:Authority"];
        o.Audience  = builder.Configuration["AzureAd:Audience"];
        o.TokenValidationParameters.ValidateIssuerSigningKey = true;
        o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("HostOperatorsOnly", p => p
        .RequireAuthenticatedUser()
        .RequireClaim("portal", "host")
        .RequireRole("platform-operator"));
```

### Claims Transformer
Implement `TenantClaimsTransformer : IClaimsTransformation` that:
- Reads `tenant_id` from the JWT `extension_tenant_id` claim (Azure AD B2C custom attribute)
- Reads `portal` claim (`"tenant"` or `"host"`)
- Adds them to `ClaimsPrincipal` in a normalized form

### Angular: MSAL / OIDC Setup (`client-app`)
- Configure `@azure/msal-angular` with Authorization Code Flow + PKCE
- `MsalGuard` on all authenticated routes
- Token acquired silently with `MsalInterceptor` attaching Bearer token to API calls
- `auth.service.ts` exposes: `login()`, `logout()`, `isAuthenticated$`, `currentUser$`

### Angular: Host Portal Auth (`host-admin-app`)
- Same MSAL setup but `authority` points to a separate B2C user flow for platform operators
- Guard `HostOperatorGuard` checks `portal === 'host'` claim — but the **real** boundary is the `HostOperatorsOnly` API policy

### Refresh Token Rotation
- Backend: sliding expiry cookie, refresh endpoint at `POST /api/v1/auth/refresh`
- Frontend: MSAL handles silent token acquisition automatically

## Output Locations
- `src/backend/PulseOne.WebApi/Auth/TenantClaimsTransformer.cs`
- `src/backend/PulseOne.WebApi/Auth/AuthEndpoints.cs`
- `src/client-app/src/app/core/auth/`
- `src/host-admin-app/src/app/core/auth/`

## Constraints
- The `HostOperatorsOnly` policy is the authoritative boundary — Angular guards are UI-only
- Refresh tokens must rotate on each use
- `SameSite=Strict` on auth cookies — no exceptions for "convenience"
- Azure AD B2C configuration values come from Key Vault / environment — never hardcoded
