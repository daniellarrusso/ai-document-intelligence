/** Mirrors AiDocumentIntelligence.Domain.AppRoles; values match the app roles on the API's Entra registration. */
export const AppRole = {
  Reader: 'Reader',
  Handler: 'Handler',
  Admin: 'Admin',
} as const;

/** Roles allowed to create and change data (the API's CanWrite policy). */
export const WRITE_ROLES: readonly string[] = [AppRole.Handler, AppRole.Admin];
