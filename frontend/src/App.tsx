import { createContext, useContext, useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react';
import { BrowserRouter, Link, Navigate, Outlet, Route, Routes, useNavigate } from 'react-router-dom';
import { ApiError, api, signIn, signOut } from './api';
import { EventDetailPage, EventListPage, EventFormPage } from './pages/Events';
import { UserManagementPage } from './pages/Users';
import { ProfilePage } from './pages/Profile';
import type { Role, Session } from './types';
import { Alert, Button, Loading, PageHeading } from './ui';

interface AuthContextValue {
  session: Session | null;
  loading: boolean;
  initialError: string;
  setSession: (session: Session | null) => void;
  setInitialError: (message: string) => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error('useAuth must be used inside AuthContext');
  return value;
}

export function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [loading, setLoading] = useState(true);
  const [initialError, setInitialError] = useState('');
  const auth = useMemo(() => ({ session, loading, initialError, setSession, setInitialError }), [session, loading, initialError]);

  useEffect(() => {
    let active = true;
    api<Session>('/api/auth/session')
      .then((value) => { if (active) setSession(value); })
      .catch((error: unknown) => {
        if (active && error instanceof ApiError && error.status === 401) return;
        if (active) {
          setSession(null);
          setInitialError(error instanceof Error ? error.message : 'Не удалось подключиться к серверу.');
        }
      })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);

  return (
    <AuthContext.Provider value={auth}>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route element={<RequireAuth />}>
            <Route element={<AppLayout />}>
              <Route index element={<HomeRedirect />} />
              <Route path="/profile" element={<ProfilePage />} />
              <Route path="/events" element={<EventListPage mode="user" />} />
              <Route path="/events/:eventId" element={<EventDetailPage mode="user" />} />
              <Route element={<RequireRole role="Administrator" />}>
                <Route path="/admin/events" element={<EventListPage mode="admin" />} />
                <Route path="/admin/events/new" element={<EventFormPage />} />
                <Route path="/admin/events/:eventId" element={<EventDetailPage mode="admin" />} />
                <Route path="/admin/events/:eventId/edit" element={<EventFormPage />} />
                <Route path="/admin/users" element={<UserManagementPage />} />
              </Route>
            </Route>
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthContext.Provider>
  );
}

function RequireAuth() {
  const { session, loading } = useAuth();
  if (loading) return <Loading label="Проверяем сессию…" />;
  return session ? <Outlet /> : <Navigate to="/login" replace />;
}

function RequireRole({ role }: { role: Role }) {
  const { session } = useAuth();
  return session?.role === role ? <Outlet /> : <Navigate to="/" replace />;
}

function HomeRedirect() {
  const { session } = useAuth();
  return <Navigate to={session?.role === 'Administrator' ? '/admin/events' : '/events'} replace />;
}

function AppLayout() {
  const { session, setSession } = useAuth();
  const navigate = useNavigate();
  const [error, setError] = useState('');

  async function handleLogout() {
    setError('');
    try {
      await signOut();
      setSession(null);
      navigate('/login', { replace: true });
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Не удалось выйти из системы.');
    }
  }

  return (
    <div className="app-frame">
      <header className="topbar">
        <Link className="brand" to="/">
          <span className="brand-mark" aria-hidden="true">K</span>
          <span>kapeldiner</span>
        </Link>
        <nav className="main-nav" aria-label="Основная навигация">
          {session?.role === 'Administrator' ? (
            <>
              <Link to="/admin/events">Мероприятия</Link>
              <Link to="/admin/users">Пользователи</Link>
            </>
          ) : <Link to="/events">Мероприятия</Link>}
        </nav>
        <div className="account">
          <Link className="account-name" to="/profile">{session?.login}</Link>
          <Button variant="quiet" onClick={handleLogout}>Выйти</Button>
        </div>
      </header>
      <main className="main-content">
        {error && <Alert variant="error">{error}</Alert>}
        <Outlet />
      </main>
      <footer className="footer">Kapeldiner <span>·</span> управление событиями и местами</footer>
    </div>
  );
}

function LoginPage() {
  const { session, loading, initialError, setSession, setInitialError } = useAuth();
  const navigate = useNavigate();
  const [login, setLogin] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  if (loading) return <Loading label="Проверяем сессию…" />;
  if (session) return <HomeRedirect />;

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError('');
    setInitialError('');
    try {
      const result = await signIn(login.trim(), password);
      if (result.role !== 'Administrator' && result.role !== 'User') {
        throw new Error('Для этой учётной записи не настроена поддерживаемая роль.');
      }
      const value: Session = { ...result, role: result.role };
      setSession(value);
      navigate(value.role === 'Administrator' ? '/admin/events' : '/events', { replace: true });
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Не удалось войти.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="login-page">
      <section className="login-card">
        <Link className="brand login-brand" to="/">
          <span className="brand-mark" aria-hidden="true">K</span><span>kapeldiner</span>
        </Link>
        <p className="eyebrow">РАБОЧЕЕ ПРОСТРАНСТВО</p>
        <h1>Войти в аккаунт</h1>
        <p className="muted">Управляйте мероприятиями и бронированием мест.</p>
        {initialError && <Alert variant="error">{initialError}</Alert>}
        {error && <Alert variant="error">{error}</Alert>}
        <form className="form-stack" onSubmit={submit}>
          <label className="field">
            <span>Логин</span>
            <input autoComplete="username" required value={login} onChange={(event) => setLogin(event.target.value)} />
          </label>
          <label className="field">
            <span>Пароль</span>
            <input type="password" autoComplete="current-password" required value={password} onChange={(event) => setPassword(event.target.value)} />
          </label>
          <Button type="submit" disabled={busy} className="full-width">{busy ? 'Входим…' : 'Войти'}</Button>
        </form>
        <p className="login-footnote">Доступ выдаёт администратор мероприятия.</p>
      </section>
    </main>
  );
}

export function PageFrame({
  title,
  description,
  actions,
  children,
}: {
  title: string;
  description?: string;
  actions?: ReactNode;
  children: ReactNode;
}) {
  return (
    <>
      <PageHeading title={title} description={description} actions={actions} />
      {children}
    </>
  );
}
