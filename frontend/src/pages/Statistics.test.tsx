import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import type { AdminStatistics } from '../types';
import { StatisticsPage } from './Statistics';

vi.mock('../api', () => ({
  api: vi.fn(),
}));

const allStatistics: AdminStatistics = {
  eventId: null,
  events: [{
    eventId: 'event-1',
    title: 'Премьера',
    startAt: '2026-11-10T18:00:00+03:00',
    status: 'completed',
  }],
  totalAllocatedQuotaSeats: 9,
  allocatedQuotaSeats: 9,
  bookedSeats: 5,
  verifiedSeats: 3,
  departments: [
    {
      userId: 'department-1',
      name: 'Отделение А',
      allocatedSeats: 5,
      bookedSeats: 3,
      verifiedSeats: 2,
      tickets: [{
        eventId: 'event-1',
        eventTitle: 'Премьера',
        rowLabel: '1',
        seatNumber: 4,
        verifiedAtUtc: '2026-11-10T15:00:00Z',
      }],
    },
    {
      userId: 'department-2',
      name: 'Отделение Б',
      allocatedSeats: 4,
      bookedSeats: 2,
      verifiedSeats: 1,
      tickets: [],
    },
  ],
};

const eventStatistics: AdminStatistics = {
  ...allStatistics,
  eventId: 'event-1',
  allocatedQuotaSeats: 5,
  bookedSeats: 3,
  verifiedSeats: 2,
  departments: [allStatistics.departments[0]],
};

beforeEach(() => {
  vi.mocked(api).mockImplementation(async <T,>(path: string): Promise<T> => {
    if (path === '/api/admin/statistics') return allStatistics as T;
    if (path === '/api/admin/statistics?eventId=event-1') return eventStatistics as T;
    throw new Error(`Unexpected API request: ${path}`);
  });
});

afterEach(cleanup);

describe('StatisticsPage', () => {
  it('shows all-event totals, attendance ranking, and verified ticket details', async () => {
    render(<MemoryRouter><StatisticsPage /></MemoryRouter>);

    await screen.findByRole('heading', { name: 'Статистика посещаемости' });
    expect(await screen.findAllByText('9', { selector: 'strong' })).toHaveLength(2);
    expect(screen.getByText(/60\s*%/)).toBeInTheDocument();
    const firstRow = screen.getByRole('row', { name: /Отделение А/ });
    expect(within(firstRow).getByText('1')).toBeInTheDocument();
    fireEvent.click(within(firstRow).getByText('Показать (1)'));
    expect(within(firstRow).getByText('Ряд 1, место 4')).toBeInTheDocument();
  });

  it('loads statistics for the selected event and retains the all-time quota total', async () => {
    render(<MemoryRouter><StatisticsPage /></MemoryRouter>);

    const eventSelect = await screen.findByRole('combobox', { name: 'Мероприятие' });
    fireEvent.change(eventSelect, { target: { value: 'event-1' } });

    expect(await screen.findAllByText('Премьера')).not.toHaveLength(0);
    expect(api).toHaveBeenLastCalledWith('/api/admin/statistics?eventId=event-1');
    expect(screen.getByText('5', { selector: 'strong' })).toBeInTheDocument();
    expect(screen.getByText('9', { selector: 'strong' })).toBeInTheDocument();
  });
});
