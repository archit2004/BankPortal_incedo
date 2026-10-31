import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth';

export function Layout() {
  const { user, signOut } = useAuth();
  const navigate = useNavigate();
  const isAdmin = user?.roles.includes('Admin');

  return (
    <div>
      <header className="topbar">
        <div className="topbar-inner">
          <span className="brand">BankPortal</span>
          <nav>
            {isAdmin ? (
              <NavLink to="/admin">Admin</NavLink>
            ) : (
              <>
                <NavLink to="/dashboard">Savings</NavLink>
                <NavLink to="/history">History</NavLink>
                <NavLink to="/fixed-deposits">Fixed deposits</NavLink>
              </>
            )}
          </nav>
          <div className="spacer" />
          <span className="muted small">{user?.email}</span>
          <button
            className="btn secondary"
            onClick={async () => {
              await signOut();
              navigate('/login');
            }}
          >
            Sign out
          </button>
        </div>
      </header>
      <main className="container">
        <Outlet />
      </main>
    </div>
  );
}