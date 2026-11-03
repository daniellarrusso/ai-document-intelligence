import { HttpErrorResponse } from '@angular/common/http';

/** File types offered by the picker. Mirrors the API's FileTypePolicy, which remains the authority. */
export const UPLOAD_ACCEPT = '.pdf,.txt,.jpg,.jpeg,.png,.gif,.webp,.mp4,.mov,.webm';

/** A 400 carries the reason (unsupported type, too large, content doesn't match the extension). */
export function uploadErrorMessage(err: HttpErrorResponse): string {
  return err.status === 400 && typeof err.error === 'string' && err.error ? err.error : 'File upload failed!';
}
