export type Language = 'zh-CN' | 'en-US'

export const languageStorageKey = 'alex-director-console-language'

export function loadLanguage(): Language {
  const stored = localStorage.getItem(languageStorageKey)
  if (stored === 'zh-CN' || stored === 'en-US') return stored
  return navigator.language.toLowerCase().startsWith('zh') ? 'zh-CN' : 'en-US'
}

export function localize(language: Language, chinese: string, english: string) {
  return language === 'zh-CN' ? chinese : english
}

export function localeName(language: Language) {
  return language === 'zh-CN' ? '中文' : 'English'
}
