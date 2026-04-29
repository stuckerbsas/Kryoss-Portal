import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Building2, Pencil, Plus, Search, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Skeleton } from '@/components/ui/skeleton';
import { Can } from '@/components/auth/Can';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { GradeBadge } from '@/components/shared/GradeBadge';
import { EmptyState } from '@/components/shared/EmptyState';
import { FranchiseDashboard } from '@/components/franchise/FranchiseDashboard';
import { OrganizationForm } from './OrganizationForm';
import { DeleteOrgDialog } from './DeleteOrgDialog';
import { useOrganizations } from '@/api/organizations';
import { useMe } from '@/api/me';
import { slugify } from '@/lib/slugify';
import { scoreToGrade } from '@/lib/grading';
import { timeAgo } from '@/lib/dates';
import type { Organization } from '@/types';

const FRANCHISE_ROLES = ['franchise_admin', 'franchise_tech'];


export function OrganizationsList() {
  const navigate = useNavigate();
  const { data: me } = useMe();
  const isFranchise = FRANCHISE_ROLES.includes(me?.role?.code ?? '');

  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [formOpen, setFormOpen] = useState(false);
  const [editOrg, setEditOrg] = useState<Organization | null>(null);
  const [deleteOrg, setDeleteOrg] = useState<Organization | null>(null);

  const { data, isLoading } = useOrganizations({
    status: statusFilter === 'all' ? undefined : statusFilter,
    search: search || undefined,
  });

  const organizations = data?.items ?? [];

  const handleEdit = (e: React.MouseEvent, org: Organization) => {
    e.stopPropagation();
    setEditOrg(org);
    setFormOpen(true);
  };

  const handleDelete = (e: React.MouseEvent, org: Organization) => {
    e.stopPropagation();
    setDeleteOrg(org);
  };

  const handleFormClose = (open: boolean) => {
    setFormOpen(open);
    if (!open) setEditOrg(null);
  };

  return (
    <div className="space-y-6">
      {/* Franchise dashboard */}
      {isFranchise && <FranchiseDashboard />}

      {/* Header */}
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold tracking-tight">Organizations</h1>
        <Can permission="organizations:create">
          <Button onClick={() => setFormOpen(true)}>
            <Plus className="mr-2 h-4 w-4" />
            New Organization
          </Button>
        </Can>
      </div>

      {/* Filters */}
      <div className="flex items-center gap-3">
        <div className="relative flex-1 max-w-sm">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
          <Input
            placeholder="Search organizations..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="pl-9"
          />
        </div>
        <Select value={statusFilter} onValueChange={setStatusFilter}>
          <SelectTrigger className="w-[140px]">
            <SelectValue placeholder="Status" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All statuses</SelectItem>
            <SelectItem value="prospect">Prospect</SelectItem>
            <SelectItem value="current">Active</SelectItem>
            <SelectItem value="disabled">Disabled</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {/* Table */}
      {isLoading ? (
        <div className="space-y-3">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      ) : organizations.length === 0 ? (
        <EmptyState
          icon={<Building2 className="h-12 w-12" />}
          title="No organizations found"
          description={
            search || statusFilter !== 'all'
              ? 'Try adjusting your search or filters.'
              : 'Create your first organization to get started.'
          }
          action={
            !search && statusFilter === 'all' ? (
              <Can permission="organizations:create">
                <Button onClick={() => setFormOpen(true)}>
                  <Plus className="mr-2 h-4 w-4" />
                  New Organization
                </Button>
              </Can>
            ) : undefined
          }
        />
      ) : (
        <div className="rounded-md border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Machines</TableHead>
                <TableHead>Score</TableHead>
                <TableHead>Last Scan</TableHead>
                <TableHead className="w-[80px]" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {organizations.map((org) => (
                <TableRow
                  key={org.id}
                  className="cursor-pointer"
                  onClick={() => navigate(`/organizations/${slugify(org.name)}`)}
                >
                  <TableCell className="font-medium">{org.name}</TableCell>
                  <TableCell>
                    <StatusBadge status={org.status} />
                  </TableCell>
                  <TableCell className="text-right">
                    {org.machineCount}
                  </TableCell>
                  <TableCell>
                    <GradeBadge
                      grade={scoreToGrade(org.avgScore)}
                      score={org.avgScore != null ? Math.round(org.avgScore * 10) / 10 : null}
                    />
                  </TableCell>
                  <TableCell className="text-muted-foreground text-sm">
                    {timeAgo(org.lastAssessmentAt)}
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-1">
                      <Can permission="organizations:edit">
                        <Button
                          variant="ghost"
                          size="icon"
                          className="h-8 w-8"
                          onClick={(e) => handleEdit(e, org)}
                        >
                          <Pencil className="h-4 w-4" />
                        </Button>
                      </Can>
                      <Can permission="organizations:delete">
                        <Button
                          variant="ghost"
                          size="icon"
                          className="h-8 w-8 text-destructive hover:text-destructive"
                          onClick={(e) => handleDelete(e, org)}
                        >
                          <Trash2 className="h-4 w-4" />
                        </Button>
                      </Can>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {/* Form drawer */}
      <OrganizationForm
        open={formOpen}
        onOpenChange={handleFormClose}
        org={editOrg}
      />

      {/* Delete dialog */}
      <DeleteOrgDialog
        open={!!deleteOrg}
        onOpenChange={(open) => {
          if (!open) setDeleteOrg(null);
        }}
        org={deleteOrg}
      />
    </div>
  );
}
