export interface MeResponse {
  id: string;
  email: string;
  displayName: string;
  authSource: string;
  lastLoginAt: string | null;
  role: {
    id: number;
    code: string;
    name: string;
    isSystem: boolean;
  };
  franchise: { id: string; name: string } | null;
  organization: { id: string; name: string } | null;
  permissions: string[];
}

export interface Organization {
  id: string;
  franchiseId: string;
  name: string;
  legalName: string | null;
  taxId: string | null;
  status: string;
  brandId: number;
  entraTenantId: string | null;
  brand: { id: number; code: string; name: string };
  machineCount: number;
  lastAssessmentAt: string | null;
  enrollmentCodeCount: number;
  createdAt: string;
}

export interface Machine {
  id: string;
  organizationId: string;
  hostname: string;
  osName: string | null;
  osVersion: string | null;
  cpuName: string | null;
  ramGb: number | null;
  diskType: string | null;
  ipAddress: string | null;
  domainStatus: string | null;
  isActive: boolean;
  lastSeenAt: string | null;
  firstSeenAt: string;
  latestScore: {
    globalScore: number | null;
    grade: string | null;
    startedAt: string;
  } | null;
}

export interface AssessmentRunSummary {
  id: string;
  globalScore: number | null;
  grade: string | null;
  passCount: number | null;
  warnCount: number | null;
  failCount: number | null;
  durationMs: number | null;
  startedAt: string;
}

export interface ControlResultItem {
  controlId: string;
  name: string;
  type: string;
  severity: string;
  categoryName: string;
  status: string;
  score: number;
  maxScore: number;
  finding: string | null;
  actualValue: string | null;
}

export interface FrameworkScore {
  code: string;
  name: string;
  score: number;
  passCount: number;
  warnCount: number;
  failCount: number;
}

export interface RunDetail {
  id: string;
  globalScore: number | null;
  grade: string | null;
  passCount: number | null;
  warnCount: number | null;
  failCount: number | null;
  totalPoints: number | null;
  earnedPoints: number | null;
  agentVersion: string | null;
  durationMs: number | null;
  startedAt: string;
  completedAt: string | null;
  frameworkScores: FrameworkScore[];
  results: ControlResultItem[];
}

export interface EnrollmentCode {
  id: number;
  code: string;
  organizationId: string;
  label: string | null;
  assessmentName: string | null;
  usedBy: string | null;
  usedAt: string | null;
  expiresAt: string;
  createdAt: string;
  isExpired: boolean;
  isUsed: boolean;
}

export interface FleetFrameworkScore {
  code: string;
  name: string;
  avgScore: number;
  totalPass: number;
  totalWarn: number;
  totalFail: number;
  machineCount: number;
}

export interface FleetDashboard {
  totalMachines: number;
  assessedMachines: number;
  avgScore: number;
  gradeDistribution: Record<string, number>;
  totalPass: number;
  totalWarn: number;
  totalFail: number;
  topFailingControls: {
    controlId: string;
    name: string;
    severity: string;
    failCount: number;
  }[];
  frameworkScores: FleetFrameworkScore[];
}

export interface RecycleBinItem {
  entityType: string;
  id: string;
  name: string;
  description: string;
  deletedAt: string;
  deletedByEmail: string | null;
  canRestore: boolean;
}
