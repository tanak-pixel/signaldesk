import { useEffect, useState, type FormEvent } from 'react';
import { API, authFetch } from './AuthGate';
import IncidentTimeline from './IncidentTimeline';

type Incident = {
  id: string;
  title: string;
  service: string;
  status: string;
  assignedTechnicianId: string | null;
  version: number;
  reportCount: number;
};

type Report = {
  id: string;
  title: string;
  service: string;
  version: number;
  incidentId: string | null;
};

async function readJson<T>(
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

export default function IncidentPanel() {
  const [incidents, setIncidents] = useState<Incident[]>([]);
  const [reports, setReports] = useState<Report[]>([]);
  const [allowed, setAllowed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [incidentId, setIncidentId] = useState('');
  const [reportId, setReportId] = useState('');

  async function load() {
    const response = await authFetch(`${API}/api/incidents`);

    // Ordinary reporters do not get an incident-management panel.
    if (response.status === 403) {
      setAllowed(false);
      return;
    }

    if (!response.ok) {
      throw new Error(
        `Cannot load incidents (${response.status}).`
      );
    }

    const items: Incident[] = await response.json();
    const reportItems = await readJson<Report[]>('/api/reports');

    setIncidents(items);
    setReports(reportItems);
    setAllowed(true);
  }

  useEffect(() => {
    load().catch(err => setError(err.message));
  }, []);

  async function createIncident(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const form = event.currentTarget;
    setBusy(true);
    setError('');
    setNotice('');

    try {
      await readJson('/api/incidents', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(
          Object.fromEntries(new FormData(form))
        )
      });

      form.reset();
      await load();
      setNotice('Incident created.');
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'Creation failed.'
      );
    } finally {
      setBusy(false);
    }
  }

  async function linkReport(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const incident = incidents.find(i => i.id === incidentId);
    const report = reports.find(r => r.id === reportId);

    if (!incident || !report) return;

    setBusy(true);
    setError('');
    setNotice('');

    try {
      await readJson(
        `/api/incidents/${incident.id}/reports/${report.id}`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            reportVersion: report.version,
            incidentVersion: incident.version
          })
        }
      );

      setReportId('');
      await load();
      setNotice('Report linked successfully.');
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'Linking failed.'
      );

      // Fetch current versions after a conflict.
      await load().catch(() => {});
    } finally {
      setBusy(false);
    }
  }

  async function updateIncident(
    incident: Incident,
    action: 'assign-to-me' | 'status',
    nextStatus?: string
  ) {
    setBusy(true);
    setError('');
    setNotice('');

    try {
      await readJson(`/api/incidents/${incident.id}/${action}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          version: incident.version,
          ...(action === 'status' ? { status: nextStatus } : {})
        })
      });

      await load();

      setNotice(
        action === 'assign-to-me'
          ? 'Incident assigned to you.'
          : 'Incident status updated.'
      );
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'Update failed.'
      );

      await load().catch(() => {});
    } finally {
      setBusy(false);
    }
  }

  const selectedIncident =
    incidents.find(i => i.id === incidentId);

  const availableReports = reports.filter(report =>
    !report.incidentId &&
    report.service === selectedIncident?.service
  );

  if (!allowed) {
    return error ? <div role="alert" className="error">{error}</div> : null;
  }

  return (
    <section style={{ marginTop: 24 }}>
      <h2>Incident coordination</h2>

      {error && <div role="alert" className="error">{error}</div>}
      {notice && <p role="status">{notice}</p>}

      <details>
        <summary>Create an incident</summary>
        <form onSubmit={createIncident}>
          <label>
            Title
            <input
              name="title"
              required
              minLength={5}
              maxLength={120}
              placeholder="STEM Wi-Fi outage"
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
            Description
            <textarea
              name="description"
              required
              minLength={10}
              maxLength={2000}
              placeholder="Several reports of Wi-Fi disconnecting in STEM."
            />
          </label>

          <button disabled={busy}>Create incident</button>
        </form>
      </details>

      <h3>Link a report</h3>

      <form onSubmit={linkReport}>
        <label>
          Incident
          <select
            required
            value={incidentId}
            onChange={event => {
              setIncidentId(event.target.value);
              setReportId('');
            }}
          >
            <option value="">Choose an incident</option>
            {incidents
              .filter(incident => incident.status !== 'Resolved')
              .map(incident => (
                <option key={incident.id} value={incident.id}>
                  {incident.title} — {incident.service}
                </option>
              ))}
          </select>
        </label>

        <label>
          Unlinked report with matching service
          <select
            required
            value={reportId}
            onChange={event => setReportId(event.target.value)}
          >
            <option value="">Choose a report</option>
            {availableReports.map(report => (
              <option key={report.id} value={report.id}>
                {report.title}
              </option>
            ))}
          </select>
        </label>

        <button disabled={busy || !incidentId || !reportId}>
          Link report
        </button>
      </form>

      <h3>Incidents ({incidents.length})</h3>

      <button
        disabled={busy}
        onClick={() => {
          setError('');
          load().catch(err => setError(err.message));
        }}
      >
        Refresh incidents and reports
      </button>

      {incidents.map(incident => (
        <article key={incident.id}>
          <strong>{incident.title}</strong>
          <p>
            {incident.service} · {incident.status} ·
            {' '}{incident.reportCount} linked reports
          </p>

          <p>
            Assignment: {incident.assignedTechnicianId
              ? 'Assigned'
              : 'Unassigned'}
          </p>

          {!incident.assignedTechnicianId &&
            incident.status !== 'Resolved' && (
              <button
                type="button"
                disabled={busy}
                onClick={() => updateIncident(incident, 'assign-to-me')}
              >
                Assign to me
              </button>
            )}

          <label>
            Incident status
            <select
              aria-label={`Incident status for ${incident.title}`}
              value={incident.status}
              disabled={busy}
              onChange={event =>
                updateIncident(incident, 'status', event.target.value)
              }
            >
              <option>New</option>
              <option>Investigating</option>
              <option>Resolved</option>
            </select>
          </label>

          <ul>
            {reports
              .filter(report => report.incidentId === incident.id)
              .map(report => (
                <li key={report.id}>{report.title}</li>
              ))}
          </ul>

          <IncidentTimeline
            incidentId={incident.id}
            version={incident.version}
          />
        </article>
      ))}
    </section>
  );
}
