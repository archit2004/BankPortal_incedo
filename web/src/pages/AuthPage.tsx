import { FormEvent, useState } from 'react';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth';
import { errorMessage } from '../format';

export function AuthPage({ mode }: { mode: 'login' | 'register' }) {
  const { user, signIn, signUp } = useAuth();
  const navigate = useNavigate();
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  if (user) return <Navigate to="/" replace />;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError('');
    if (mode === 'register') {
      if (!fullName.trim()) return setError('Full name is required.');
      if (password.length < 10) return setError('Password must be at least 10 characters.');
    }
    setBusy(true);
    try {
      if (mode === 'login') await signIn(email.trim(), password);
      else await signUp(fullName.trim(), email.trim(), password);
      navigate('/', { replace: true });
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="auth-wrap">
      <form className="card auth-card" onSubmit={submit}>
        <h1>{mode === 'login' ? 'Sign in' : 'Create your account'}</h1>
        {error && <div className="alert error">{error}</div>}
        {mode === 'register' && (
          <label>Full name
            <input value={fullName} onChange={(e) => setFullName(e.target.value)} autoComplete="name" />
          </label>
        )}
        <label>Email
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" required />
        </label>
        <label>Password
          <input
            type="password" value={password} onChange={(e) => setPassword(e.target.value)} required
            autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
          />
        </label>
        {mode === 'register' && (
          <p className="muted small">At least 10 characters with upper and lower case, a digit and a symbol.</p>
        )}
        <button className="btn" disabled={busy}>{busy ? 'Please wait...' : mode === 'login' ? 'Sign in' : 'Register'}</button>
        <p className="small center">
          {mode === 'login'
            ? <>New here? <Link to="/register">Create an account</Link></>
            : <>Already registered? <Link to="/login">Sign in</Link></>}
        </p>
      </form>
    </div>
  );
}