export const environment = {
  production: false,
  authApiUrl: 'http://localhost:57797/api',
  productApiUrl: 'http://localhost:51668/api',
  inventoryApiUrl: 'http://localhost:5191/api',
  orderApiUrl: 'http://localhost:5100/api',
  paymentApiUrl: 'http://localhost:5200/api',
  recommendationApiUrl: 'http://localhost:5310/api',
  aiApiUrl: 'http://localhost:5320/api',
  // Isolated from aiApiUrl above — a separate microservice/Foundry project for the Help Assistant
  // widget. The Angular app never talks to Foundry directly: this backend holds the Entra ID
  // credentials and calls the published Foundry Agent Application server-side.
  helpAssistantApiUrl: 'http://localhost:5330/api',
  stripePublishableKey: 'pk_test_51Tw06QJD0DuM5qcDR4D6S1hPOCat5C2qRF0PFWqCbAg7m9NlhP2g5Bp6TKJ9kMSrvIABegUTWEK04DVG7sV9Rwyz001HIchdZY'
};