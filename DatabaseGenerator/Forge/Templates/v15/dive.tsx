import { useSQLQuery } from "@motherduck/react-sql-query";

// Internal Dive; no embedded session or paid embedding dependency.
// Both queries consume canonical Gold/marts. No KPI formula or business join belongs here.
export default function ContosoForgeValidation() {
  const kpis = useSQLQuery('select * from "__DATABASE__".gold.kpi_customer_satisfaction');
  const daily = useSQLQuery('select * from "__DATABASE__".gold.bi_daily_customer_experience order by order_day, store_key');
  const rows = Array.isArray(kpis.data) ? kpis.data : [];
  const trends = Array.isArray(daily.data) ? daily.data : [];
  if (kpis.error || daily.error) return <div role="alert">Gold could not be read. Verify this run's warehouse and dbt result.</div>;
  return <main className="p-8 max-w-6xl mx-auto">
    <h1 className="text-3xl font-semibold">Contoso Forge · Customer experience</h1>
    <p className="mt-3 mb-8">Canonical dbt Gold. Reconciliation and model evidence are in the companion Evidence report.</p>
    {kpis.isLoading ? <p>Loading Gold KPIs…</p> : rows.map((row, i) => <div key={i} className="grid grid-cols-3 gap-4">
      {Object.entries(row).map(([name, value]) => <section key={name} className="border rounded p-4">
        <h2 className="text-sm">{name.replaceAll('_', ' ')}</h2><p className="text-2xl">{String(value ?? '—')}</p>
      </section>)}
    </div>)}
    <h2 className="text-xl mt-8 mb-4">Daily results · order day / store grain</h2>
    {daily.isLoading ? <p>Loading the daily mart…</p> : <div className="overflow-auto"><table className="w-full text-sm">
      <thead><tr>{Object.keys(trends[0] ?? {}).map(k => <th key={k} className="p-2 text-left">{k.replaceAll('_', ' ')}</th>)}</tr></thead>
      <tbody>{trends.map((row, i) => <tr key={i}>{Object.entries(row).map(([k, v]) => <td key={k} className="p-2 border-t">{String(v ?? '—')}</td>)}</tr>)}</tbody>
    </table></div>}
  </main>;
}
