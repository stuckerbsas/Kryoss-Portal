import { useState } from 'react';
import { toast } from 'sonner';
import { Briefcase, Loader2 } from 'lucide-react';
import {
  useBenchmarkIndustries,
  useSetOrgIndustry,
} from '@/api/cloudAssessment';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

interface IndustryPickerProps {
  orgId: string;
  currentIndustryCode?: string | null;
  currentEmployeeBand?: string | null;
  onSaved?: () => void;
  compact?: boolean;
}

export function IndustryPicker({
  orgId,
  currentIndustryCode,
  currentEmployeeBand,
  onSaved,
  compact,
}: IndustryPickerProps) {
  const { data: options, isLoading } = useBenchmarkIndustries();
  const setIndustry = useSetOrgIndustry();

  const [industry, setIndustry_] = useState<string>(currentIndustryCode ?? '');
  const [band, setBand] = useState<string>(currentEmployeeBand ?? '');

  const canSave = industry && !setIndustry.isPending;

  const handleSave = () => {
    if (!industry) return;
    setIndustry.mutate(
      {
        orgId,
        industryCode: industry,
        employeeBand: band || undefined,
      },
      {
        onSuccess: () => {
          toast.success('Industry saved. Benchmarks will use this on the next scan.');
          onSaved?.();
        },
        onError: (err: any) => toast.error(`Failed to save: ${err.message}`),
      },
    );
  };

  const body = (
    <div className={compact ? 'flex gap-2 items-end flex-wrap' : 'space-y-4'}>
      <div className={compact ? 'min-w-[200px]' : 'space-y-1.5'}>
        <Label className="text-xs">Industry</Label>
        <Select value={industry} onValueChange={setIndustry_} disabled={isLoading}>
          <SelectTrigger className="h-9">
            <SelectValue placeholder="Select an industry…" />
          </SelectTrigger>
          <SelectContent>
            {(options?.industries ?? []).map((i) => (
              <SelectItem key={i.code} value={i.code}>
                <div className="flex flex-col">
                  <span>{i.label}</span>
                  <span className="text-xs text-muted-foreground">{i.description}</span>
                </div>
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className={compact ? 'min-w-[140px]' : 'space-y-1.5'}>
        <Label className="text-xs">Employees</Label>
        <Select value={band} onValueChange={setBand}>
          <SelectTrigger className="h-9">
            <SelectValue placeholder="(optional)" />
          </SelectTrigger>
          <SelectContent>
            {(options?.employeeBands ?? []).map((b) => (
              <SelectItem key={b} value={b}>
                {b}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <Button onClick={handleSave} disabled={!canSave} size="sm" className="h-9">
        {setIndustry.isPending ? (
          <>
            <Loader2 className="h-3.5 w-3.5 mr-1.5 animate-spin" />
            Saving…
          </>
        ) : (
          'Save'
        )}
      </Button>
    </div>
  );

  if (compact) return body;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-sm flex items-center gap-2">
          <Briefcase className="h-4 w-4" />
          Industry & size
        </CardTitle>
      </CardHeader>
      <CardContent>
        <p className="text-xs text-muted-foreground mb-3">
          Set the organization's industry and headcount band so benchmarks can compare against similar organizations.
        </p>
        {body}
      </CardContent>
    </Card>
  );
}
