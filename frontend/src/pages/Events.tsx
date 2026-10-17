import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, ApiError } from '../api';
import { SeatLegend, SeatMap } from '../components/SeatMap';
import { PageFrame } from '../App';
import { eventInputToIso, formatEventDate, formatEventDateInput, statusLabel } from '../format';
import type {
  AdminSeat, EventItem, EventStatistics, Quota, Reservation, UserProfile, UserSeat,
} from '../types';
import { Alert, Button, EmptyState, ErrorMessage, FieldError, Loading } from '../ui';

type Mode = 'admin' | 'user';

export function EventListPage({ mode }: { mode: Mode }) {
  const isAdmin = mode === 'admin';
  const [events, setEvents] = useState<EventItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [search, setSearch] = useState('');
  const [period, setPeriod] = useState('all');
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);

  async function loadEvents() {
    setLoading(true);
    setError(null);
    try {
      if (isAdmin) {
        const query = new URLSearchParams({ period, page: String(page), pageSize: '50' });
        if (search.trim()) query.set('search', search.trim());
        if (status) query.set('status', status);
        const response = await fetch(`/api/admin/events?${query}`, { credentials: 'include' });
        const envelope = await response.json();
        if (!response.ok || !envelope.success) throw new ApiError(response.status, envelope.error?.code ?? 'REQUEST_FAILED', envelope.error?.message ?? 'Не удалось загрузить мероприятия.');
        setEvents(envelope.data as EventItem[]);
        setTotalCount(Number(response.headers.get('X-Total-Count') ?? 0));
      } else {
        setEvents(await api<EventItem[]>('/api/events'));
      }
    } catch (reason) {
      setError(reason);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void loadEvents(); }, [isAdmin, period, page, status]);

  function submitSearch(event: FormEvent) {
    event.preventDefault();
    if (page === 1) void loadEvents();
    else setPage(1);
  }

  return (
    <PageFrame
      title={isAdmin ? 'Мероприятия' : 'Ваши мероприятия'}
      description={isAdmin ? 'Создавайте события, назначайте квоты и следите за бронированиями.' : 'События, доступные по вашей квоте.'}
      actions={isAdmin && <Link className="button button-primary" to="/admin/events/new">Создать мероприятие <span aria-hidden="true">＋</span></Link>}
    >
      {isAdmin && (
        <form className="filter-bar" onSubmit={submitSearch}>
          <label className="field filter-search"><span className="sr-only">Поиск по мероприятиям</span><input placeholder="Поиск по названию" value={search} onChange={(event) => setSearch(event.target.value)} /></label>
          <label className="field"><span className="sr-only">Период</span><select value={period} onChange={(event) => { setPage(1); setPeriod(event.target.value); }}><option value="all">Все даты</option><option value="upcoming">Предстоящие</option><option value="past">Прошедшие</option></select></label>
          <label className="field"><span className="sr-only">Статус</span><select value={status} onChange={(event) => { setPage(1); setStatus(event.target.value); }}><option value="">Все статусы</option><option value="draft">Черновики</option><option value="published">Опубликовано</option><option value="completed">Завершено</option><option value="cancelled">Отменено</option></select></label>
          <Button type="submit" variant="secondary">Найти</Button>
        </form>
      )}
      {loading ? <Loading /> : error ? <ErrorMessage error={error} /> : events.length === 0 ? (
        <EmptyState title={isAdmin ? 'Мероприятия не найдены' : 'Пока нет доступных мероприятий'}>
          {isAdmin ? 'Измените фильтры или создайте первое событие.' : 'Администратор назначит вам квоту, когда добавит событие.'}
        </EmptyState>
      ) : (
        <>
          <div className="event-grid">
            {events.map((item) => (
              <article className="event-card" key={item.eventId}>
                <div className="event-card-top">
                  <span className={`status status-${item.status}`}>{statusLabel(item.status)}</span>
                  <span className="event-date-short">{formatEventDate(item.startAt)}</span>
                </div>
                <h2><Link to={`${isAdmin ? '/admin/events' : '/events'}/${item.eventId}`}>{item.title}</Link></h2>
                {item.description && <p className="event-description">{item.description}</p>}
                <div className="event-card-bottom">
                  <span className="quota-inline">{isAdmin
                    ? `${item.quotas?.length ?? 0} квот`
                    : `${item.myQuota?.usedSeats ?? 0} из ${item.myQuota?.availableSeats ?? 0} мест`}</span>
                  <Link className="text-link" to={`${isAdmin ? '/admin/events' : '/events'}/${item.eventId}`}>Открыть <span aria-hidden="true">→</span></Link>
                </div>
              </article>
            ))}
          </div>
          {isAdmin && totalCount > 50 && <div className="pagination"><Button variant="secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>Назад</Button><span>Страница {page} из {Math.ceil(totalCount / 50)}</span><Button variant="secondary" disabled={page * 50 >= totalCount} onClick={() => setPage(page + 1)}>Дальше</Button></div>}
        </>
      )}
    </PageFrame>
  );
}

