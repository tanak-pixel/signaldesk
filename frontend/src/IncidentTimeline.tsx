import { useEffect, useState } from 'react';
import { API, authFetch } from './AuthGate';

type Activity = {
  id: string;
  actorName: string;
  message: string;
  createdAt: string;
};

export default function IncidentTimeline({
  incidentId,
  version
}: {
  incidentId: string;
  version: number;
}) {
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<Activity[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    if (!open) return;

    // Ignore late responses when the panel closes or its incident changes.
    let cancelled = false;
    setLoading(true);
    setError('');

    async function load() {
      try {
        const response = await authFetch(
          `${API}/api/incidents/${incidentId}/activity`
        );

        if (!response.ok) {
          throw new Error(
            `Cannot load activity (${response.status}).`
          );
        }

        const data: Activity[] = await response.json();

        if (!cancelled) setItems(data);
      } catch (err) {
        if (!cancelled) {
          setError(
            err instanceof Error
              ? err.message
              : 'Cannot load activity.'
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
  // Refresh an open timeline after incident changes or an explicit retry.
  }, [incidentId, version, open, retry]);

  return (
    <div className="incident-timeline">
      <button
        type="button"
        aria-expanded={open}
        onClick={() => setOpen(value => !value)}
      >
        {open ? 'Hide activity' : 'View activity'}
      </button>

      {open && (
        <div>
          {loading ? (
            <p role="status">Loading activity…</p>
          ) : error ? (
            <div role="alert">
              <p>{error}</p>
              <button
                type="button"
                onClick={() => setRetry(value => value + 1)}
              >
                Retry
              </button>
            </div>
          ) : items.length === 0 ? (
            <p>No activity recorded yet.</p>
          ) : (
            <ol>
              {items.map(item => (
                <li key={item.id}>
                  <p>
                    <strong>{item.actorName}</strong>
                    {' — '}{item.message}
                  </p>
                  <small>
                    <time dateTime={item.createdAt}>
                      {new Date(item.createdAt).toLocaleString()}
                    </time>
                  </small>
                </li>
              ))}
            </ol>
          )}
        </div>
      )}
    </div>
  );
}