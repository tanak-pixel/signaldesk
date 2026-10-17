/// <reference types="vite/client" />

import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import IncidentPanel from './IncidentPanel';
import { AuthGate, API, authFetch } from './AuthGate';
import './style.css';

type Report = {
  id: string;
  title: string;
  description: string;
  service: string;
  location: string;
  status: string;
  createdAt: string;
  version: number;
  incidentId: string | null;
  linkedIncident: {
    title: string;
    status: string;
  } | null;
};

async function request<T>(
  path: string,
  options?: RequestInit
): Promise<T> {
  const response = await authFetch(`${API}${path}`, options);

  // Convert API errors into messages the form and list can display.
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(
      body.message ?? `Request failed (${response.status}).`
    );
  }

  return response.json();
}

function App() {
  const [activeTab, setActiveTab] =
    useState<'reports' | 'incidents'>('reports');

  const [canManage, setCanManage] = useState(false);
  const [reports, setReports] = useState<Report[]>([]);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);

  const [search, setSearch] = useState('');
  const [serviceFilter, setServiceFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');

  async function refresh() {
    setReports(await request<Report[]>('/api/reports'));
  }

  useEffect(() => {
    refresh()
      .catch(err =>
        setError(`Cannot load reports. ${err.message}`)
      )
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    // The API filters report ownership; this endpoint only controls the UI.
    request<{ canManageIncidents: boolean }>('/api/access')
      .then(access =>
        setCanManage(access.canManageIncidents)
      )
      .catch(() => {
        setCanManage(false);
        setError(
          'Cannot check account permissions. Refresh to retry.'
        );
      });
  }, []);

  async function submit(
    event: React.FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();

    const form = event.currentTarget;
    setBusy(true);
    setError('');

    try {
      await request('/api/reports', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(
          Object.fromEntries(new FormData(form))
        )
      });

      form.reset();
      await refresh();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'Submission failed.'
      );
    } finally {
      setBusy(false);
    }
  }

  async function updateStatus(report: Report, value: string) {
    setBusy(true);
    setError('');

    try {
      await request(`/api/reports/${report.id}/status`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          status: value,
          version: report.version
        })
      });

      await refresh();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'Update failed.'
      );

      await refresh().catch(() => {});
    } finally {
      setBusy(false);
    }
  }

  async function refreshReports() {
    setLoading(true);
    setError('');

    try {
      await refresh();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : 'Cannot refresh reports.'
      );
    } finally {
      setLoading(false);
    }
  }

  function clearFilters() {
    setSearch('');
    setServiceFilter('');
    setStatusFilter('');
  }

  const searchText = search.trim().toLowerCase();

  // Search and dropdown filters refine the already-authorized report list.
  const filteredReports = reports.filter(report => {
    const matchesSearch =
      !searchText ||
      report.title.toLowerCase().includes(searchText) ||
      report.location.toLowerCase().includes(searchText);

    const matchesService =
      !serviceFilter || report.service === serviceFilter;

    const matchesStatus =
      !statusFilter || report.status === statusFilter;

    return matchesSearch && matchesService && matchesStatus;
  });

  return (
    <main>
      <header>
        <div>
          <p className="eyebrow">CAMPUS OPERATIONS</p>
          <h1>
            SignalDesk<span> / Report desk</span>
          </h1>
        </div>

        <span className="pill">
          Learning build · Local API
        </span>
      </header>

      <p>Collect the signals. Coordinate the response.</p>

      <aside>
        Reports are saved. Technicians can group related reports
        into incidents.
      </aside>

      {error && (
        <div role="alert" className="error">
          {error}
        </div>
      )}

      <nav className="desk-tabs" aria-label="Desk views">
        <button
          type="button"
          aria-pressed={activeTab === 'reports'}
          onClick={() => setActiveTab('reports')}
        >
          Reports
        </button>

        {canManage && (
          <button
            type="button"
            aria-pressed={activeTab === 'incidents'}
            onClick={() => setActiveTab('incidents')}
          >
            Incidents
          </button>
        )}
      </nav>

      <div hidden={activeTab !== 'reports'}>
        <div className="layout">
          <section>
            <h2>Report an issue</h2>

            <form onSubmit={submit}>
              <label>
                Short title
                <input
                  name="title"
                  required
                  minLength={5}
                  maxLength={120}
                  placeholder="Wi-Fi disconnects in STEM"
                />
              </label>

              <label>
                Service
                <select name="service">
                  <option>Wi-Fi</option>
                  <option>Email</option>
                  <option>Printing</option>
                  <option>Other</option>
                </select>
              </label>

              <label>
                Location
                <input
                  name="location"
                  required
                  minLength={2}
                  maxLength={120}
                  placeholder="STEM, floor 3"
                />
              </label>

              <label>
                What happened?
                <textarea
                  name="description"
                  required
                  minLength={10}
                  maxLength={2000}
                  placeholder="Describe symptoms and when they started."
                />
              </label>

              <button disabled={busy || loading}>
                {busy ? 'Working…' : 'Submit report'}
              </button>
            </form>
          </section>

          <section>
            <div className="section-title">
              <h2>
                {canManage ? 'Incoming reports' : 'My reports'}
              </h2>

              <button
                type="button"
                disabled={busy || loading}
                onClick={refreshReports}
              >
                Refresh
              </button>
            </div>

            <div className="list-filters">
              <label>
                Search
                <input
                  type="search"
                  value={search}
                  placeholder="Search title or location"
                  onChange={event =>
                    setSearch(event.target.value)
                  }
                />
              </label>

              <label>
                Service
                <select
                  value={serviceFilter}
                  onChange={event =>
                    setServiceFilter(event.target.value)
                  }
                >
                  <option value="">All services</option>
                  <option>Wi-Fi</option>
                  <option>Email</option>
                  <option>Printing</option>
                  <option>Other</option>
                </select>
              </label>

              <label>
                Status
                <select
                  value={statusFilter}
                  onChange={event =>
                    setStatusFilter(event.target.value)
                  }
                >
                  <option value="">All statuses</option>
                  <option>New</option>
                  <option>Investigating</option>
                  <option>Resolved</option>
                </select>
              </label>

              <button
                type="button"
                className="clear-filters"
                onClick={clearFilters}
              >
                Clear filters
              </button>
            </div>

            {!loading && (
              <p role="status">
                Showing {filteredReports.length} of{' '}
                {reports.length} reports
              </p>
            )}

            {loading ? (
              <p>Loading…</p>
            ) : reports.length === 0 ? (
              <p>No reports yet. Submit the first one.</p>
            ) : filteredReports.length === 0 ? (
              <p>
                No reports match your filters. Try clearing them.
              </p>
            ) : (
              filteredReports.map(report => (
                <article key={report.id}>
                  <div className="section-title">
                    <strong>{report.title}</strong>
                    <span className="pill">
                      {report.service}
                    </span>
                  </div>

                  <p>{report.description}</p>

                  <small>
                    {report.location} ·{' '}
                    {new Date(report.createdAt).toLocaleString()}
                  </small>

                  {canManage ? (
                    <label>
                      Status
                      <select
                        aria-label={`Status for ${report.title}`}
                        value={report.status}
                        disabled={busy}
                        onChange={event =>
                          updateStatus(
                            report,
                            event.target.value
                          )
                        }
                      >
                        <option>New</option>
                        <option>Investigating</option>
                        <option>Resolved</option>
                      </select>
                    </label>
                  ) : (
                    <p>
                      Status:{' '}
                      <span className="pill">
                        {report.status}
                      </span>
                    </p>
                  )}

                  {report.linkedIncident && (
                    // Expose only the linked incident summary returned by the API.
                    <div className="linked-incident">
                      <strong>Related incident</strong>
                      <p>{report.linkedIncident.title}</p>
                      <p>
                        Incident status:{' '}
                        <span className="pill">
                          {report.linkedIncident.status}
                        </span>
                      </p>
                    </div>
                  )}
                </article>
              ))
            )}
          </section>
        </div>
      </div>

      {canManage && (
        <div hidden={activeTab !== 'incidents'}>
          <IncidentPanel />
        </div>
      )}
    </main>
  );
}

createRoot(document.getElementById('root')!).render(
  <AuthGate>
    <App />
  </AuthGate>
);