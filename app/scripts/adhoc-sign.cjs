'use strict'

const { execFileSync } = require('node:child_process')
const { existsSync } = require('node:fs')
const path = require('node:path')

/**
 * Ad hoc signs the macOS app, after electron-builder has assembled it and before it is wrapped in a
 * dmg or a zip.
 *
 * <p><b>Why not `identity` in the build config.</b> `identity: null` tells electron-builder to skip
 * signing, and `identity: "-"` does not mean « ad hoc » to it: it looks the name up in the keychain,
 * finds nothing, logs « no valid identity with this name » and skips signing all the same. Neither
 * produces a signature.</p>
 *
 * <p><b>Why a signature is needed at all when we have no certificate.</b> Electron's own binaries
 * arrive ad hoc signed. Packaging rewrites the bundle, renames the executable and adds the backend, so
 * that signature no longer matches and the app ends up carrying a broken one. On Apple Silicon every
 * binary must have a valid signature to be loaded at all, and a broken one is worse than none:
 * macOS refuses with « Avocado est endommagé et ne peut pas être ouvert », which offers no way out.
 * There is no entry in Confidentialité et sécurité and no « Ouvrir quand même » button, and the only
 * escape is a Terminal command.</p>
 *
 * <p>An ad hoc signature is still not a signature from Apple, and Gatekeeper still objects. But it
 * objects with « Apple ne peut pas vérifier », which registers the app in the security settings and
 * grows a button she can press. Same refusal, a way through it.</p>
 *
 * <p><b>It fails the build rather than warning.</b> Shipping an unsigned build is the defect being
 * fixed here, and one that only shows up on somebody else's Mac a week later.</p>
 */
exports.default = async function adhocSign(context) {
  if (context.electronPlatformName !== 'darwin') {
    return
  }

  const app = path.join(context.appOutDir, `${context.packager.appInfo.productFilename}.app`)

  if (!existsSync(app)) {
    throw new Error(`Ad hoc signing: ${app} is not there.`)
  }

  const sign = (target, extra = []) =>
    execFileSync('codesign', ['--force', ...extra, '--sign', '-', '--timestamp=none', target], {
      stdio: 'inherit',
    })

  // The backend first, and the bundle after it. Signing anything inside a bundle that is already
  // signed breaks the outer seal, so the order is not a preference.
  const backend = path.join(app, 'Contents', 'Resources', 'backend', 'Avocado.Server')

  if (existsSync(backend)) {
    sign(backend)
  }

  sign(app, ['--deep'])

  // Proof in the build log, since nobody can check this from a Windows machine.
  execFileSync('codesign', ['--verify', '--deep', '--strict', '--verbose=2', app], { stdio: 'inherit' })

  console.log(`Ad hoc signed ${path.basename(app)} for ${context.arch === 1 ? 'x64' : 'arm64'}.`)
}
