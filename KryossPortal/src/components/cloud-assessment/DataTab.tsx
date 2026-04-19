import { useCloudAssessmentDetail } from '@/api/cloudAssessment';
import { AreaFindingsTab } from './CloudAssessmentPage';
import { EmailSecurityCard } from './EmailSecurityCard';
import { ForwardingRisksTable } from './ForwardingRisksTable';
import { SharedMailboxRisksTable } from './SharedMailboxRisksTable';

export function DataTab({ scanId }: { scanId: string | undefined }) {
  const { data: detail } = useCloudAssessmentDetail(scanId);

  return (
    <div className="space-y-4">
      <AreaFindingsTab area="data" scanId={scanId} />
      {scanId && detail && (
        <>
          <EmailSecurityCard domains={detail.mailDomains ?? []} />
          <ForwardingRisksTable risks={detail.mailboxRisks ?? []} />
          <SharedMailboxRisksTable mailboxes={detail.sharedMailboxes ?? []} />
        </>
      )}
    </div>
  );
}
