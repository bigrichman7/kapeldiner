import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import type { AdminSeat, EventStatistics, EventItem, Quota, Reservation, UserProfile, UserSeat } from '../types';
import { EventDetailPage } from './Events';

vi.mock('../api', () => ({
  api: vi.fn(),
  ApiError: class ApiError extends Error {
    status = 0;
    code = '';
  },
}));

const event: EventItem = {
  eventId: 'event-1',
  title: 'Проверка кнопки',
  description: null,
  startAt: '2026-11-10T18:00:00+03:00',
  endAt: '2026-11-10T20:00:00+03:00',
  status: 'published',
  createdAt: '2026-11-01T10:00:00Z',
  bookingOpen: true,
  myQuota: { availableSeats: 2, usedSeats: 0, remainingSeats: 2 },
  quotas: null,
  enableAdditionalRow12A: false,
};

const emptyReservation: Reservation = {
  reservationId: null,
  eventId: event.eventId,
  userId: 'user-1',
  seatIds: [],
  availableSeats: 2,
  usedSeats: 0,
  remainingSeats: 2,
};

let currentReservation = emptyReservation;

beforeEach(() => {
  currentReservation = emptyReservation;
  vi.mocked(api).mockImplementation(async <T,>(path: string): Promise<T> => {
    if (path === `/api/events/${event.eventId}`) return event as T;
    if (path === `/api/events/${event.eventId}/seats`) {
      return [{ seatId: '1-1', available: true }] satisfies UserSeat[] as T;
    }
    if (path === `/api/events/${event.eventId}/my-reservation`) return currentReservation as T;
    throw new Error(`Unexpected API request: ${path}`);
  });
});

