import { ReportGenerator } from '@/components/reports/ReportGenerator';
import { useOrgParam } from '@/hooks/useOrgParam';

export function ReportsTab() {
  const { orgId } = useOrgParam();
  if (!orgId) return null;

  return (
    <div className="space-y-4">
      <div>
        <h3 className="text-lg font-semibold">Generate Report</h3>
        <p className="text-sm text-muted-foreground">
          Select a framework and report type to generate a branded HTML report for this organization.
        </p>
      </div>
      <ReportGenerator targetId={orgId} />
    </div>
  );
}
