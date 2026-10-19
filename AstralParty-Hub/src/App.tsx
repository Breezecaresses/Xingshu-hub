import { useEffect } from 'react';
import { RouterProvider } from 'react-router-dom';
import { router } from './app/router';
import { I18nProvider } from './i18n';
import { api } from './shared/api/tauri';
import './App.css';

export function App() {
  useEffect(() => {
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