describe('EventDetailPage ticket controls', () => {
  it('warns before deleting a draft and only deletes after confirmation', async () => {
    const draftEvent = { ...event, status: 'draft' as const };
    vi.mocked(api).mockImplementation(async <T,>(path: string, options?: RequestInit): Promise<T> => {
      if (path === `/api/admin/events/${event.eventId}` && options?.method === 'DELETE') return undefined as T;
      if (path === `/api/admin/events/${event.eventId}`) return draftEvent as T;
      if (path === `/api/admin/events/${event.eventId}/statistics`) {
        return {
          eventId: event.eventId,
          totalSeats: 0,
          reservedSeats: 0,
          unreservedSeats: 0,
          userBookableSeats: 0,
          allocatedQuotaSeats: 0,
          usedQuotaSeats: 0,
          remainingQuotaSeats: 0,
          reservationCount: 0,
          users: [],
        } satisfies EventStatistics as T;
      }
      if (path === `/api/admin/events/${event.eventId}/quotas`
        || path === `/api/admin/events/${event.eventId}/seats`
        || path === `/api/admin/events/${event.eventId}/reservations`
        || path === '/api/admin/users') return [] as T;
      throw new Error(`Unexpected API request: ${path}`);
    });
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);

    render(
      <MemoryRouter initialEntries={[`/admin/events/${event.eventId}`]}>
        <Routes>
          <Route path="/admin/events/:eventId" element={<EventDetailPage mode="admin" />} />
          <Route path="/admin/events" element={<div>Список мероприятий</div>} />
        </Routes>
      </MemoryRouter>,
    );

    fireEvent.click(await screen.findByRole('button', { name: 'Удалить' }));

    expect(confirm).toHaveBeenCalledWith(
      'Удалить это мероприятие? Вся связанная с мероприятием информация о бронировании будет безвозвратно удалена.',
    );
    await screen.findByText('Список мероприятий');
    expect(api).toHaveBeenCalledWith(`/api/admin/events/${event.eventId}`, { method: 'DELETE' });
    confirm.mockRestore();
  });

  it('does not offer deletion while an event is published', async () => {
    vi.mocked(api).mockImplementation(async <T,>(path: string): Promise<T> => {
      if (path === `/api/admin/events/${event.eventId}`) return event as T;
      if (path === `/api/admin/events/${event.eventId}/statistics`) {
        return {
          eventId: event.eventId,
          totalSeats: 0,
          reservedSeats: 0,
          unreservedSeats: 0,
          userBookableSeats: 0,
          allocatedQuotaSeats: 0,
          usedQuotaSeats: 0,
          remainingQuotaSeats: 0,
          reservationCount: 0,
          users: [],
        } satisfies EventStatistics as T;
      }
      if (path === `/api/admin/events/${event.eventId}/quotas`
        || path === `/api/admin/events/${event.eventId}/seats`
        || path === `/api/admin/events/${event.eventId}/reservations`
        || path === '/api/admin/users') return [] as T;
      throw new Error(`Unexpected API request: ${path}`);
    });

    render(
      <MemoryRouter initialEntries={[`/admin/events/${event.eventId}`]}>
        <Routes>
          <Route path="/admin/events/:eventId" element={<EventDetailPage mode="admin" />} />
        </Routes>
      </MemoryRouter>,
    );

    await screen.findByRole('button', { name: 'Отменить' });
    expect(screen.queryByRole('button', { name: 'Удалить' })).not.toBeInTheDocument();
  });

  it('hides the ticket button until the user has booked seats', async () => {
    render(
      <MemoryRouter initialEntries={[`/events/${event.eventId}`]}>
        <Routes>
          <Route path="/events/:eventId" element={<EventDetailPage mode="user" />} />
        </Routes>
      </MemoryRouter>,
    );

    await screen.findByRole('heading', { name: 'Выберите места' });
    expect(screen.queryByRole('button', { name: 'Получить билеты' })).not.toBeInTheDocument();
  });

  it('shows an active ticket button above the seat map when seats are booked', async () => {
    currentReservation = {
      ...emptyReservation,
      reservationId: 'reservation-1',
      seatIds: ['1-1'],
      usedSeats: 1,
      remainingSeats: 1,
    };

    render(
      <MemoryRouter initialEntries={[`/events/${event.eventId}`]}>
        <Routes>
          <Route path="/events/:eventId" element={<EventDetailPage mode="user" />} />
        </Routes>
      </MemoryRouter>,
    );

    const ticketButton = await screen.findByRole('button', { name: 'Получить билеты' });
    expect(ticketButton).toBeInTheDocument();
    expect(ticketButton).toBeEnabled();
    expect(ticketButton.closest('.section-heading')).toBeInTheDocument();

    fireEvent.click(ticketButton);
    expect(screen.getByRole('button', { name: 'Скачать PDF' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Отправить на почту' })).toBeEnabled();
  });

  it('lets an administrator get tickets for the selected participant reservation', async () => {
    const adminEvent: EventItem = { ...event, quotas: [{ userId: 'user-1', availableSeats: 2, usedSeats: 1, remainingSeats: 1 }] };
    const quota: Quota = {
      eventId: event.eventId,
      userId: 'user-1',
      login: 'member',
      name: 'Отделение',
      availableSeats: 2,
      usedSeats: 1,
      remainingSeats: 1,
    };
    const reservation: Reservation = {
      ...emptyReservation,
      reservationId: 'reservation-1',
      seatIds: ['1-1'],
      usedSeats: 1,
      remainingSeats: 1,
    };
    const statistics: EventStatistics = {
      eventId: event.eventId,
      totalSeats: 1,
      reservedSeats: 1,
      unreservedSeats: 0,
      userBookableSeats: 0,
      allocatedQuotaSeats: 2,
      usedQuotaSeats: 1,
      remainingQuotaSeats: 1,
      reservationCount: 1,
      users: [],
    };
    const user: UserProfile = {
      id: quota.userId,
      login: quota.login!,
      email: 'member@example.com',
      name: quota.name!,
      role: 'User',
      createdAtUtc: '2026-11-01T10:00:00Z',
    };

    vi.mocked(api).mockImplementation(async <T,>(path: string): Promise<T> => {
      if (path === `/api/admin/events/${event.eventId}`) return adminEvent as T;
      if (path === `/api/admin/events/${event.eventId}/statistics`) return statistics as T;
      if (path === `/api/admin/events/${event.eventId}/quotas`) return [quota] as T;
      if (path === `/api/admin/events/${event.eventId}/seats`) return [{ seatId: '1-1', booked: true, designation: null, userId: quota.userId, userName: quota.name }] satisfies AdminSeat[] as T;
      if (path === `/api/admin/events/${event.eventId}/reservations`) return [reservation] as T;
      if (path === '/api/admin/users') return [user] as T;
      if (path === `/api/admin/events/${event.eventId}/quotas/${quota.userId}/reservation`) return reservation as T;
      throw new Error(`Unexpected API request: ${path}`);
    });

    const { container } = render(
      <MemoryRouter initialEntries={[`/admin/events/${event.eventId}`]}>
        <Routes>
          <Route path="/admin/events/:eventId" element={<EventDetailPage mode="admin" />} />
        </Routes>
      </MemoryRouter>,
    );

    const view = within(container);
    fireEvent.click(await view.findByRole('tab', { name: 'Зал и бронирования' }));
    fireEvent.click(await view.findByRole('button', { name: 'Получить билеты' }));
    await waitFor(() => expect(view.getByRole('button', { name: 'Скачать PDF' })).toBeEnabled());
    expect(view.getByRole('button', { name: 'Отправить на почту' })).toBeEnabled();
    expect(view.getByText('Билеты для подразделения «Отделение». Количество мест: 1.')).toBeInTheDocument();
  });
});
