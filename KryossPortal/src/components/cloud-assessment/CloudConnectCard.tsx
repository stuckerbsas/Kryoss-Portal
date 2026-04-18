import { ExternalLink, Loader2, Cloud } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { useUnifiedCloudConnectUrl } from '@/api/cloudAssessment';

export function CloudConnectCard({ orgId }: { orgId: string }) {
  const { data, isLoading } = useUnifiedCloudConnectUrl(orgId);

  const handleConnect = () => {
    if (data?.url) window.location.href = data.url;
  };

  return (
    <Card className="max-w-2xl mx-auto">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Cloud className="h-5 w-5" />
          Connect Cloud Services
        </CardTitle>
        <CardDescription>
          One-click setup for Microsoft 365, Azure, and Power BI security scanning.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="rounded-lg border border-blue-200 bg-blue-50 p-4 text-sm text-blue-800 space-y-2">
          <p className="font-medium">What happens when you click Connect:</p>
          <ol className="list-decimal list-inside space-y-1 text-xs">
            <li>Microsoft's admin consent screen opens</li>
            <li>Sign in with a Global Administrator account</li>
            <li>Approve read-only security audit permissions</li>
            <li>Azure Reader access is assigned automatically (if admin has Owner role)</li>
            <li>Power BI governance access is verified</li>
            <li>Full cloud assessment scan runs automatically</li>
          </ol>
          <p className="text-xs text-blue-600 mt-2">
            All permissions are read-only. Services without access show as "Not Available".
          </p>
        </div>

        <Button
          onClick={handleConnect}
          disabled={isLoading || !data?.url}
          className="w-full h-12 text-base"
          size="lg"
        >
          {isLoading ? (
            <>
              <Loader2 className="mr-2 h-5 w-5 animate-spin" />
              Loading...
            </>
          ) : (
            <>
              <ExternalLink className="mr-2 h-5 w-5" />
              Connect Cloud Services
            </>
          )}
        </Button>
      </CardContent>
    </Card>
  );
}
