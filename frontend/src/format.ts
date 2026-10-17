export function formatEventDate(value: string): string {
  return new Intl.DateTimeFormat('ru-RU', {
    timeZone: 'Europe/Moscow',
    hour: '2-digit',
    minute: '2-digit',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  }).format(new Date(value));
}

export function formatEventDateInput(value: string): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Moscow',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(new Date(value));
  const part = (type: string) => parts.find((item) => item.type === type)?.value ?? '';
  return `${part('year')}-${part('month')}-${part('day')}T${part('hour')}:${part('minute')}`;
}

export function eventInputToIso(value: string): string {
  return new Date(`${value}:00+03:00`).toISOString();
}

export function statusLabel(status: string): string {
  return ({
    draft: 'Черновик',
    published: 'Опубликовано',
    completed: 'Завершено',
    cancelled: 'Отменено',
  } as Record<string, string>)[status] ?? status;
}
