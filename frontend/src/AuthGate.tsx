/// <reference types="vite/client" />

import { useState, type FormEvent, type ReactNode } from 'react';

// Development defaults to the local API.
// Production defaults to the same origin unless VITE_API_URL is supplied.
const configuredApi =
  import.meta.env.VITE_API_URL?.trim() ??
  (import.meta.env.DEV ? 'http://localhost:5080' : '');

export const API = configuredApi.replace(/\/+$/, '');

// Keep the token in memory; refreshing requires signing in again.
let accessToken: string | null = null;

export function authFetch(
  url: string,
  options: RequestInit = {}
): Promise<Response> {
  const headers = new Headers(options.headers);

  if (accessToken) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }

  return fetch(url, {
    ...options,
    headers
  });
}

export function AuthGate({
  children
}: {
  children: ReactNode;
}) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [signedIn, setSignedIn] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  async function signIn(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();

    setBusy(true);
    setError('');
    accessToken = null;

    try {
      const response = await fetch(
        `${API}/auth/login?useCookies=false`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            email: email.trim(),
            password
          })
        }
      );

      if (!response.ok) {
        throw new Error(
          response.status === 401
            ? 'Email or password was not accepted.'
            : response.status === 429
              ? 'Too many attempts. Please try again later.'
              : `Sign-in failed (${response.status}).`
        );
      }

      const result = await response.json();

      if (
        typeof result.accessToken !== 'string' ||
        !result.accessToken
      ) {
        throw new Error(
          'The API did not return an access token.'
        );
      }

      accessToken = result.accessToken;

      const identity = await authFetch(`${API}/api/me`);

      if (!identity.ok) {
        throw new Error(
          'Could not verify your signed-in account.'
        );
      }

      setEmail(email.trim());
      setPassword('');
      setSignedIn(true);
    } catch (err) {
      accessToken = null;

      setError(
        err instanceof TypeError
          ? 'Cannot reach SignalDesk. Check your connection and API address.'
          : err instanceof Error
            ? err.message
            : 'Unable to sign in.'
      );
    } finally {
      setBusy(false);
    }
  }

  function signOut() {
    accessToken = null;
    setPassword('');
    setError('');
    setSignedIn(false);
  }

  if (signedIn) {
    return (
      <>
        <div
          style={{
            padding: '12px 24px',
            display: 'flex',
            justifyContent: 'flex-end',
            alignItems: 'center',
            flexWrap: 'wrap',
            gap: 16
          }}
        >
          <span>{email}</span>

          <button type="button" onClick={signOut}>
            Sign out
          </button>
        </div>

        {children}
      </>
    );
  }

  return (
    <main style={{ maxWidth: 480, margin: '60px auto' }}>
      <section>
        <p className="eyebrow">SIGNALDESK</p>
        <h1>Sign in</h1>
        <p>Use your existing SignalDesk account.</p>

        {error && (
          <div role="alert" className="error">
            {error}
          </div>
        )}

        <form onSubmit={signIn}>
          <label>
            Email
            <input
              type="email"
              autoComplete="username"
              value={email}
              onChange={event =>
                setEmail(event.target.value)
              }
              required
              disabled={busy}
            />
          </label>

          <label>
            Password
            <input
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={event =>
                setPassword(event.target.value)
              }
              required
              disabled={busy}
            />
          </label>

          <button disabled={busy}>
            {busy ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </section>
    </main>
  );
}