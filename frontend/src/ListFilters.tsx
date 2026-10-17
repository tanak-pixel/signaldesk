export type Filters = {
  search: string;
  service: string;
  status: string;
  assignment: string;
};

export const emptyFilters: Filters = {
  search: '',
  service: '',
  status: '',
  assignment: ''
};

type Props = {
  value: Filters;
  onChange: (value: Filters) => void;
  showAssignment?: boolean;
  searchPlaceholder: string;
};

export default function ListFilters({
  value,
  onChange,
  showAssignment = false,
  searchPlaceholder
}: Props) {
  function change(key: keyof Filters, next: string) {
    onChange({ ...value, [key]: next });
  }

  return (
    <div className="list-filters">
      <label>
        Search
        <input
          type="search"
          value={value.search}
          placeholder={searchPlaceholder}
          onChange={event => change('search', event.target.value)}
        />
      </label>

      <label>
        Service
        <select
          value={value.service}
          onChange={event => change('service', event.target.value)}
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
          value={value.status}
          onChange={event => change('status', event.target.value)}
        >
          <option value="">All statuses</option>
          <option>New</option>
          <option>Investigating</option>
          <option>Resolved</option>
        </select>
      </label>

      {showAssignment && (
        <label>
          Assignment
          <select
            value={value.assignment}
            onChange={event =>
              change('assignment', event.target.value)
            }
          >
            <option value="">All assignments</option>
            <option value="assigned">Assigned</option>
            <option value="unassigned">Unassigned</option>
          </select>
        </label>
      )}

      <button
        type="button"
        className="clear-filters"
        onClick={() => onChange({ ...emptyFilters })}
      >
        Clear filters
      </button>
    </div>
  );
}