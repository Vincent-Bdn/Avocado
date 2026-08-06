/**
 * The one outbound request the application makes: the French company registry, queried by name or by
 * SIREN so a personne morale can be created without retyping what the state already knows.
 *
 * Everything about it is deliberate and reversible. It fires only from the third character, it sends
 * nothing but what was typed in that one field, and it can be turned off entirely: with the lookup
 * off there is no network request at all and the word « annuaire » leaves the interface. The vault is
 * covered by secret professionnel, so « offline-first » is a promise, not a default.
 *
 * https://recherche-entreprises.api.gouv.fr — open data, no key, no account.
 */

const ENDPOINT = 'https://recherche-entreprises.api.gouv.fr/search'
const SETTING = 'avocado.annuaire'

export interface AnnuaireCompany {
  siren: string
  name: string
  legalForm: string | null
  address: string | null
  commune: string | null
  postalCode: string | null
  ceased: boolean
}

/** Is the lookup allowed at all? Off means no request is ever made. */
export function annuaireEnabled(): boolean {
  return localStorage.getItem(SETTING) !== 'off'
}

export function setAnnuaireEnabled(enabled: boolean): void {
  localStorage.setItem(SETTING, enabled ? 'on' : 'off')
}

export class AnnuaireUnreachable extends Error {
  constructor() {
    super('Annuaire injoignable')
    this.name = 'AnnuaireUnreachable'
  }
}

export async function searchCompanies(term: string, signal: AbortSignal): Promise<AnnuaireCompany[]> {
  const url = `${ENDPOINT}?q=${encodeURIComponent(term)}&page=1&per_page=6`

  let response: Response

  try {
    response = await fetch(url, { signal })
  } catch (failure) {
    // An abort is the caller replacing this query, not a failure to report.
    if (signal.aborted) throw failure
    throw new AnnuaireUnreachable()
  }

  if (!response.ok) throw new AnnuaireUnreachable()

  const payload = (await response.json()) as { results?: RawCompany[] }

  return (payload.results ?? []).map(toCompany)
}

interface RawCompany {
  siren: string
  nom_complet?: string
  nom_raison_sociale?: string
  nature_juridique?: string
  etat_administratif?: string
  siege?: {
    adresse?: string
    code_postal?: string
    libelle_commune?: string
    etat_administratif?: string
  }
}

function toCompany(raw: RawCompany): AnnuaireCompany {
  const siege = raw.siege ?? {}

  return {
    siren: raw.siren,
    name: raw.nom_raison_sociale?.trim() || raw.nom_complet?.trim() || raw.siren,
    legalForm: legalForms[raw.nature_juridique ?? ''] ?? raw.nature_juridique ?? null,
    address: siege.adresse?.trim() || null,
    commune: siege.libelle_commune ?? null,
    postalCode: siege.code_postal ?? null,
    // « A » for active, « C » for cessée. A ceased company still gets created, it is simply labelled.
    ceased: (raw.etat_administratif ?? siege.etat_administratif) === 'C',
  }
}

/** « SIREN 842 671 093 », as it is written on paper. */
export function formatSiren(siren: string): string {
  return siren.replace(/(\d{3})(\d{3})(\d{3})/, '$1 $2 $3')
}

/**
 * The INSEE catégorie juridique codes a solo practice actually meets. Anything else falls through to
 * its raw code, which is still more useful than an empty field, and stays editable.
 */
const legalForms: Record<string, string> = {
  '1000': 'Entrepreneur individuel',
  '5202': 'SNC',
  '5306': 'Société en commandite simple',
  '5498': 'SARL',
  '5499': 'SARL',
  '5599': 'SA',
  '5710': 'SAS',
  '5720': 'SASU',
  '5785': 'SAS',
  '6540': 'SCI',
  '6220': 'GIE',
  '9220': 'Association déclarée',
  '5470': 'SELARL',
  '5485': 'SELAFA',
  '5385': 'SELAS',
}
