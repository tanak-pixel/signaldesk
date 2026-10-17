import { useState, type FormEvent, type ReactNode } from 'react';

export const API = 'http://localhost:5081';

// Keep the token in memory. Refreshing the page requires signing in again.
let accessToken: string | null = null;

export function authFetch(
  url: string,
  options: RequestInit = {}
): Promise<Response> {
  const headers = new Headers(options.headers);

  if (accessToken) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }

  return fetch(url, { ...options, headers });
}

export function AuthGate({ children }: { children: ReactNode }) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [signedIn, setSignedIn] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  async function signIn(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError('');

    try {
      const response = await fetch(
        `${API}/auth/login?useCookies=false`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ email: email.trim(), password })
        }
      );

      if (!response.ok) {
        throw new Error(
          response.status === 401
            ? 'Email or password was not accepted.'
            : `Sign-in failed (${response.status}).`
        );
      }

      const result = await response.json();

      if (typeof result.accessToken !== 'string') {
        throw new Error('The API did not return an access token.');
      }

      accessToken = result.accessToken;

      const identity = await authFetch(`${API}/api/me`);

      if (!identity.ok) {
        throw new Error('Could not verify your signed-in account.');
      }

      setPassword('');
      setSignedIn(true);
    } catch (err) {
      accessToken = null;
      setError(
        err instanceof Error ? err.message : 'Unable to sign in.'
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
            gap: 16
          }}
        >
          <span>{email}</span>
          <button onClick={signOut}>Sign out</button>
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
              onChange={event => setEmail(event.target.value)}
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
              onChange={event => setPassword(event.target.value)}
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