export function EventFormPage() {
  const { eventId } = useParams();
  const isEdit = Boolean(eventId);
  const navigate = useNavigate();
  const [users, setUsers] = useState<UserProfile[]>([]);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [startAt, setStartAt] = useState('');
  const [endAt, setEndAt] = useState('');
  const [enableAdditionalRow12A, setEnableAdditionalRow12A] = useState(false);
  const [quotas, setQuotas] = useState<Array<{ userId: string; availableSeats: string }>>([]);
  const [loading, setLoading] = useState(isEdit);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let active = true;
    const load = async () => {
      try {
        if (!eventId) {
          const userList = await api<UserProfile[]>('/api/admin/users');
          if (active) setUsers(userList.filter((user) => user.role === 'User'));
        }
        if (eventId) {
          const item = await api<EventItem>(`/api/admin/events/${eventId}`);
          if (!active) return;
          setTitle(item.title);
          setDescription(item.description ?? '');
          setStartAt(formatEventDateInput(item.startAt));
          setEndAt(formatEventDateInput(item.endAt));
          setEnableAdditionalRow12A(item.enableAdditionalRow12A);
        }
      } catch (reason) {
        if (active) setError(reason);
      } finally {
        if (active) setLoading(false);
      }
    };
    void load();
    return () => { active = false; };
  }, [eventId]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (isEdit) {
        const body = {
          title: title.trim(),
          description: description.trim(),
          ...(!started && { startAt: eventInputToIso(startAt) }),
          endAt: eventInputToIso(endAt),
          enableAdditionalRow12A,
        };
        await api(`/api/admin/events/${eventId}`, { method: 'PATCH', body: JSON.stringify(body) });
        navigate(`/admin/events/${eventId}`);
      } else {
        const body = {
          title: title.trim(),
          description: description.trim() || null,
          startAt: eventInputToIso(startAt),
          endAt: eventInputToIso(endAt),
          enableAdditionalRow12A,
          quotas: quotas.filter((quota) => quota.userId && Number(quota.availableSeats) > 0)
            .map((quota) => ({ userId: quota.userId, availableSeats: Number(quota.availableSeats) })),
        };
        const created = await api<EventItem>('/api/admin/events', { method: 'POST', body: JSON.stringify(body) });
        navigate(`/admin/events/${created.eventId}`);
      }
    } catch (reason) {
      setError(reason);
    } finally {
      setBusy(false);
    }
  }

  if (loading) return <Loading />;
  const selectedUsers = new Set(quotas.map((quota) => quota.userId).filter(Boolean));
  const eventStart = eventId && startAt ? Date.parse(eventInputToIso(startAt)) : 0;
  const started = eventId !== undefined && eventStart <= Date.now();

  return (
    <PageFrame title={isEdit ? 'Редактирование мероприятия' : 'Новое мероприятие'} description="Время указывается по Москве (UTC+3).">
      <div className="form-card">
        {Boolean(error) && <ErrorMessage error={error} />}
        <FieldError error={error} />
        <form className="form-stack" onSubmit={submit}>
          <label className="field"><span>Название</span><input required maxLength={200} value={title} onChange={(event) => setTitle(event.target.value)} /></label>
          <label className="field"><span>Описание <small>необязательно</small></span><textarea rows={4} maxLength={2000} value={description} onChange={(event) => setDescription(event.target.value)} /></label>
          <div className="form-grid">
            <label className="field"><span>Начало</span><input type="datetime-local" required disabled={started} value={startAt} onChange={(event) => setStartAt(event.target.value)} /></label>
            <label className="field"><span>Окончание</span><input type="datetime-local" required value={endAt} onChange={(event) => setEndAt(event.target.value)} /></label>
          </div>
          <label className="checkbox-field"><input type="checkbox" checked={enableAdditionalRow12A} onChange={(event) => setEnableAdditionalRow12A(event.target.checked)} /><span>Добавить дополнительный ряд 12А</span></label>
          {!isEdit && (
            <section className="initial-quotas">
              <div className="section-heading"><div><h2>Квоты участников</h2><p className="muted">Укажите пользователям места, доступные для бронирования.</p></div><Button variant="secondary" type="button" onClick={() => setQuotas([...quotas, { userId: '', availableSeats: '1' }])}>Добавить участника</Button></div>
              {quotas.map((quota, index) => (
                <div className="quota-editor" key={`${index}-${quota.userId}`}>
                  <label className="field"><span className="sr-only">Пользователь</span><select required value={quota.userId} onChange={(event) => setQuotas(quotas.map((item, i) => i === index ? { ...item, userId: event.target.value } : item))}><option value="">Выберите пользователя</option>{users.filter((user) => !selectedUsers.has(user.id) || user.id === quota.userId).map((user) => <option key={user.id} value={user.id}>{user.name} · {user.login}</option>)}</select></label>
                  <label className="field"><span className="sr-only">Количество мест в квоте</span><input type="number" min="1" max="643" required value={quota.availableSeats} onChange={(event) => setQuotas(quotas.map((item, i) => i === index ? { ...item, availableSeats: event.target.value } : item))} /></label>
                  <Button type="button" variant="quiet" aria-label="Удалить квоту" onClick={() => setQuotas(quotas.filter((_, i) => i !== index))}>Убрать</Button>
                </div>
              ))}
              {users.length === 0 && <p className="helper-text">Сначала создайте пользователей и настройте почту для отправки паролей.</p>}
            </section>
          )}
          <div className="form-actions"><Link className="button button-secondary" to={eventId ? `/admin/events/${eventId}` : '/admin/events'}>Отмена</Link><Button type="submit" disabled={busy}>{busy ? 'Сохраняем…' : isEdit ? 'Сохранить изменения' : 'Создать черновик'}</Button></div>
        </form>
      </div>
    </PageFrame>
  );
}

