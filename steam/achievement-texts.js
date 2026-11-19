// Replaces achievement names and descriptions inside Steam's own interface (its SharedJSContext) with the texts it is
// given, matched by the achievement's API name. The app's Millennium plugin loads it, and the classic mode evaluates it
// over Steam's debug port; evaluating it again keeps the hooks already in place. Its value is which hooks are in place.
(() => {
  if (window.__achievementTexts) {
    return window.__achievementTexts.install();
  }

  const originals = [];
  // A hook whose rewriting failed once leaves Steam's texts alone for the rest of the session.
  const failed = { achievements: false, notifications: false };
  const state = {
    texts: {},
    language: '',
    refresh: null,
    hooks: { achievements: false, notifications: false },

    /**
     * Replaces the texts to show.
     * @param {Record<string, Record<string, [string | null, string | null]>>} texts Name and description by app id, then by API name.
     * @param {string} language The Steam API language the texts are in, such as "polish".
     */
    setTexts(texts, language) {
      state.texts = texts;
      state.language = language;
    },

    /** Places the hooks that are not in place yet and returns which are. */
    install() {
      const require = webpackRequire();
      if (require) {
        state.hooks.achievements ||= !failed.achievements && hookAchievementViews(require);
        state.hooks.notifications ||= !failed.notifications && hookUnlockNotifications(require);
      }
      return { ...state.hooks };
    },

    /** Puts every wrapped function back and forgets the texts. */
    uninstall() {
      for (const [owner, name, original] of originals.splice(0).reverse()) {
        owner[name] = original;
      }
      delete window.__achievementTexts;
    },
  };

  function webpackRequire() {
    let require;
    window.webpackChunksteamui?.push([[Symbol('achievement-texts')], {}, (r) => (require = r)]);
    return require;
  }

  // Module ids change with every Steam build, so a module is found by what its source mentions.
  function exportsMentioning(require, needle) {
    const found = [];
    for (const chunk of window.webpackChunksteamui ?? []) {
      for (const [id, factory] of Object.entries(chunk[1] ?? {})) {
        if (String(factory).includes(needle)) {
          try {
            found.push(require(id));
          } catch {
            // A module that cannot load here is not one of Steam's loaded services.
          }
        }
      }
    }
    return found;
  }

  function wrap(owner, name, makeReplacement) {
    const original = owner[name];
    owner[name] = makeReplacement(original);
    originals.push([owner, name, original]);
  }

  function textsOf(appId, language) {
    return language === state.language ? state.texts[String(appId)] : undefined;
  }

  // A Steam update may change what Steam's messages hold; Steam then shows its own texts, and the hook says it is blocked.
  function rewrite(hook, change) {
    if (failed[hook]) return;
    try {
      change();
    } catch {
      failed[hook] = true;
      state.hooks[hook] = false;
    }
  }

  // A refresh that never settles must not hold a view back, which then shows the texts loaded so far.
  async function refreshed() {
    if (!state.refresh) return;
    let timer;
    await Promise.race([state.refresh(), new Promise((resolve) => (timer = setTimeout(resolve, 3000)))]);
    clearTimeout(timer);
  }

  function hookAchievementViews(require) {
    for (const exports of exportsMentioning(require, 'GetGameAchievements')) {
      const service = Object.values(exports ?? {}).find((value) => typeof value?.GetGameAchievements === 'function');
      if (!service) continue;
      wrap(service, 'GetGameAchievements', (original) => async function (transport, request) {
        await refreshed();
        const response = await original.call(this, transport, request);
        rewrite('achievements', () => {
          const texts = textsOf(request?.appid, request?.language);
          if (!texts || response.GetEResult() !== 1) return;
          // Every text is found before any is set, so a message Steam changed is left as it came.
          const found = response.Body().achievements().map((achievement) => [achievement, texts[achievement.internal_name()] ?? []]);
          for (const [achievement, [name, description]] of found) {
            if (name) achievement.set_localized_name(name);
            if (description) achievement.set_localized_desc(description);
          }
        });
        return response;
      });
      return true;
    }
    return false;
  }

  function classNameOf(value) {
    try {
      return typeof value === 'function' ? value.prototype?.getClassName?.() : undefined;
    } catch {
      return undefined;
    }
  }

  function hookUnlockNotifications(require) {
    // A toast carries no language: it is shown in Steam's interface language, which the context's address names.
    const interfaceLanguage = new URLSearchParams(window.location.search).get('LANGUAGE');
    for (const exports of exportsMentioning(require, 'CClientNotificationAchievement')) {
      const message = Object.values(exports ?? {}).find((value) => classNameOf(value) === 'CClientNotificationAchievement');
      if (!message) continue;
      wrap(message, 'deserializeBinary', (original) => function (bytes) {
        const notification = original.call(this, bytes);
        rewrite('notifications', () => {
          const [name, description] = textsOf(notification.appid(), interfaceLanguage)?.[notification.achievement_id()] ?? [];
          if (name) notification.set_name(name);
          if (description) notification.set_description(description);
        });
        return notification;
      });
      return true;
    }
    return false;
  }

  window.__achievementTexts = state;
  return state.install();
})();
