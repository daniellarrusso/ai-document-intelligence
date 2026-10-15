// Swapped in for environment.ts by the "development" build configuration (see angular.json).
export const environment = {
  apiBaseUrl: 'http://localhost:5265/api',
  // Entra ID identifiers (public values, not secrets). The SPA signs users in and requests the API's delegated scope.
  auth: {
    authority: 'https://login.microsoftonline.com/d433ee20-8bd8-42fa-bd65-634587e4aa84',
    clientId: '96f5d48a-7b67-4f52-aac0-b3a150c2057d',
    apiScope: 'api://68c28c82-4728-42a6-ae27-aa3691e4d63a/access_as_user',
  },
};
