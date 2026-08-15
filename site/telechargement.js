/*
 * Met en avant le système sur lequel se trouve le visiteur.
 *
 * C'est le seul script du site, et il n'est qu'un confort : sans lui, les trois systèmes restent
 * affichés dans l'ordre, tous les liens fonctionnent, rien n'est caché. Il n'y a donc aucun appel
 * réseau ici, ni à l'API GitHub ni ailleurs. Les liens de téléchargement pointent directement sur
 * /releases/latest/download/, qui redirige toujours vers la dernière version publiée.
 */
(function () {
  var cards = document.querySelectorAll('[data-os]')
  if (!cards.length) {
    return
  }

  var agent = navigator.userAgent
  var detected = null

  // Dans cet ordre : « Mac » apparaît aussi dans les chaînes d'agent iOS, et « Linux » apparaît dans
  // celles d'Android. Ni l'un ni l'autre ne peut installer Avocado, on ne leur désigne donc rien.
  if (/Android/i.test(agent) || /iPhone|iPad|iPod/i.test(agent)) {
    detected = null
  } else if (/Windows/i.test(agent)) {
    detected = 'windows'
  } else if (/Mac OS X|Macintosh/i.test(agent)) {
    detected = 'macos'
  } else if (/Linux|X11/i.test(agent)) {
    detected = 'linux'
  }

  if (!detected) {
    return
  }

  cards.forEach(function (card) {
    if (card.getAttribute('data-os') !== detected) {
      return
    }

    card.classList.add('download--yours')
    card.style.order = '-1'

    var badge = card.querySelector('[data-badge]')
    if (badge) {
      badge.hidden = false
    }
  })
})()
