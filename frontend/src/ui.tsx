import type { ButtonHTMLAttributes, ReactNode } from 'react';

export function Button({
  variant = 'primary',
  className = '',
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'primary' | 'secondary' | 'quiet' | 'danger' }) {
  return <button className={`button button-${variant} ${className}`.trim()} {...props} />;
}

export function PageHeading({
  title,
  description,
  actions,
}: {
  title: string;
  description?: string;
  actions?: ReactNode;
}) {
  return (
    <div className="page-heading">
      <div><h1>{title}</h1>{description && <p className="muted">{description}</p>}</div>
      {actions && <div className="heading-actions">{actions}</div>}
    </div>
  );
}

export function Alert({ children, variant = 'info' }: { children: ReactNode; variant?: 'info' | 'error' | 'success' }) {
  return <div className={`alert alert-${variant}`} role={variant === 'error' ? 'alert' : 'status'}>{children}</div>;
}

export function Loading({ label = 'Загружаем данные…' }: { label?: string }) {
  return <div className="loading" role="status"><span className="spinner" aria-hidden="true" />{label}</div>;
}

export function EmptyState({ title, children }: { title: string; children?: ReactNode }) {
  return <div className="empty-state"><span className="empty-icon" aria-hidden="true">○</span><h2>{title}</h2>{children && <p className="muted">{children}</p>}</div>;
}

export function ErrorMessage({ error }: { error: unknown }) {
  return <Alert variant="error">{error instanceof Error ? error.message : 'Произошла непредвиденная ошибка.'}</Alert>;
}

export function FieldError({ error }: { error: unknown }) {
  if (!(error instanceof Error) || !('fields' in error) || !error.fields) return null;
  const fields = error.fields as Record<string, string[] | null>;
  const messages = Object.values(fields).flatMap((items) => items ?? []);
  return messages.length ? <ul className="field-errors">{messages.map((message) => <li key={message}>{message}</li>)}</ul> : null;
}
