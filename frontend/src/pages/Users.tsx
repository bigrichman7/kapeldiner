import { useEffect, useState, type FormEvent } from 'react';
import { api } from '../api';
import { PageFrame } from '../App';
import type { UserProfile } from '../types';
import { Alert, Button, EmptyState, ErrorMessage, FieldError, Loading } from '../ui';

interface UserFormValues {
  login: string;
  email: string;
  name: string;
}

const emptyForm: UserFormValues = { login: '', email: '', name: '' };

export function UserManagementPage() {
  const [users, setUsers] = useState<UserProfile[]>([]);
  const [form, setForm] = useState<UserFormValues>(emptyForm);
  const [editingId, setEditingId] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [success, setSuccess] = useState('');

  async function loadUsers() {
    setError(null);
    try {
      setUsers(await api<UserProfile[]>('/api/admin/users'));
    } catch (reason) {
      setError(reason);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void loadUsers(); }, []);

  function beginEdit(user: UserProfile) {
    setEditingId(user.id);
    setForm({ login: user.login, email: user.email, name: user.name });
    setSuccess('');
    setError(null);
  }

  function resetForm() {
    setEditingId('');
    setForm(emptyForm);
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setSuccess('');
    try {
      if (editingId) {
        const result = await api<{ profile: UserProfile; sessionsRevoked: boolean }>(`/api/admin/users/${editingId}`, {
          method: 'PUT',
          body: JSON.stringify(form),
        });
        setSuccess(result.sessionsRevoked ? 'Данные обновлены. Пользователю потребуется войти снова из-за изменения логина.' : 'Данные пользователя обновлены.');
      } else {
        await api<UserProfile>('/api/admin/users', { method: 'POST', body: JSON.stringify(form) });
        setSuccess('Пользователь создан. Временный пароль отправлен на указанный email.');
      }
      resetForm();
      await loadUsers();
    } catch (reason) {
      setError(reason);
    } finally {
      setBusy(false);
    }
  }

  async function deleteUser(user: UserProfile) {
    if (!window.confirm(`Удалить пользователя «${user.name}»?`)) return;
    setError(null);
    try {
      await api(`/api/admin/users/${user.id}`, { method: 'DELETE' });
      setSuccess(`Пользователь ${user.name} удалён.`);
      if (editingId === user.id) resetForm();
      await loadUsers();
    } catch (reason) {
      setError(reason);
    }
  }

  return (
    <PageFrame title="Пользователи" description="Учётные записи участников и доступ к мероприятиям.">
      <div className="user-admin-layout">
        <section className="panel user-list-panel">
          <div className="section-heading"><div><h2>Участники</h2><p className="muted">{users.filter((user) => user.role === 'User').length} пользователей</p></div><Button variant="secondary" onClick={resetForm}>Новый пользователь</Button></div>
          {loading ? <Loading /> : Boolean(error) && !users.length ? <ErrorMessage error={error} /> : users.length === 0 ? <EmptyState title="Пользователей пока нет" /> : (
            <div className="table-wrap"><table><thead><tr><th>Пользователь</th><th>Логин</th><th>Email</th><th /></tr></thead><tbody>
              {users.map((user) => <tr key={user.id}>
                <td><strong>{user.name}</strong>{user.role === 'Administrator' && <span className="role-pill">Администратор</span>}</td>
                <td>{user.login}</td><td>{user.email}</td>
                <td><div className="row-actions"><Button variant="quiet" onClick={() => beginEdit(user)}>Изменить</Button>{user.role === 'User' && <Button variant="quiet" onClick={() => void deleteUser(user)}>Удалить</Button>}</div></td>
              </tr>)}
            </tbody></table></div>
          )}
        </section>
        <aside className="panel user-form-panel">
          <p className="eyebrow">{editingId ? 'УЧЁТНАЯ ЗАПИСЬ' : 'НОВЫЙ ДОСТУП'}</p>
          <h2>{editingId ? 'Редактирование' : 'Добавить участника'}</h2>
          {!editingId && <p className="helper-text">При создании сервер отправляет сгенерированный пароль пользователю по email. Если SMTP не настроен, учётная запись не создаётся.</p>}
          {Boolean(error) && <><ErrorMessage error={error} /><FieldError error={error} /></>}
          {success && <Alert variant="success">{success}</Alert>}
          <form className="form-stack" onSubmit={submit}>
            <label className="field"><span>Имя</span><input required minLength={1} maxLength={100} value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} /></label>
            <label className="field"><span>Логин</span><input required minLength={3} maxLength={50} value={form.login} onChange={(event) => setForm({ ...form, login: event.target.value })} /><small>Буквы, цифры, точка, дефис или подчёркивание.</small></label>
            <label className="field"><span>Email</span><input type="email" required maxLength={254} value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} /></label>
            <div className="form-actions">
              {editingId && <Button type="button" variant="secondary" onClick={resetForm}>Отмена</Button>}
              <Button type="submit" disabled={busy}>{busy ? 'Сохраняем…' : editingId ? 'Сохранить' : 'Создать пользователя'}</Button>
            </div>
          </form>
        </aside>
      </div>
    </PageFrame>
  );
}
