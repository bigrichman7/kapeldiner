import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import type { EventItem, Reservation, UserSeat } from '../types';
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
});
