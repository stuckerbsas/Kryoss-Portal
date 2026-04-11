/** Convert a display name to a URL-friendly slug. */
export function slugify(text: string): string {
  return text
    .toLowerCase()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '') // strip accents
    .replace(/[^a-z0-9]+/g, '-')    // non-alphanum → dash
    .replace(/^-+|-+$/g, '');       // trim leading/trailing dashes
}

/** Check if a string looks like a GUID. */
export function isGuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}
