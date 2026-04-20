function ensureUtc(date: string): string {
  return date.endsWith('Z') ? date : date + 'Z';
}

export function timeAgo(date: string | null | undefined): string {
  if (!date) return 'Never';
  const diff = Date.now() - new Date(ensureUtc(date)).getTime();
  if (diff < 0) return 'Just now';
  const mins = Math.floor(diff / 60000);
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

export function formatDate(date: string | null | undefined): string {
  if (!date) return '—';
  return new Date(ensureUtc(date)).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}
