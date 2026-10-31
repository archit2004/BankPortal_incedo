import { createContext, ReactNode, useContext, useEffect, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import * as client from './api';

export interface User { email: string; roles: string[] }

function decode(token: string | null): User | null {
  if (!token) return null;
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
    const role = payload.role;
    const roles: string[] = Array.isArray(role) ? role : role ? [role] : [];
    return { email: payload.email as string, roles };
  } catch {
    return null;
  }
}

interface AuthContextValue {
  user: User | null;
  loading: boolean;
  signIn: (email: string, password: string) => Promise<void>;
  signUp: (fullName: string, email: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    client.setSessionExpiredHandler(() => {
      setUser(null);
      qc.clear();
    });

    // Restore the session after a page reload (access token is memory-only).
    if (client.hasRefreshToken()) {
      client
        .refreshSession()
        .then((ok) => setUser(ok ? decode(client.getAccessToken()) : null))
        .finally(() => setLoading(false));
    } else {
      setLoading(false);
    }
  }, [qc]);

  const value: AuthContextValue = {
    user,
    loading,
    signIn: async (email, password) => {
      await client.login(email, password);
      qc.clear();
      setUser(decode(client.getAccessToken()));
    },
    signUp: async (fullName, email, password) => {
      await client.register(fullName, email, password);
      qc.clear();
      setUser(decode(client.getAccessToken()));
    },
    signOut: async () => {
      await client.logout();
      setUser(null);
      qc.clear();
    },
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}