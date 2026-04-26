import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface RemediationTask {
  id: number;
  machineId: string;
  controlDefId: number;
  controlId: string;
  controlName: string;
  actionType: string;
  status: string;
  previousValue: string | null;
  newValue: string | null;
  errorMessage: string | null;
  approvedAt: string | null;
  executedAt: string | null;
  completedAt: string | null;
  createdAt: string;
}

interface TaskListResponse {
  tasks: RemediationTask[];
}

export function useMachineTasks(machineId: string | undefined) {
  return useQuery({
    queryKey: ['remediation-tasks', machineId],
    queryFn: () => apiFetch<TaskListResponse>(`/v2/remediation/tasks?machineId=${machineId}`),
    enabled: !!machineId,
    refetchInterval: 30_000,
  });
}

export function useCancelTask(machineId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (taskId: number) =>
      apiFetch<{ id: number; status: string }>(`/v2/remediation/tasks/${taskId}/cancel`, {
        method: 'PATCH',
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['remediation-tasks', machineId] }),
  });
}
