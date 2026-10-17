import IncidentSuggestions from './IncidentSuggestions';
import { useEffect, useState, type FormEvent } from 'react';
import { API, authFetch } from './AuthGate';
import IncidentTimeline from './IncidentTimeline';

type Incident = {
  id: string;
  title: string;
  service: string;
  status: string;
  assignedTechnicianId: string | null;
  assignedTechnicianName: string | null;
  isAssignedToMe: boolean;
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

type IncidentAction =
  | 'assign-to-me'
  | 'release-assignment'
  | 'status';

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
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  const [incidentId, setIncidentId] = useState('');
  const [reportId, setReportId] = useState('');

  const [search, setSearch] = useState('');
  const [serviceFilter, setServiceFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [assignmentFilter, setAssignmentFilter] = useState('');

  async function load() {
    const response = await authFetch(`${API}/api/incidents`);

    // The API remains the security boundary; hide management data for reporters.
    if (response.status === 403) {
      setAllowed(false);
      setIncidents([]);
      setReports([]);
      return;
    }

    if (!response.ok) {
      throw new Error(
        `Cannot load incidents (${response.status}).`
      );
    }

    const items: Incident[] = await response.json();
    const reportItems =
      await readJson<Report[]>('/api/reports');

    setIncidents(items);
    setReports(reportItems);
    setAllowed(true);

    // Clear stale form choices after another action changes available records.
    setIncidentId(current => {
      const stillAvailable = items.some(
        incident =>
          incident.id === current &&
          incident.status !== 'Resolved'
      );

      return stillAvailable ? current : '';
    });

    setReportId(current => {
      const stillAvailable = reportItems.some(
        report => report.id === current && !report.incidentId
      );

      return stillAvailable ? current : '';
    });
  }

  useEffect(() => {
    load()
      .catch(err =>
        setError(
          err instanceof Error
            ? err.message
            : 'Cannot load incidents.'
        )
      )
      .finally(() => setLoading(false));
  }, []);

  async function createIncident(
    event: FormEvent<HTMLFormElement>
  ) {
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

  async function linkReport(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();

    const incident =
      incidents.find(item => item.id === incidentId);

    const report =
      reports.find(item => item.id === reportId);

    // Check eligibility in the UI for quick feedback; the API validates it too.
    if (
      !incident ||
      !report ||
      incident.status === 'Resolved' ||
      report.incidentId ||
      report.service !== incident.service
    ) {
      setError(
        'Choose an open incident and an unlinked report with the same service.'
      );
      return;
    }

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

      // Reload current versions so a stale update can be retried safely.
      await load().catch(() => {});
    } finally {
      setBusy(false);
    }
  }

  async function updateIncident(
    incident: Incident,
    action: IncidentAction,
    nextStatus?: string
  ) {
    setBusy(true);
    setError('');
    setNotice('');

    try {
      await readJson(
        `/api/incidents/${incident.id}/${action}`,
        {
          method: 'PATCH',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            version: incident.version,
            ...(action === 'status'
              ? { status: nextStatus }
              : {})
          })
        }
      );

      await load();

      switch (action) {
        case 'assign-to-me':
          setNotice('Incident assigned to you.');
          break;
        case 'release-assignment':
          setNotice('Assignment released.');
          break;
        case 'status':
          setNotice('Incident status updated.');
          break;
      }
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'Update failed.'
      );

      await load().catch(() => {});
    } finally {
      setBusy(false);
    }
  }

  async function refreshAll() {
    setBusy(true);
    setError('');
    setNotice('');

    try {
      await load();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : 'Cannot refresh incidents.'
      );
    } finally {
      setBusy(false);
    }
  }

  function clearFilters() {
    setSearch('');
    setServiceFilter('');
    setStatusFilter('');
    setAssignmentFilter('');
  }

  const selectedIncident =
    incidents.find(incident => incident.id === incidentId);

  const availableReports = reports.filter(report =>
    selectedIncident &&
    selectedIncident.status !== 'Resolved' &&
    !report.incidentId &&
    report.service === selectedIncident.service
  );

  const searchText = search.trim().toLowerCase();

  // These filters only affect the displayed list; the API still enforces access.
  const filteredIncidents = incidents.filter(incident => {
    const matchesSearch =
      !searchText ||
      incident.title.toLowerCase().includes(searchText);

    const matchesService =
      !serviceFilter || incident.service === serviceFilter;

    const matchesStatus =
      !statusFilter || incident.status === statusFilter;

    const isAssigned =
      Boolean(incident.assignedTechnicianId);

    const matchesAssignment =
      !assignmentFilter ||
      (assignmentFilter === 'mine' && incident.isAssignedToMe) ||
      (assignmentFilter === 'assigned' && isAssigned) ||
      (assignmentFilter === 'unassigned' && !isAssigned);

    return (
      matchesSearch &&
      matchesService &&
      matchesStatus &&
      matchesAssignment
    );
  });

  const myIncidentCount = incidents.filter(
    incident => incident.isAssignedToMe
  ).length;

  if (loading) {
    return <p role="status">Loading incidents…</p>;
  }

  if (!allowed) {
    return error ? (
      <div role="alert" className="error">
        <p>{error}</p>
        <button
          type="button"
          disabled={busy}
          onClick={refreshAll}
        >
          Retry
        </button>
      </div>
    ) : null;
  }

  return (
    <section style={{ marginTop: 24 }}>
      <h2>Incident coordination</h2>

      {error && (
        <div role="alert" className="error">
          {error}
        </div>
      )}

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
            disabled={busy}
            value={incidentId}
            onChange={event => {
              setIncidentId(event.target.value);
              setReportId('');
            }}
          >
            <option value="">Choose an incident</option>

            {incidents
              .filter(incident =>
                incident.status !== 'Resolved'
              )
              .map(incident => (
                <option
                  key={incident.id}
                  value={incident.id}
                >
                  {incident.title} — {incident.service}
                </option>
              ))}
          </select>
        </label>

        <label>
          Unlinked report with matching service
          <select
            required
            disabled={busy || !selectedIncident}
            value={reportId}
            onChange={event =>
              setReportId(event.target.value)
            }
          >
            <option value="">Choose a report</option>

            {availableReports.map(report => (
              <option key={report.id} value={report.id}>
                {report.title}
              </option>
            ))}
          </select>
        </label>

        {selectedIncident && availableReports.length === 0 && (
          <p>
            No unlinked reports match this incident’s service.
          </p>
        )}

        <button
          disabled={
            busy ||
            !selectedIncident ||
            selectedIncident.status === 'Resolved' ||
            !availableReports.some(report =>
              report.id === reportId
            )
          }
        >
          Link report
        </button>
      </form>

      <div className="section-title">
        <h3>Incidents</h3>

        <button
          type="button"
          disabled={busy}
          onClick={refreshAll}
        >
          Refresh incidents and reports
        </button>
      </div>

      <div className="list-filters">
        <label>
          Search
          <input
            type="search"
            value={search}
            placeholder="Search incident title"
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

        <label>
          Assignment
          <select
            value={assignmentFilter}
            onChange={event =>
              setAssignmentFilter(event.target.value)
            }
          >
            <option value="">All assignments</option>
            <option value="mine">
              My incidents ({myIncidentCount})
            </option>
            <option value="assigned">Assigned</option>
            <option value="unassigned">Unassigned</option>
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

      <p role="status">
        Showing {filteredIncidents.length} of{' '}
        {incidents.length} incidents
      </p>

      {incidents.length === 0 ? (
        <p>No incidents yet. Create the first one above.</p>
      ) : filteredIncidents.length === 0 ? (
        <p>
          No incidents match your filters. Try clearing them.
        </p>
      ) : (
        filteredIncidents.map(incident => (
          <article key={incident.id}>
            <strong>{incident.title}</strong>

            <p>
              {incident.service} · {incident.status} ·{' '}
              {incident.reportCount} linked{' '}
              {incident.reportCount === 1 ? 'report' : 'reports'}
            </p>

            <p>
              Assignment:{' '}
              {incident.isAssignedToMe ? (
                <span className="pill">Assigned to you</span>
              ) : incident.assignedTechnicianId ? (
                <span>
                  Assigned to{' '}
                  {incident.assignedTechnicianName ??
                    'another technician'}
                </span>
              ) : (
                <span className="pill">Unassigned</span>
              )}
            </p>

            {!incident.assignedTechnicianId &&
              incident.status !== 'Resolved' && (
                <button
                  type="button"
                  disabled={busy}
                  onClick={() =>
                    updateIncident(incident, 'assign-to-me')
                  }
                >
                  Assign to me
                </button>
              )}

            {incident.isAssignedToMe && (
              <button
                type="button"
                disabled={busy}
                onClick={() =>
                  updateIncident(
                    incident,
                    'release-assignment'
                  )
                }
              >
                Release assignment
              </button>
            )}

            <label>
              Incident status
              <select
                aria-label={`Incident status for ${incident.title}`}
                value={incident.status}
                disabled={busy}
                onChange={event =>
                  updateIncident(
                    incident,
                    'status',
                    event.target.value
                  )
                }
              >
                <option>New</option>
                <option>Investigating</option>
                <option>Resolved</option>
              </select>
            </label>

            <ul>
              {reports
                .filter(report =>
                  report.incidentId === incident.id
                )
                .map(report => (
                  <li key={report.id}>{report.title}</li>
                ))}
            </ul>
            <IncidentSuggestions
              incidentId={incident.id}
              version={incident.version}
              status={incident.status}
              disabled={busy}
              onLinked={load}
            />
            <IncidentTimeline
              incidentId={incident.id}
              version={incident.version}
            />
          </article>
        ))
      )}
    </section>
  );
}