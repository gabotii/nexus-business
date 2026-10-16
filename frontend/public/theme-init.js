// Resolves the theme before React loads so there is no flash of the wrong palette. Mirrors src/lib/theme.ts.
;(function () {
  var preference = 'system'
  try {
    var stored = window.localStorage.getItem('nexus.theme')
    if (stored === 'light' || stored === 'dark' || stored === 'system') preference = stored
  } catch (e) {
    // Storage can be blocked (private mode, policies): fall back to the system preference.
  }
  var dark =
    preference === 'dark' || (preference === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches)
  document.documentElement.dataset.theme = dark ? 'dark' : 'light'
})()
