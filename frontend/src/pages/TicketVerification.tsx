import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api } from '../api';

type TicketVerification = {
  status: 'tooEarly' | 'verified' | 'alreadyVerified' | 'notFound';
  eventTitle: string | null;
  startAt: string | null;
  rowLabel: string | null;
  seatNumber: number | null;
};

const verificationRequests = new Map<string, Promise<TicketVerification>>();

function getTicketVerification(token: string): Promise<TicketVerification> {
  const pendingRequest = verificationRequests.get(token);
  if (pendingRequest) return pendingRequest;

  const request = api<TicketVerification>(`/api/tickets/verify/${encodeURIComponent(token)}`);
  verificationRequests.set(token, request);
  void request.then(
    () => {
      if (verificationRequests.get(token) === request) verificationRequests.delete(token);
    },
    () => {
      if (verificationRequests.get(token) === request) verificationRequests.delete(token);
    },
  );
  return request;
}

const dateFormatter = new Intl.DateTimeFormat('ru-RU', {
  timeZone: 'Europe/Moscow',
  day: 'numeric',
  month: 'long',
  year: 'numeric',
});
const timeFormatter = new Intl.DateTimeFormat('ru-RU', {
  timeZone: 'Europe/Moscow',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});

export function TicketVerificationPage() {
  const { token = '' } = useParams();
  const [ticket, setTicket] = useState<TicketVerification | null>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    let active = true;
    getTicketVerification(token)
      .then((result) => { if (active) setTicket(result); })
      .catch(() => { if (active) setError(true); });
    return () => { active = false; };
  }, [token]);

  const status = error ? 'notFound' : ticket?.status ?? 'loading';
  const eventInfo = ticket?.startAt && ticket.eventTitle
    ? {
        title: `«${ticket.eventTitle}»`,
        date: dateFormatter.format(new Date(ticket.startAt)),
        time: timeFormatter.format(new Date(ticket.startAt)),
        seat: `Ряд ${ticket.rowLabel}, место ${ticket.seatNumber}`,
      }
    : null;

  let message = <p>Проверяем билет…</p>;
  if (status === 'notFound') {
    message = (
      <p>{error
        ? 'Не удалось проверить билет. Повторите попытку или обратитесь к Администратору.'
        : 'Мероприятие не найдено. Обратитесь к Администратору'}</p>
    );
  } else if (eventInfo && status === 'tooEarly') {
    message = (
      <>
        <p>
          Мероприятие <strong>{eventInfo.title}</strong> еще не началось.
        </p>
        <p>
          Оно начнется <strong>{eventInfo.date}</strong> в <strong>{eventInfo.time}</strong>.
        </p>
        <p>
          Ваше место: <strong>{eventInfo.seat}</strong>.
        </p>
        <p className="ticket-verification-note">
          Больше не сканируйте код самостоятельно, чтобы Администратор смог верифицировать его на входе.
        </p>
      </>
    );
  } else if (eventInfo && status === 'verified') {
    message = (
      <>
        <p>
          Билет на <strong>{eventInfo.title}</strong> <strong>{eventInfo.date}</strong> в{' '}
          <strong>{eventInfo.time}</strong> верифицирован.
        </p>
        <p>
          Место: <strong>{eventInfo.seat}</strong>.
        </p>
      </>
    );
  } else if (eventInfo && status === 'alreadyVerified') {
    message = (
      <>
        <p>
          Билет на {eventInfo.title} {eventInfo.date} в {eventInfo.time}{' '}
          <strong>уже был верифицирован</strong>.
        </p>
        <p>
          Место: <strong>{eventInfo.seat}</strong>.
        </p>
      </>
    );
  }

  const icon = {
    loading: '…',
    tooEarly: '…',
    verified: '✓',
    alreadyVerified: '●',
    notFound: '×',
  }[status];

  return (
    <main className="ticket-verification-page">
      <section className={`ticket-verification-card ticket-verification-${status}`} aria-live="polite">
        <div className="ticket-verification-icon" aria-hidden="true">{icon}</div>
        {message}
      </section>
    </main>
  );
}
