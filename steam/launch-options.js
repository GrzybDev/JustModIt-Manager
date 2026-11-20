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

  // Steam's stand-in for the game's command line: what comes before it sets the game's environment, what follows goes to
  // the game. Wine reads its library overrides from the last assignment before it, whose entries the app shares.
  const command = '%command%';
  const overrides = 'WINEDLLOVERRIDES=';
  const isOverride = (option) => option.startsWith(overrides);
  const entryOf = (option) => option.slice(overrides.length);

  function split(text, word) {
    const words = [];
    let end = 0;
    for (const match of text.matchAll(word)) {
      words.push({ space: text.slice(end, match.index), text: match[0] });
      end = match.index + match[0].length;
    }
    return words;
  }

  // Words split at spaces outside quotes, as the shell that runs the options splits them, each with the spaces before it;
  // where a quote is left open, which no shell runs, at every space.
  function wordsOf(text) {
    const trimmed = text.trim();
    const words = split(trimmed, /(?:[^\s"']+|"[^"]*"|'[^']*')+/g);
    const whole = words.map((word) => word.space + word.text).join('') === trimmed && words.every((word) => !/\S/.test(word.space));
    return whole ? words : split(trimmed, /\S+/g);
  }

  // An assignment's entries and the quote around them; null for one whose quotes the app cannot read.
  function readOverrides(word) {
    const value = word.slice(overrides.length);
    const quote = value.length > 1 && /^["']/.test(value) && value.endsWith(value[0]) && !value.slice(1, -1).includes(value[0])
      ? value[0]
      : '';
    if (!quote && /["']/.test(value)) return null;
    return { quote, entries: (quote ? value.slice(1, -1) : value).split(';').filter((entry) => entry.trim()) };
  }

  // More than one entry needs quotes, since the shell ends a command at a semicolon.
  function assignment(entries, quote) {
    const around = quote || (entries.length > 1 ? '"' : '');
    return overrides + around + entries.join(';') + around;
  }

  function appliedOverrides(words, at) {
    for (let index = at - 1; index >= 0; index--) if (isOverride(words[index].text)) return index;
    return -1;
  }

  function changeOverrides(words, at, adding, removing) {
    const applied = appliedOverrides(words, at);
    const value = applied < 0 ? null : readOverrides(words[applied].text);
    if (!value) {
      // After an assignment whose quotes the app cannot read, the app's own is the one Wine reads.
      if (adding.length > 0) words.splice(applied + 1, 0, { space: ' ', text: assignment(adding, '') });
      return;
    }

    const kept = value.entries.filter((entry) => !removing.includes(entry.trim()));
    for (const entry of adding) if (!kept.some((known) => known.trim() === entry)) kept.push(entry);
    if (kept.length === value.entries.length && kept.every((entry, index) => entry === value.entries[index])) return;
    if (kept.length === 0) words.splice(applied, 1);
    else words[applied] = { ...words[applied], text: assignment(kept, value.quote) };
  }

  // Takes out whole options only and adds at the end, where Steam hands an option to the game whatever comes before it;
  // Steam's command line goes before the player's words, which all went to the game without it, and the overrides' entries
  // into the assignment Wine reads.
  function changed(text, add, remove) {
    const words = wordsOf(text).filter((word) => !remove.includes(word.text));
    const adding = add.filter((option, index) => add.indexOf(option) === index);
    const addMissing = (option) => {
      if (!words.some((word) => word.text === option)) words.push({ space: ' ', text: option });
    };
    adding.filter((option) => option !== command && !isOverride(option)).forEach(addMissing);

    let at = words.findIndex((word) => word.text === command);
    if (at < 0 && adding.includes(command)) {
      at = words.findIndex((word) => !isOverride(word.text));
      if (at < 0) at = words.length;
      words.splice(at, 0, { space: ' ', text: command });
    }

    // Without Steam's command line nothing sets the environment, so an override goes to the game like any option.
    if (at < 0) adding.filter(isOverride).forEach(addMissing);
    else changeOverrides(words, at, adding.filter(isOverride).map(entryOf), remove.filter(isOverride).map(entryOf));
    return words.map((word, index) => (index === 0 ? '' : word.space || ' ') + word.text).join('');
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
