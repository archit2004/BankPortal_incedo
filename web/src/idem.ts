import { useRef } from 'react';
import { ApiError } from './api';

// One Idempotency-Key per logical submission. It is kept across retries (network errors / 5xx),
// so a retry can never move money twice, and rotated once the server has answered definitively.
export function useIdempotencyKey() {
  const ref = useRef<string>(crypto.randomUUID());
  return {
    get: () => ref.current,
    reset: () => {
      ref.current = crypto.randomUUID();
    },
  };
}

export const shouldRotateKey = (e: unknown) => e instanceof ApiError && e.status >= 400 && e.status < 500;