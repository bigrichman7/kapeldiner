import { useEffect, useMemo, useState } from 'react';
import { api } from '../api';
import { PageFrame } from '../App';
import { formatEventDate } from '../format';
import type { AdminStatistics } from '../types';
import { EmptyState, ErrorMessage, Loading } from '../ui';

const numberFormatter = new Intl.NumberFormat('ru-RU');
const percentFormatter = new Intl.NumberFormat('ru-RU', {
  style: 'percent',
  maximumFractionDigits: 0,
});
const verificationTimeFormatter = new Intl.DateTimeFormat('ru-RU', {
  timeZone: 'Europe/Moscow',
  dateStyle: 'short',
  timeStyle: 'short',
});

function percentage(verifiedSeats: number, bookedSeats: number): string {
  return bookedSeats === 0 ? '—' : percentFormatter.format(verifiedSeats / bookedSeats);
}

export function StatisticsPage() {
  const [eventId, setEventId] = useState('');
  const [statistics, setStatistics] = useState<AdminStatistics | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);
    const query = eventId ? `?eventId=${encodeURIComponent(eventId)}` : '';
    api<AdminStatistics>(`/api/admin/statistics${query}`)
      .then((result) => {
        if (active) setStatistics(result);
      })
      .catch((reason: unknown) => {
        if (active) setError(reason);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => { active = false; };
  }, [eventId]);

  const rankedDepartments = useMemo(
    () => [...(statistics?.departments ?? [])].sort((first, second) => {
      const attendanceDifference = (second.bookedSeats > 0 ? second.verifiedSeats / second.bookedSeats : -1)
        - (first.bookedSeats > 0 ? first.verifiedSeats / first.bookedSeats : -1);
      return attendanceDifference
        || second.verifiedSeats - first.verifiedSeats
        || first.bookedSeats - second.bookedSeats
        || (first.name ?? '').localeCompare(second.name ?? '', 'ru');
    }),
    [statistics],
  );
  const eventTitle = eventId
    ? statistics?.events.find((item) => item.eventId === eventId)?.title
    : 'Все мероприятия';

  return (
    <PageFrame
      title="Статистика посещаемости"
      description="Квоты, бронирования и подтверждённые проходы по QR-кодам для каждого подразделения."
    >
      {statistics && (
        <div className="statistics-toolbar">
          <label className="field">
            <span>Мероприятие</span>
            <select aria-label="Мероприятие" value={eventId} onChange={(event) => setEventId(event.target.value)}>
              <option value="">Все мероприятия</option>
              {statistics.events.map((item) => (
                <option key={item.eventId} value={item.eventId}>
                  {item.title} · {formatEventDate(item.startAt)}
                </option>
              ))}
            </select>
          </label>
          <p className="muted">{eventTitle}</p>
        </div>
      )}

      {error ? <ErrorMessage error={error} /> : loading || !statistics ? <Loading /> : (
        <>
          <div className="stats-grid statistics-summary">
            <div className="stat-card"><span>Выделено квот · {eventId ? 'мероприятие' : 'все мероприятия'}</span><strong>{numberFormatter.format(statistics.allocatedQuotaSeats)}</strong></div>
            <div className="stat-card"><span>Забронировано мест</span><strong>{numberFormatter.format(statistics.bookedSeats)}</strong></div>
            <div className="stat-card"><span>Подтверждено проходов</span><strong>{numberFormatter.format(statistics.verifiedSeats)}</strong></div>
            <div className="stat-card"><span>Посещаемость от бронирований</span><strong>{percentage(statistics.verifiedSeats, statistics.bookedSeats)}</strong></div>
            <div className="stat-card statistics-lifetime-card">
              <span>Квот по всем мероприятиям за всё время</span>
              <strong>{numberFormatter.format(statistics.totalAllocatedQuotaSeats)}</strong>
              <small>Сумма сохранённых квот; прежние значения изменённых или удалённых квот не учитываются.</small>
            </div>
          </div>

          {rankedDepartments.length === 0 ? (
            <EmptyState title="Статистики пока нет">Для выбранного периода ещё не выделены квоты.</EmptyState>
          ) : (
            <section className="panel statistics-panel">
              <div className="section-heading">
                <div>
                  <h2>Подразделения</h2>
                  <p className="muted">Рейтинг отсортирован по доле подтверждённых проходов среди забронированных мест.</p>
                </div>
              </div>
              <div className="table-wrap">
                <table className="statistics-table">
                  <thead>
                    <tr>
                      <th>Место</th>
                      <th>Подразделение</th>
                      <th>Квота</th>
                      <th>Забронировано</th>
                      <th>Подтверждено</th>
                      <th>Посещаемость</th>
                      <th>Билеты</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rankedDepartments.map((department, index) => (
                      <tr key={department.userId}>
                        <td>{department.bookedSeats > 0 ? index + 1 : '—'}</td>
                        <td><strong>{department.name ?? 'Удалённое подразделение'}</strong></td>
                        <td>{numberFormatter.format(department.allocatedSeats)}</td>
                        <td>{numberFormatter.format(department.bookedSeats)}</td>
                        <td>{numberFormatter.format(department.verifiedSeats)}</td>
                        <td>{percentage(department.verifiedSeats, department.bookedSeats)}</td>
                        <td>
                          {department.tickets.length === 0 ? '—' : (
                            <details className="statistics-ticket-details">
                              <summary>Показать ({department.tickets.length})</summary>
                              <ul className="statistics-ticket-list">
                                {department.tickets.map((ticket) => (
                                  <li key={`${ticket.eventId}-${ticket.rowLabel}-${ticket.seatNumber}`}>
                                    <strong>{ticket.eventTitle}</strong>
                                    <span>Ряд {ticket.rowLabel}, место {ticket.seatNumber}</span>
                                    <small>{verificationTimeFormatter.format(new Date(ticket.verifiedAtUtc))}</small>
                                  </li>
                                ))}
                              </ul>
                            </details>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}
        </>
      )}
    </PageFrame>
  );
}
