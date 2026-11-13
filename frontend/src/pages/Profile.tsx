import { useEffect, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../App';
import type { UserProfile } from '../types';
import { Alert, Button, ErrorMessage, FieldError, Loading, PageHeading } from '../ui';

export function ProfilePage() {
  const { session, setSession } = useAuth();
  const navigate = useNavigate();
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [login, setLogin] = useState('');
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [passwordBusy, setPasswordBusy] = useState(false);
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [success, setSuccess] = useState('');

  useEffect(() => {
    api<UserProfile>('/api/users/me')
      .then((item) => {
        setProfile(item);
        setLogin(item.login);
        setName(item.name);
        setEmail(item.email);
      })
      .catch(setError)
      .finally(() => setLoading(false));
  }, []);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setSuccess('');
    try {
      const result = await api<{ profile: UserProfile; sessionsRevoked: boolean }>('/api/users/me', {
        method: 'PUT',
        body: JSON.stringify({ login, name, email }),
      });
      if (result.sessionsRevoked) {
        setSuccess('Логин изменён. Войдите снова с новым логином.');
        setSession(null);
        window.setTimeout(() => navigate('/login', { replace: true }), 1200);
      } else {
        setProfile(result.profile);
        if (session) setSession({ ...session, name: result.profile.name });
        setSuccess('Профиль сохранён.');
      }
    } catch (reason) {
      setError(reason);
    } finally {
      setBusy(false);
    }
  }

  async function submitPassword(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setSuccess('');
    if (newPassword !== confirmPassword) {
      setError(new Error('Новый пароль и подтверждение не совпадают.'));
      return;
    }

    setPasswordBusy(true);
    try {
      await api<{ sessionsRevoked: boolean }>('/api/users/me/password', {
        method: 'POST',
        body: JSON.stringify({ currentPassword, newPassword }),
      });
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      setSuccess('Пароль изменён. Выполняется выход из учетной записи.');
      window.setTimeout(() => {
        setSession(null);
        navigate('/login', { replace: true });
      }, 1200);
    } catch (reason) {
      setError(reason);
    } finally {
      setPasswordBusy(false);
    }
  }

  if (loading) return <Loading />;
  return (
    <>
      <PageHeading title="Профиль" description="Ваши контактные данные для уведомлений и входа." />
      <section className="form-card profile-card">
        {Boolean(error) && <><ErrorMessage error={error} /><FieldError error={error} /></>}
        {success && <Alert variant="success">{success}</Alert>}
        <form className="form-stack" onSubmit={submit}>
          <label className="field"><span>Имя</span><input required minLength={1} maxLength={100} value={name} onChange={(event) => setName(event.target.value)} /></label>
          <label className="field"><span>Логин</span><input required minLength={3} maxLength={50} value={login} onChange={(event) => setLogin(event.target.value)} /></label>
          <label className="field"><span>Email</span><input type="email" required maxLength={254} value={email} onChange={(event) => setEmail(event.target.value)} /></label>
          <div className="profile-meta"><span>Роль</span><strong>{profile?.role === 'Administrator' ? 'Администратор' : 'Пользователь'}</strong></div>
          <div className="form-actions"><Button type="submit" disabled={busy}>{busy ? 'Сохраняем…' : 'Сохранить профиль'}</Button></div>
        </form>
        <div className="password-section">
          <h2>Смена пароля</h2>
          <p className="helper-text">После смены пароля потребуется войти снова.</p>
          <form className="form-stack" onSubmit={submitPassword}>
            <label className="field"><span>Текущий пароль</span><input type="password" autoComplete="current-password" required value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} /></label>
            <label className="field"><span>Новый пароль</span><input type="password" autoComplete="new-password" required minLength={6} maxLength={1024} value={newPassword} onChange={(event) => setNewPassword(event.target.value)} /><small>Не менее 6 символов.</small></label>
            <label className="field"><span>Повторите новый пароль</span><input type="password" autoComplete="new-password" required minLength={6} maxLength={1024} value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} /></label>
            <div className="form-actions"><Button type="submit" disabled={passwordBusy}>{passwordBusy ? 'Меняем пароль…' : 'Сменить пароль'}</Button></div>
          </form>
        </div>
      </section>
    </>
  );
}
