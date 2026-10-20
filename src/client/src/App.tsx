import { BrowserRouter, NavLink, Route, Routes } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { DevUserBar } from '@/components/DevUserBar';
import { LanguageSwitcher } from '@/components/LanguageSwitcher';
import { Placeholder } from '@/components/Placeholder';
import { Dashboard } from '@/pages/Dashboard';
import { Approvals } from '@/pages/Approvals';
import { Economy } from '@/pages/Economy';
import { MonthEnd } from '@/pages/MonthEnd';
import { MyWork } from '@/pages/MyWork';
import { Plan } from '@/pages/Plan';
import { Projects } from '@/pages/Projects';
import { Reports } from '@/pages/Reports';
import { Resources } from '@/pages/Resources';
import { Settings } from '@/pages/Settings';
import { cn } from '@/lib/utils';
import { visibleNav } from '@/lib/roles';
import { useMe } from '@/lib/useMe';

export default function App() {
  const { t } = useTranslation();
  const me = useMe();

  return (
    <BrowserRouter>
      <div className="min-h-svh bg-slate-50">
        <header className="border-b border-slate-200 bg-white">
          <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3">
            <div className="mr-4">
              <p className="text-sm font-bold leading-tight text-slate-900">{t('appName')}</p>
              <p className="text-[11px] leading-tight text-slate-400">{t('appTagline')}</p>
            </div>
            <nav className="flex flex-wrap items-center gap-1">
              {visibleNav(me?.role).map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  end={item.to === '/'}
                  className={({ isActive }) =>
                    cn(
                      'rounded px-2 py-1 text-sm font-medium',
                      isActive ? 'bg-blue-50 text-blue-700' : 'text-slate-600 hover:bg-slate-100',
                    )
                  }
                >
                  {t(item.key)}
                </NavLink>
              ))}
            </nav>
            <div className="ml-auto flex items-center gap-3">
              <LanguageSwitcher />
              <DevUserBar />
            </div>
          </div>
        </header>
        <main>
          <Routes>
            <Route path="/" element={<Dashboard />} />
            <Route path="/plan" element={<Plan />} />
            <Route path="/projects" element={<Projects />} />
            <Route path="/resources" element={<Resources />} />
            <Route path="/my" element={<MyWork />} />
            <Route path="/approvals" element={<Approvals />} />
            <Route path="/month-end" element={<MonthEnd />} />
            <Route path="/economy" element={<Economy />} />
            <Route path="/reports" element={<Reports />} />
            <Route path="/settings" element={<Settings />} />
            <Route path="*" element={<Placeholder titleKey="nav.dashboard" />} />
          </Routes>
        </main>
      </div>
    </BrowserRouter>
  );
}
