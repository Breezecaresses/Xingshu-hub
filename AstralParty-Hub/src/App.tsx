import { useEffect } from 'react';
import { RouterProvider } from 'react-router-dom';
import { router } from './app/router';
import { I18nProvider } from './i18n';
import { api } from './shared/api/tauri';
import { isTauriRuntime } from './shared/runtime';
import './App.css';
import './styles/aurora.css';
import './styles/desktop-shell.css';

export function App() {
  useEffect(() => {
    if (!isTauriRuntime()) {
      document.title = '星枢 Hub Web';
      return;
    }
    api.getStartupRoute().then((targetRoute) => {
      if (targetRoute) {
        const route = targetRoute.startsWith('/') ? targetRoute : `/${targetRoute}`;
        router.navigate(route);
      }
    }).catch(() => {});
  }, []);

  return (
    <I18nProvider>
      <RouterProvider router={router} />
    </I18nProvider>
  );
}

export default App;