export function EventDetailPage({ mode }: { mode: Mode }) {
  const { eventId = '' } = useParams();
  const admin = mode === 'admin';
  const [event, setEvent] = useState<EventItem | null>(null);
  const [stats, setStats] = useState<EventStatistics | null>(null);
  const [quotas, setQuotas] = useState<Quota[]>([]);
  const [seatData, setSeatData] = useState<Array<UserSeat | AdminSeat>>([]);
  const [reservations, setReservations] = useState<Reservation[]>([]);
  const [myReservation, setMyReservation] = useState<Reservation | null>(null);
  const [selectedUserId, setSelectedUserId] = useState('');
  const [selectedSeats, setSelectedSeats] = useState<Set<string>>(new Set());
  const [tab, setTab] = useState<'overview' | 'seats' | 'quotas'>('overview');
  const [designation, setDesignation] = useState<'vip' | 'organization' | 'none'>('vip');
  const [users, setUsers] = useState<UserProfile[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);
  const [seatMode, setSeatMode] = useState<'booking' | 'designation'>('booking');

  async function load() {
    setLoading(true);
    setError(null);
    try {
      if (admin) {
        const [item, statsData, quotaData, seatList, reservationList, userList] = await Promise.all([
          api<EventItem>(`/api/admin/events/${eventId}`),
          api<EventStatistics>(`/api/admin/events/${eventId}/statistics`),
          api<Quota[]>(`/api/admin/events/${eventId}/quotas`),
          api<AdminSeat[]>(`/api/admin/events/${eventId}/seats`),
          api<Reservation[]>(`/api/admin/events/${eventId}/reservations`),
          api<UserProfile[]>('/api/admin/users'),
        ]);
        setEvent(item);
        setStats(statsData);
        setQuotas(quotaData);
        setSeatData(seatList);
        setReservations(reservationList);
        setUsers(userList.filter((user) => user.role === 'User'));
        const selected = quotaData.some((quota) => quota.userId === selectedUserId)
          ? selectedUserId
          : quotaData[0]?.userId ?? '';
        setSelectedUserId(selected);
      } else {
        const [item, seatList, reservation] = await Promise.all([
          api<EventItem>(`/api/events/${eventId}`),
          api<UserSeat[]>(`/api/events/${eventId}/seats`),
          api<Reservation>(`/api/events/${eventId}/my-reservation`),
        ]);
        setEvent(item);
        setSeatData(seatList);
        setMyReservation(reservation);
        setSelectedSeats(new Set(reservation.seatIds));
      }
    } catch (reason) {
      setError(reason);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void load(); }, [eventId, admin]);

  useEffect(() => {
    if (!admin || !selectedUserId) return;
    let active = true;
    api<Reservation>(`/api/admin/events/${eventId}/quotas/${selectedUserId}/reservation`)
      .then((reservation) => { if (active) setSelectedSeats(new Set(reservation.seatIds)); })
      .catch((reason: unknown) => {
        if (active && !(reason instanceof ApiError && reason.code === 'QUOTA_NOT_FOUND')) setError(reason);
        if (active) setSelectedSeats(new Set());
      });
    return () => { active = false; };
  }, [admin, eventId, selectedUserId]);

  const currentReservation = admin ? reservations.find((item) => item.userId === selectedUserId) : myReservation;
  const canBook = Boolean(event?.bookingOpen);
  const canAdminManage = admin && Boolean(event) && event!.status !== 'completed' && event!.status !== 'cancelled'
    && Date.parse(event!.endAt) > Date.now();
  const canAdminBook = canAdminManage && event?.status === 'published';

  function toggleSeat(seatId: string) {
    setSelectedSeats((previous) => {
      const next = new Set(previous);
      if (next.has(seatId)) next.delete(seatId);
      else next.add(seatId);
      return next;
    });
  }

  async function saveReservation() {
    if (!event || !selectedUserId && admin) return;
    setBusy(true);
    setError(null);
    setMessage('');
    const path = admin
      ? `/api/admin/events/${eventId}/quotas/${selectedUserId}/reservation`
      : `/api/events/${eventId}/my-reservation`;
    try {
      await api(path, { method: 'PUT', body: JSON.stringify({ seatIds: [...selectedSeats] }) });
      setMessage('Бронирование сохранено.');
      await load();
    } catch (reason) {
      setError(reason);
      if (reason instanceof ApiError && reason.code === 'SEAT_ALREADY_BOOKED') await load();
    } finally {
      setBusy(false);
    }
  }

  async function cancelReservation() {
    if (!event) return;
    setBusy(true);
    setError(null);
    setMessage('');
    const path = admin
      ? `/api/admin/events/${eventId}/quotas/${selectedUserId}/reservation`
      : `/api/events/${eventId}/my-reservation`;
    try {
      await api(path, { method: 'DELETE' });
      setSelectedSeats(new Set());
      setMessage('Бронирование отменено.');
      await load();
    } catch (reason) {
      setError(reason);
    } finally {
      setBusy(false);
    }
  }

  async function changeDesignation(seatId: string, next: 'vip' | 'organization' | 'none') {
    if (!canAdminManage) return;
    setBusy(true);
    setError(null);
    try {
      await api(`/api/admin/events/${eventId}/seats/${encodeURIComponent(seatId)}/designation`, {
        method: 'PUT',
        body: JSON.stringify({ designation: next }),
      });
      await load();
    } catch (reason) {
      setError(reason);
    } finally {
      setBusy(false);
    }
  }

  async function changeStatus(action: 'publish' | 'cancel') {
    if (action === 'cancel' && !window.confirm('Отменить это мероприятие?')) return;
    setBusy(true);
    setError(null);
    try {
      await api(`/api/admin/events/${eventId}/${action}`, { method: 'POST' });
      await load();
    } catch (reason) {
      setError(reason);
    } finally {
      setBusy(false);
    }
  }

  async function reloadAdminData() {
    await load();
  }

  if (loading) return <Loading />;
  if (Boolean(error) && !event) return <PageFrame title="Мероприятие"><ErrorMessage error={error} /></PageFrame>;
  if (!event) return <PageFrame title="Мероприятие"><EmptyState title="Мероприятие не найдено" /></PageFrame>;
  const reserveCount = selectedSeats.size;
  const quota = admin ? quotas.find((item) => item.userId === selectedUserId) : event.myQuota;

  return (
    <PageFrame
      title={event.title}
      description={event.description ?? undefined}
      actions={admin ? (
        <div className="heading-actions">
          {!['completed', 'cancelled'].includes(event.status) && <Link className="button button-secondary" to={`/admin/events/${eventId}/edit`}>Изменить</Link>}
          {event.status === 'draft' && <Button disabled={busy} onClick={() => void changeStatus('publish')}>Опубликовать</Button>}
          {['draft', 'published'].includes(event.status) && <Button variant="danger" disabled={busy} onClick={() => void changeStatus('cancel')}>Отменить</Button>}
        </div>
      ) : <Link className="text-link back-link" to="/events">← Все мероприятия</Link>}
    >
      <div className="event-meta-strip">
        <span className={`status status-${event.status}`}>{statusLabel(event.status)}</span>
        <span><b>Начало</b>{formatEventDate(event.startAt)}</span>
        <span><b>Окончание</b>{formatEventDate(event.endAt)}</span>
        {event.enableAdditionalRow12A && <span className="meta-tag">Ряд 12А</span>}
      </div>
      {Boolean(error) && <ErrorMessage error={error} />}
      {message && <Alert variant="success">{message}</Alert>}
      {admin && <div className="tabs" role="tablist" aria-label="Разделы мероприятия">
        <button className={tab === 'overview' ? 'tab active' : 'tab'} role="tab" aria-selected={tab === 'overview'} onClick={() => setTab('overview')}>Обзор</button>
        <button className={tab === 'seats' ? 'tab active' : 'tab'} role="tab" aria-selected={tab === 'seats'} onClick={() => setTab('seats')}>Зал и бронирования</button>
        <button className={tab === 'quotas' ? 'tab active' : 'tab'} role="tab" aria-selected={tab === 'quotas'} onClick={() => setTab('quotas')}>Квоты</button>
      </div>}
      {(!admin || tab === 'overview') && (
        <>
          {admin && stats && <StatisticsCards stats={stats} />}
          {!admin && <section className="quota-summary"><div className="quota-circle">{quota?.remainingSeats ?? 0}</div><div><strong>Осталось мест по квоте</strong><p className="muted">{quota?.usedSeats ?? 0} забронировано из {quota?.availableSeats ?? 0}</p></div></section>}
          {!admin && (
            <section className="panel reservation-panel">
              <div className="section-heading"><div><h2>Выберите места</h2><p className="muted">Можно выбрать до {quota?.remainingSeats ?? 0} дополнительных мест. Ваши места отмечены.</p></div><span className="selection-count">{reserveCount} выбрано</span></div>
              <SeatLegend />
              <SeatMap data={seatData as UserSeat[]} selected={selectedSeats} onToggle={toggleSeat} disabled={!canBook} enableAdditionalRow12A={event.enableAdditionalRow12A} />
              {!canBook && <p className="helper-text">Бронирование закрыто: мероприятие уже началось или не опубликовано.</p>}
              <div className="form-actions">
                <Button variant="secondary" disabled={busy || selectedSeats.size === 0} onClick={() => setSelectedSeats(new Set())}>Снять выбор</Button>
                {myReservation?.seatIds.length ? <Button variant="danger" disabled={busy || !canBook} onClick={() => void cancelReservation()}>Отменить бронь</Button> : null}
                <Button disabled={busy || !canBook || selectedSeats.size > (quota?.availableSeats ?? 0)} onClick={() => void saveReservation()}>{busy ? 'Сохраняем…' : myReservation?.seatIds.length ? 'Обновить бронь' : 'Подтвердить выбор'}</Button>
              </div>
            </section>
          )}
        </>
      )}
      {admin && tab === 'seats' && (
        <section className="panel">
          <div className="section-heading"><div><h2>Схема зала</h2><p className="muted">Просматривайте бронирования или назначайте категории мест.</p></div>
            <div className="view-controls">
              <label className="field"><span className="sr-only">Режим схемы</span><select value={seatMode} onChange={(event) => setSeatMode(event.target.value as 'booking' | 'designation')}><option value="booking">Бронирование</option><option value="designation">Категории мест</option></select></label>
              {seatMode === 'booking' && <label className="field"><span className="sr-only">Участник</span><select value={selectedUserId} onChange={(event) => setSelectedUserId(event.target.value)}><option value="">Выберите участника</option>{quotas.map((item) => <option key={item.userId} value={item.userId}>{item.name ?? item.login ?? item.userId} · {item.usedSeats}/{item.availableSeats}</option>)}</select></label>}
              {seatMode === 'designation' && <label className="field"><span className="sr-only">Категория нового места</span><select value={designation} onChange={(event) => setDesignation(event.target.value as typeof designation)}><option value="vip">VIP</option><option value="organization">Организационное</option><option value="none">Снять категорию</option></select></label>}
            </div>
          </div>
          <SeatLegend admin />
          <SeatMap
            data={seatData as AdminSeat[]}
            selected={selectedSeats}
            selectedUserId={seatMode === 'booking' ? selectedUserId : undefined}
            disabled={seatMode === 'booking' ? !canAdminBook || !selectedUserId : !canAdminManage}
            admin
            enableAdditionalRow12A={event.enableAdditionalRow12A}
            onToggle={toggleSeat}
            onDesignation={seatMode === 'designation' ? (seatId) => void changeDesignation(seatId, designation) : undefined}
          />
          <section className="booking-roster" aria-label="Бронирования участников">
            <h3>Занятые места</h3>
            {reservations.length === 0 ? <p className="muted">Пока нет бронирований.</p> : <div className="roster-list">
              {reservations.map((item) => {
                const participant = users.find((user) => user.id === item.userId);
                return <div className="roster-item" key={item.userId}>
                  <div><strong>{participant?.name ?? 'Участник'}</strong><small>{participant?.login ?? item.userId}</small></div>
                  <span>{item.seatIds.join(', ')}</span>
                </div>;
              })}
            </div>}
          </section>
          {seatMode === 'booking' && <>
            <div className="booking-actions">
              <div><strong>{selectedUserId ? users.find((item) => item.id === selectedUserId)?.name ?? 'Участник' : 'Выберите участника'}</strong><p className="muted">Выбрано мест: {reserveCount} из {quota?.availableSeats ?? 0}</p></div>
              <div className="heading-actions">
                {currentReservation?.seatIds.length ? <Button variant="danger" disabled={busy || !canAdminBook} onClick={() => void cancelReservation()}>Отменить бронь</Button> : null}
                <Button disabled={busy || !canAdminBook || !selectedUserId || reserveCount > (quota?.availableSeats ?? 0)} onClick={() => void saveReservation()}>{busy ? 'Сохраняем…' : 'Сохранить бронь'}</Button>
              </div>
            </div>
            {!canAdminBook && <p className="helper-text">Бронирование доступно для опубликованного мероприятия до его завершения.</p>}
          </>}
          {seatMode === 'designation' && <p className="helper-text">Нажмите на свободное место, чтобы назначить выбранную категорию. Бронирование защищено от изменений.</p>}
        </section>
      )}
      {admin && tab === 'quotas' && (
        <section className="panel">
          <div className="section-heading"><div><h2>Квоты участников</h2><p className="muted">Выделенные места и текущее использование по каждому участнику.</p></div><Link className="text-link" to="/admin/users">Управление пользователями →</Link></div>
          <QuotaTable eventId={eventId} quotas={quotas} reservations={reservations} users={users} readOnly={!canAdminManage} onChanged={reloadAdminData} />
        </section>
      )}
    </PageFrame>
  );
}

