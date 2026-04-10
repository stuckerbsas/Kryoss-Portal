import { useParams } from 'react-router-dom';
import { ReportGenerator } from '@/components/reports/ReportGenerator';

export function ReportsTab() {
  const { orgId } = useParams<{ orgId: string }>();
  if (!orgId) return null;

  return (
    <div className="space-y-4">
      <div>
        <h3 className="text-lg font-semibold">Generate Report</h3>
        <p className="text-sm text-muted-foreground">
          Select a framework and report type to generate a branded HTML report for this organization.
        </p>
      </div>
      <ReportGenerator targetType="org" targetId={orgId} />
    </div>
  );
}
