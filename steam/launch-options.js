// Reads and changes the player's launch options of Steam games inside Steam's own interface (its SharedJSContext), keeping
// every other option as the player wrote it. The app's Millennium plugin loads it, and the classic mode evaluates it over
// Steam's debug port; evaluating it again keeps what is in place. Its value is the object both use.
(() => {
  if (window.__launchOptions) {
    return window.__launchOptions;
  }

  const watches = {};

  // Steam hands an app's details to whoever registers for them, at once and again whenever they change.
  function register(appId, onOptions) {
    return SteamClient.Apps.RegisterForAppDetails(Number(appId), (details) => onOptions(details?.strLaunchOptions ?? ''));
  }

  function readOne(appId) {
    return new Promise((resolve) => {
      let registration;
      let done = false;
      const finish = (options) => {
        if (done) return;
        done = true;
        clearTimeout(timer);
        // The first details may come before the registration is returned.
        queueMicrotask(() => registration?.unregister());
        resolve(options);
      };
      const timer = setTimeout(() => finish(null), 5000);
      registration = register(appId, finish);
    });
  }

  function optionsOf(text) {
    return text.split(/\s+/).filter(Boolean);
  }

  // Takes out whole options only and adds at the end, where Steam hands an option to the game whatever comes before it.
  function changed(text, add, remove) {
    const parts = text.trim().split(/(\s+)/).filter(Boolean);
    const kept = [];
    for (let index = 0; index < parts.length; index += 2) {
      if (remove.includes(parts[index])) continue;
      if (kept.length > 0) kept.push(parts[index - 1]);
      kept.push(parts[index]);
    }

    let result = kept.join('');
    for (const option of add) {
      if (!optionsOf(result).includes(option)) result += (result ? ' ' : '') + option;
    }
    return result;
  }

  window.__launchOptions = {
    /**
     * The options after a change, as {@link change} sets them.
     * @param {string} text The options before.
     * @param {string[]} add The options to add where they are missing.
     * @param {string[]} remove The options to take out where they are present.
     */
    changed,

    /**
     * Reads the launch options of games.
     * @param {string[]} appIds The games' app ids.
     * @returns {Promise<Record<string, string>>} Each game's options by app id; a game Steam gives no details of is left out.
     */
    async read(appIds) {
      const read = await Promise.all(appIds.map(async (appId) => [String(appId), await readOne(appId)]));
      return Object.fromEntries(read.filter(([, options]) => options !== null));
    },

    /**
     * Adds options a game lacks and takes out those it has, leaving the player's others as they are.
     * @param {string} appId The game's app id.
     * @param {string[]} add The options to add where they are missing.
     * @param {string[]} remove The options to take out where they are present.
     * @returns {Promise<string | null>} The game's options after the change; null when Steam gave no details of it.
     */
    async change(appId, add, remove) {
      const options = await readOne(appId);
      if (options === null) return null;
      const result = changed(options, add, remove);
      if (result !== options.trim()) SteamClient.Apps.SetAppLaunchOptions(Number(appId), result);
      return result;
    },

    /**
     * Reports whenever the launch options of games change, replacing what the same owner watched before.
     * @param {string} owner Who watches, so two watchers keep their own games.
     * @param {string[]} appIds The games' app ids.
     * @param {(appId: string, options: string) => void} report Receives a game's options when they differ from the last report.
     */
    watch(owner, appIds, report) {
      const previous = watches[owner];
      for (const registration of previous?.registrations ?? []) registration?.unregister();
      const last = previous?.last ?? {};
      watches[owner] = {
        last,
        registrations: appIds.map(String).map((appId) => register(appId, (options) => {
          if (last[appId] === options) return;
          last[appId] = options;
          report(appId, options);
        })),
      };
    },
  };
  return window.__launchOptions;
})();
