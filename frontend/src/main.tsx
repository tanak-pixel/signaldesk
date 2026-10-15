import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './style.css';

type Report = { id: string; title: string; description: string; service: string; location: string; status: string; createdAt: string; version: number };
const API = 'http://localhost:5080'; // Local lesson only. Configure hosting in the deployment milestone.
async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`${API}${path}`, options);
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.message ?? `Request failed (${response.status})`);
  }
  return response.json();
}
function App() {
  const [reports, setReports] = useState<Report[]>([]);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  async function refresh() { setReports(await request<Report[]>('/api/reports')); }
  useEffect(() => { refresh().catch(e => setError(`Cannot load reports. Start the API. ${e.message}`)).finally(() => setLoading(false)); }, []);
  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    setBusy(true); setError('');
    try {
      await request<Report>('/api/reports', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(Object.fromEntries(new FormData(form))) });
      form.reset(); await refresh();
    } catch (e) { setError(e instanceof Error ? e.message : 'Submission failed'); }
    finally { setBusy(false); }
  }
  async function status(report: Report, value: string) {
    setBusy(true); setError('');
    try {
      await request(`/api/reports/${report.id}/status`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ status: value, version: report.version }) });
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Update failed');
      await refresh().catch(() => {});
    } finally { setBusy(false); }
  }
  return <main>
    <header><div><p className="eyebrow">CAMPUS OPERATIONS</p><h1>SignalDesk<span> / Report desk</span></h1></div><span className="pill">Learning build · Local API</span></header>
    <p>Collect the signals. Coordinate the response.</p>
    <aside>Milestone 1: reports and status updates. Reports reset when the API restarts.</aside>
    {error && <div role="alert" className="error">{error}</div>}
    <div className="layout"><section><h2>Report an issue</h2><form onSubmit={submit}>
      <label>Short title<input name="title" required minLength={5} maxLength={120} placeholder="Wi-Fi disconnects in STEM" /></label>
      <label>Service<select name="service"><option>Wi-Fi</option><option>Email</option><option>Printing</option><option>Other</option></select></label>
      <label>Location<input name="location" required minLength={2} maxLength={120} placeholder="STEM, floor 3" /></label>
      <label>What happened?<textarea name="description" required minLength={10} maxLength={2000} placeholder="Describe symptoms and when they started." /></label>
      <button disabled={busy}>{busy ? 'Working…' : 'Submit report'}</button>
    </form></section><section><div className="section-title"><h2>Incoming reports ({reports.length})</h2><button disabled={busy || loading} onClick={() => { setError(''); refresh().catch(e => setError(e.message)); }}>Refresh</button></div>
      {loading ? <p>Loading…</p> : reports.length === 0 ? <p>No reports yet. Submit the first one.</p> : reports.map(report => <article key={report.id}>
        <div className="section-title"><strong>{report.title}</strong><span className="pill">{report.service}</span></div>
        <p>{report.description}</p><small>{report.location} · {new Date(report.createdAt).toLocaleString()}</small>
        <label>Status<select aria-label={`Status for ${report.title}`} value={report.status} disabled={busy} onChange={e => status(report, e.target.value)}><option>New</option><option>Investigating</option><option>Resolved</option></select></label>
      </article>)}
    </section></div>
  </main>;
}
createRoot(document.getElementById('root')!).render(<App />);
