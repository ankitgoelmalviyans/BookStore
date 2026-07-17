import { HttpContextToken } from '@angular/common/http';

// Set on a request whose caller treats a 404 as a normal, expected outcome (e.g.
// PaymentService.getByOrderId — "no payment yet" is routine, not exceptional) so
// ErrorInterceptor knows not to toast for it. Any request that doesn't set this still gets the
// generic toast on a 404, since a 404 elsewhere usually does mean something's wrong.
export const SUPPRESS_404_TOAST = new HttpContextToken<boolean>(() => false);
