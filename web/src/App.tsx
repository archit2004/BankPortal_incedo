import { Navigate, Outlet, Route, Routes } from 'react-router-dom';
import { useAuth } from './auth';
import { Layout } from './components/Layout';
import { AuthPage } from './pages/AuthPage';
import { Dashboard } from './pages/Dashboard';
import { History } from './pages/History';
import { FixedDeposits } from './pages/FixedDeposits';
import { FdDetail } from './pages/FdDetail';
import { Admin } from './pages/Admin';

function Home() {
  const { user, loading } = useAuth();
  if (loading) return <p className="center muted">Loading...</p>;
  if (!user) return <Navigate to="/login" replace />;
  return <Navigate to={user.roles.includes('Admin') ? '/admin' : '/dashboard'} replace />;
}

function RequireRole({ role }: { role: 'Customer' | 'Admin' }) {
  const { user, loading } = useAuth();
  if (loading) return <p className="center muted">Loading...</p>;
  if (!user) return <Navigate to="/login" replace />;
  if (!user.roles.includes(role)) return <Navigate to="/" replace />;
  return <Outlet />;
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Home />} />
      <Route path="/login" element={<AuthPage mode="login" />} />
      <Route path="/register" element={<AuthPage mode="register" />} />

      <Route element={<RequireRole role="Customer" />}>
        <Route element={<Layout />}>
          <Route path="/dashboard" element={<Dashboard />} />
          <Route path="/history" element={<History />} />
          <Route path="/fixed-deposits" element={<FixedDeposits />} />
          <Route path="/fixed-deposits/:id" element={<FdDetail />} />
        </Route>
      </Route>

      <Route element={<RequireRole role="Admin" />}>
        <Route element={<Layout />}>
          <Route path="/admin" element={<Admin />} />
        </Route>
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}