function StatisticsCards({ stats }: { stats: EventStatistics }) {
  const values = [
    ['Вместимость зала', stats.totalSeats],
    ['Забронировано мест', stats.reservedSeats],
    ['Свободно в зале', stats.unreservedSeats],
    ['Доступно для брони', stats.userBookableSeats],
    ['Назначено по квотам', stats.allocatedQuotaSeats],
    ['Использовано по квотам', stats.usedQuotaSeats],
    ['Остаток по квотам', stats.remainingQuotaSeats],
    ['Бронирований', stats.reservationCount],
  ];
  return <div className="stats-grid">{values.map(([label, value]) => <div className="stat-card" key={label}><span>{label}</span><strong>{value}</strong></div>)}</div>;
}

function QuotaTable({
  eventId,
  quotas,
  reservations,
  users,
  readOnly,
  onChanged,
}: {
  eventId: string;
  quotas: Quota[];
  reservations: Reservation[];
  users: UserProfile[];
  readOnly: boolean;
  onChanged: () => Promise<void>;
}) {
  const [values, setValues] = useState<Record<string, number>>({});
  const [cancelSeats, setCancelSeats] = useState<Record<string, Set<string>>>({});
  const [busyUserId, setBusyUserId] = useState('');
  const [newUserId, setNewUserId] = useState('');
  const [newQuota, setNewQuota] = useState(1);
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const currentValue = (quota: Quota) => values[quota.userId] ?? quota.availableSeats;

  const ordered = useMemo(() => [...quotas].sort((a, b) => (a.name ?? a.login ?? '').localeCompare(b.name ?? b.login ?? '', 'ru')), [quotas]);

  async function save(quota: Quota) {
    setBusyUserId(quota.userId);
    setError(null);
    try {
      await api(`/api/admin/events/${eventId}/quotas/${quota.userId}`, {
        method: 'PUT',
        body: JSON.stringify({ availableSeats: currentValue(quota), cancelSeatIds: [...(cancelSeats[quota.userId] ?? [])] }),
      });
      setValues((previous) => { const next = { ...previous }; delete next[quota.userId]; return next; });
      setCancelSeats((previous) => { const next = { ...previous }; delete next[quota.userId]; return next; });
      await onChanged();
    } catch (reason) {
      setError(reason);
    } finally {
      setBusyUserId('');
    }
  }

  async function addQuota() {
    if (!newUserId) return;
    setAdding(true);
    setError(null);
    try {
      await api(`/api/admin/events/${eventId}/quotas/${newUserId}`, { method: 'PUT', body: JSON.stringify({ availableSeats: newQuota }) });
      setNewUserId('');
      await onChanged();
    } catch (reason) { setError(reason); }
    finally { setAdding(false); }
  }

  return (
    <>
      {Boolean(error) && <ErrorMessage error={error} />}
      {ordered.length === 0 ? <EmptyState title="Квоты ещё не назначены">Назначьте квоты при создании события или добавьте участника.</EmptyState> : (
        <div className="table-wrap"><table><thead><tr><th>Участник</th><th>Использовано</th><th>Выделено</th><th>Осталось</th><th>Действия</th></tr></thead><tbody>
          {ordered.map((quota) => {
            const newValue = currentValue(quota);
            const cancellationCount = Math.max(0, quota.usedSeats - newValue);
            const selectedForCancel = cancelSeats[quota.userId] ?? new Set<string>();
            return <tr key={quota.userId}>
              <td><strong>{quota.name ?? quota.login ?? 'Пользователь'}</strong>{quota.login && <small className="table-subtitle">{quota.login}</small>}</td>
              <td>{quota.usedSeats}</td>
              <td><input disabled={readOnly} className="table-number" aria-label={`Квота: ${quota.name ?? quota.login ?? quota.userId}`} type="number" min="0" max="643" value={newValue} onChange={(event) => {
                const value = Number(event.target.value);
                setValues({ ...values, [quota.userId]: value });
                if (value >= quota.usedSeats) setCancelSeats((previous) => { const next = { ...previous }; delete next[quota.userId]; return next; });
              }} /></td>
              <td>{Math.max(0, newValue - quota.usedSeats)}</td>
              <td><div className="row-actions"><Button variant="secondary" disabled={readOnly || busyUserId === quota.userId || (cancellationCount > 0 && selectedForCancel.size !== cancellationCount)} onClick={() => void save(quota)}>{busyUserId === quota.userId ? '…' : 'Сохранить'}</Button><Button variant="quiet" disabled={readOnly || busyUserId === quota.userId} onClick={async () => {
                if (!window.confirm(`Удалить квоту для ${quota.name ?? quota.login ?? 'пользователя'}?`)) return;
                setBusyUserId(quota.userId);
                setError(null);
                try {
                  await api(`/api/admin/events/${eventId}/quotas/${quota.userId}`, { method: 'DELETE' });
                  await onChanged();
                } catch (reason) { setError(reason); }
                finally { setBusyUserId(''); }
              }}>Удалить</Button></div></td>
            </tr>;
          })}
        </tbody></table></div>
      )}
      {ordered.some((quota) => Math.max(0, quota.usedSeats - currentValue(quota)) > 0) && <div className="cancel-quota-list">
        <h3>Места для отмены</h3>
        {ordered.filter((quota) => Math.max(0, quota.usedSeats - currentValue(quota)) > 0).map((quota) => {
          const count = quota.usedSeats - currentValue(quota);
          const reservation = reservations.find((item) => item.userId === quota.userId);
          const chosen = cancelSeats[quota.userId] ?? new Set<string>();
          return <fieldset className="cancel-seats" key={quota.userId}>
            <legend>{quota.name ?? quota.login}: выберите {count} место(а)</legend>
            {(reservation?.seatIds ?? []).map((seatId) => <label key={seatId}><input disabled={readOnly} type="checkbox" checked={chosen.has(seatId)} onChange={(event) => {
              const next = new Set(chosen);
              if (event.target.checked) next.add(seatId); else next.delete(seatId);
              setCancelSeats({ ...cancelSeats, [quota.userId]: next });
            }} /> {seatId}</label>)}
          </fieldset>;
        })}
      </div>}
      <div className="quota-add">
        <label className="field"><span>Участник без квоты</span><select disabled={readOnly} value={newUserId} onChange={(event) => setNewUserId(event.target.value)}><option value="">Выберите пользователя</option>{users.filter((user) => !quotas.some((quota) => quota.userId === user.id)).map((user) => <option key={user.id} value={user.id}>{user.name} · {user.login}</option>)}</select></label>
        <label className="field quota-add-count"><span>Мест</span><input disabled={readOnly} type="number" min="0" max="643" value={newQuota} onChange={(event) => setNewQuota(Number(event.target.value))} /></label>
        <Button variant="secondary" disabled={readOnly || adding || !newUserId} onClick={() => void addQuota()}>{adding ? 'Назначаем…' : 'Назначить квоту'}</Button>
      </div>
      <p className="helper-text">Чтобы снизить квоту ниже числа бронирований, сначала выберите конкретные места для отмены. Удалить квоту с активными бронями нельзя.</p>
    </>
  );
}
