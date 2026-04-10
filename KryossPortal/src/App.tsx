import { RouterProvider } from 'react-router-dom';
import { QueryClient, QueryClientProvider, QueryCache } from '@tanstack/react-query';
import { MsalProvider } from '@azure/msal-react';
import { Toaster, toast } from 'sonner';
import { msalInstance } from './auth/msalInstance';
import { router } from './router';

const queryClient = new QueryClient({
  queryCache: new QueryCache({
    onError: (error: unknown) => {
      if ((error as any).status === 403) toast.error("You don't have permission for this action");
    },
  }),
  defaultOptions: {
    queries: { retry: 1, refetchOnWindowFocus: false },
  },
});

export default function App() {
  return (
    <MsalProvider instance={msalInstance}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
        <Toaster position="top-right" richColors />
      </QueryClientProvider>
    </MsalProvider>
  );
}
