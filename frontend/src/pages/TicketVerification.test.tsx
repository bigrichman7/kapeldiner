import { render, screen, within } from '@testing-library/react';
import { StrictMode } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import { TicketVerificationPage } from './TicketVerification';

vi.mock('../api', () => ({
  api: vi.fn(),
}));

const ticket = {
  eventTitle: 'Тест QR-кодов',
  startAt: '2026-11-14T10:00:00+03:00',
  rowLabel: '1',
  seatNumber: 1,
};

function renderTicketPage() {
  return render(
    <MemoryRouter initialEntries={['/ticket/example-token']}>
      <Routes>
        <Route path="/ticket/:token" element={<TicketVerificationPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.mocked(api).mockReset();
});

describe('TicketVerificationPage', () => {
  it('sends only one verification request when React StrictMode replays effects', async () => {
    vi.mocked(api).mockResolvedValue({ ...ticket, status: 'verified' });
    render(
      <StrictMode>
        <MemoryRouter initialEntries={['/ticket/strict-mode-token']}>
          <Routes>
            <Route path="/ticket/:token" element={<TicketVerificationPage />} />
          </Routes>
        </MemoryRouter>
      </StrictMode>,
    );

    expect(await screen.findByText(/верифицирован/)).toBeInTheDocument();
    expect(api).toHaveBeenCalledTimes(1);
  });

  it('emphasizes event and seat details and separates the early-scan instruction', async () => {
    vi.mocked(api).mockResolvedValue({ ...ticket, status: 'tooEarly' });
    const { container } = renderTicketPage();
    const page = within(container);

    const note = await page.findByText('Больше не сканируйте код самостоятельно, чтобы Администратор смог верифицировать его на входе.');
    expect(page.getByText('«Тест QR-кодов»').closest('strong')).toBeInTheDocument();
    expect(page.getByText('Ряд 1, место 1').closest('strong')).toBeInTheDocument();
    expect(page.getByText('14 ноября 2026 г.').closest('strong')).toBeInTheDocument();
    expect(page.getByText('10:00').closest('strong')).toBeInTheDocument();
    expect(note.closest('p'))
      .toHaveClass('ticket-verification-note');
  });

  it('emphasizes the ticket details for a newly verified ticket', async () => {
    vi.mocked(api).mockResolvedValue({ ...ticket, status: 'verified' });
    const { container } = renderTicketPage();
    const page = within(container);

    expect(await page.findByText(/верифицирован/)).toBeInTheDocument();
    expect(page.getByText('«Тест QR-кодов»').closest('strong')).toBeInTheDocument();
    expect(page.getByText('Ряд 1, место 1').closest('strong')).toBeInTheDocument();
    expect(page.getByText('14 ноября 2026 г.').closest('strong')).toBeInTheDocument();
    expect(page.getByText('10:00').closest('strong')).toBeInTheDocument();
  });

  it('emphasizes only the already-verified status on repeated scans', async () => {
    vi.mocked(api).mockResolvedValue({ ...ticket, status: 'alreadyVerified' });
    const { container } = renderTicketPage();
    const page = within(container);

    expect(await page.findByText(/уже был верифицирован/)).toBeInTheDocument();
    expect([...container.querySelectorAll('strong')].map((element) => element.textContent)).toEqual([
      'уже был верифицирован',
      'Ряд 1, место 1',
    ]);
  });

  it('shows the not-found message without event details', async () => {
    vi.mocked(api).mockResolvedValue({
      status: 'notFound',
      eventTitle: null,
      startAt: null,
      rowLabel: null,
      seatNumber: null,
    });
    const { container } = renderTicketPage();

    expect(await within(container).findByText('Мероприятие не найдено. Обратитесь к Администратору')).toBeInTheDocument();
  });
});
