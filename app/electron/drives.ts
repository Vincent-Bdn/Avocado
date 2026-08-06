import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { readdir, stat, writeFile } from 'node:fs/promises'
import path from 'node:path'

const run = promisify(execFile)

export interface RemovableDrive {
  /** Root path to write to, e.g. `E:\` or `/Volumes/SANDISK`. */
  path: string
  /** « Clé USB SanDisk », « SAUVEGARDE CABINET » — what is printed on the thing itself. */
  label: string
  freeBytes: number
}

/**
 * Removable volumes only. Internal disks are excluded on purpose: a recovery key saved on this
 * computer's own disk disappears with the computer, on precisely the day it would have been needed.
 */
export async function listRemovableDrives(): Promise<RemovableDrive[]> {
  try {
    if (process.platform === 'win32') return await windowsDrives()
    if (process.platform === 'darwin') return await mountedUnder('/Volumes')
    return await mountedUnder(`/media/${process.env.USER ?? ''}`, `/run/media/${process.env.USER ?? ''}`)
  } catch {
    // An empty list is the honest answer here; the wizard has a designed state for it.
    return []
  }
}

async function windowsDrives(): Promise<RemovableDrive[]> {
  // DriveType 2 is removable. Get-Volume would be tidier but is absent on older Windows builds.
  const script =
    'Get-CimInstance Win32_LogicalDisk -Filter "DriveType=2" | ' +
    'Select-Object DeviceID,VolumeName,FreeSpace | ConvertTo-Json -Compress'

  const { stdout } = await run('powershell', ['-NoProfile', '-NonInteractive', '-Command', script])
  if (!stdout.trim()) return []

  const parsed: unknown = JSON.parse(stdout)
  const rows = Array.isArray(parsed) ? parsed : [parsed]

  return rows
    .filter((row): row is { DeviceID: string; VolumeName?: string; FreeSpace?: number } =>
      typeof row === 'object' && row !== null && 'DeviceID' in row,
    )
    .map((row) => ({
      path: `${row.DeviceID}\\`,
      label: row.VolumeName?.trim() || 'Support amovible',
      freeBytes: Number(row.FreeSpace ?? 0),
    }))
}

/** macOS and Linux mount removable media under a well-known directory. */
async function mountedUnder(...roots: string[]): Promise<RemovableDrive[]> {
  const drives: RemovableDrive[] = []

  for (const root of roots.filter(Boolean)) {
    let entries: string[]
    try {
      entries = await readdir(root)
    } catch {
      continue
    }

    for (const entry of entries) {
      const mount = path.join(root, entry)
      try {
        if ((await stat(mount)).isDirectory()) {
          drives.push({ path: mount, label: entry, freeBytes: 0 })
        }
      } catch {
        // A volume that vanished between listing and stat is simply not offered.
      }
    }
  }

  return drives
}

/**
 * Writes the recovery key to a removable volume. Returns the file path, which the wizard shows so
 * the user knows exactly what was written and where.
 */
export async function saveRecoveryKey(drivePath: string, contents: string): Promise<string> {
  const target = path.join(drivePath, 'avocado-cle-de-recuperation.txt')
  await writeFile(target, contents, 'utf8')
  return target
}
