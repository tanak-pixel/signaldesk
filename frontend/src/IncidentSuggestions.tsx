import { useEffect, useState } from 'react';
import { API, authFetch } from './AuthGate';

type SuggestedReport = {
  id: string;
  title: string;
  service: string;
  location: string;
  status: string;
  createdAt: string;
  version: number;
  match: 'Strong' | 'Possible';
  score: number;
  reasons: string[];
};

type SuggestionResponse = {
  incidentVersion: number;
  candidateLimitReached: boolean;
  suggestions: SuggestedReport[];
};

type Props = {
  incidentId: string;
  version: number;
  status: string;
  disabled: boolean;
  onLinked: () => Promise<void>;
};

async function request<T>(
  path: string,
  options?: RequestInit
): Promise<T> {
  const response = await authFetch(`${API}${path}`, options);

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));

    throw new Error(
      body.message ?? `Request failed (${response.status}).`
    );
  }

  return response.json();
}

export default function IncidentSuggestions({
  incidentId,
  version,
  status,
  disabled,
  onLinked
}: Props) {
  const [open, setOpen] = useState(false);
  const [data, setData] =
    useState<SuggestionResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [linking, setLinking] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [refreshKey, setRefreshKey] = useState(0);

  useEffect(() => {
    if (!open || status === 'Resolved') return;

    let cancelled = false;

    setLoading(true);
    setError('');
    setData(null);

    async function load() {
      try {
        const result = await request<SuggestionResponse>(
          `/api/incidents/${incidentId}/suggestions`
        );

        if (!cancelled) setData(result);
      } catch (err) {
        if (!cancelled) {
          setError(
            err instanceof Error
              ? err.message
              : 'Cannot load suggestions.'
          );
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void load();

    return () => {
      cancelled = true;
    };
  }, [incidentId, version, status, open, refreshKey]);

  async function approve(report: SuggestedReport) {
    if (!data || disabled || linking) return;

    setLinking(true);
    setError('');
    setNotice('');

    try {
      await request(
        `/api/incidents/${incidentId}/reports/${report.id}`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            reportVersion: report.version,
            incidentVersion: data.incidentVersion
          })
        }
      );

      setNotice(`Linked "${report.title}".`);
      await onLinked();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'Linking failed.'
      );

      // Refresh parent records after a conflict or failed refresh.
      await onLinked().catch(() => {});
    } finally {
      setLinking(false);
      setRefreshKey(value => value + 1);
    }
  }

  if (status === 'Resolved') return null;

  return (
    <div className="incident-suggestions">
      <button
        type="button"
        aria-expanded={open}
        disabled={disabled || linking}
        onClick={() => setOpen(value => !value)}
      >
        {open ? 'Hide suggestions' : 'Suggested reports'}
      </button>

      {open && (
        <div>
          <p>
            Suggestions use service, submission time, and
            linked-report locations. Review before linking.
          </p>

          {notice && <p role="status">{notice}</p>}

          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}

          <button
            type="button"
            disabled={disabled || loading || linking}
            onClick={() => setRefreshKey(value => value + 1)}
          >
            Refresh suggestions
          </button>

          {loading ? (
            <p role="status">Finding related reports…</p>
          ) : data ? (
            <>
              {data.candidateLimitReached && (
                <p>
                  Showing suggestions from the 200 most recent
                  reports in the matching time range.
                </p>
              )}

              {data.suggestions.length === 0 ? (
                <p>No matching unlinked reports found.</p>
              ) : (
                data.suggestions.map(report => (
                  <article key={report.id}>
                    <div className="section-title">
                      <strong>{report.title}</strong>
                      <span className="pill">
                        {report.match} match
                      </span>
                    </div>

                    <p>
                      {report.location} · {report.status}
                    </p>

                    <small>
                      {new Date(
                        report.createdAt
                      ).toLocaleString()}
                    </small>

                    <ul>
                      {report.reasons.map(reason => (
                        <li key={reason}>{reason}</li>
                      ))}
                    </ul>

                    <button
                      type="button"
                      disabled={disabled || linking || loading}
                      onClick={() => approve(report)}
                    >
                      {linking ? 'Working…' : 'Approve and link'}
                    </button>
                  </article>
                ))
              )}
            </>
          ) : null}
        </div>
      )}
    </div>
  );
}