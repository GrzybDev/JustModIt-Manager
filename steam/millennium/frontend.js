// Frontend of the app's Millennium plugin, laid out the way Millennium's plugin compiler lays one out: it registers the
// plugin, runs the achievement texts hook and the launch options script, feeds them what the app wrote, read through the
// plugin's backend, reports back what they did, and shows the panel whose words the app wrote with them. The app writes
// the plugin's name and both scripts in place of the markers below when it installs the plugin.
const MILLENNIUM_IS_CLIENT_MODULE = true;
const pluginName = '__PLUGIN_NAME__';
(window.PLUGIN_LIST ||= {})[pluginName] ||= {};
window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS ||= {};

(async () => {
  const backend = Object.fromEntries(['texts', 'last_report', 'report', 'millennium_version']
    .map((name) => [name, window.MILLENNIUM_API.ffi(pluginName, name)]));
  // Millennium 3 hands a backend's JSON text over already decoded; an empty file stays an empty string.
  const parse = (content) => {
    if (content && typeof content === 'object') return content;
    try {
      return content ? JSON.parse(content) : {};
    } catch {
      return {};
    }
  };
  const sleep = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));

  __HOOK__;
  const hook = window.__achievementTexts;
  const launchOptions = __LAUNCH_OPTIONS__;
  const last = parse(await backend.last_report());
  const millennium = await backend.millennium_version();
  // Changes the app listed are applied once each, in order, also across Steam's starts.
  let applied = last.applied ?? 0;
  let options = last.launchOptions ?? {};
  let file = {};

  // The hook's own state, since it blocks itself when a Steam update changed what it rewrites.
  const report = () => backend.report(JSON.stringify({ hooks: hook.hooks, applied, launchOptions: options, millennium }));

  async function refreshOnce() {
    file = parse(await backend.texts());
    hook.setTexts(file.texts ?? {}, file.language ?? '');
    for (const change of file.launchOptions?.changes ?? []) {
      if (change.id <= applied) continue;
      if ((await launchOptions.change(change.app, change.add, change.remove)) === null) break;
      applied = change.id;
    }

    const games = file.launchOptions?.games ?? [];
    options = await launchOptions.read(games);
    launchOptions.watch(pluginName, games, (appId, value) => {
      options[appId] = value;
      report();
    });
    await report();
  }

  // One refresh at a time, each reading what the app wrote last.
  let refreshing = Promise.resolve();
  const refresh = () => (refreshing = refreshing.then(refreshOnce).catch(() => {}));
  hook.refresh = refresh;
  await refresh();

  // A game takes the launch options the app set for it, so they are brought up to date before it starts.
  const runGame = SteamClient.Apps.RunGame;
  SteamClient.Apps.RunGame = function (...args) {
    return new Promise((resolve) => {
      const timer = setTimeout(resolve, 3000);
      refresh().then(() => {
        clearTimeout(timer);
        resolve();
      });
    }).then(() => runGame.apply(this, args));
  };

  // Millennium shows the panel in its narrow sidebar, so it is built from the Steam components Millennium's own panels use.
  const h = window.SP_REACT.createElement;
  const { Field, PanelSection, PanelSectionRow, DialogButton } = window.MILLENNIUM_API;
  const state = (good, text) => h('span', { style: { color: good ? '#4ADE80' : '#FBBF24', whiteSpace: 'nowrap' } }, text);
  const row = (label, description, aside) =>
    h(PanelSectionRow, null, h(Field, { label, description, inlineWrap: 'keep-inline', verticalAlignment: 'center' }, aside));
  const Panel = () => {
    const panel = file.panel;
    if (!panel) return null;
    const matches = new URLSearchParams(window.location.search).get('LANGUAGE') === file.language;
    return h(PanelSection, { title: panel.subtitle },
      row(panel.texts.label, panel.texts.value,
        state(hook.hooks.achievements, hook.hooks.achievements ? panel.texts.working : panel.texts.blocked)),
      row(panel.language.label, matches ? panel.language.matches : panel.language.other,
        state(matches, matches ? panel.language.matchesStatus : panel.language.otherStatus)),
      row(panel.manage.label, panel.manage.value),
      h(PanelSectionRow, null,
        h(DialogButton, { onClick: () => SteamClient.System.OpenInSystemBrowser(panel.manage.link) }, panel.manage.open)));
  };
  const Icon = () =>
    h('svg', { width: 16, height: 16, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2, 'aria-hidden': true },
      h('path', { d: 'M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0V4zM17 5h3v2a3 3 0 0 1-3 3M7 5H4v2a3 3 0 0 0 3 3' }));

  const props = { title: file.panel?.title ?? pluginName, icon: h(Icon), content: h(Panel) };
  Object.assign(window.PLUGIN_LIST[pluginName], {
    default: () => props,
    __millennium_internal_plugin_name_do_not_use_or_change__: pluginName,
  });
  window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS[pluginName] = props;
  if (MILLENNIUM_IS_CLIENT_MODULE) MILLENNIUM_BACKEND_IPC.postMessage(1, { pluginName });

  // Steam may load the modules the hook looks for after the plugin starts.
  for (let attempt = 0; attempt < 10 && !(hook.hooks.achievements && hook.hooks.notifications); attempt++) {
    await sleep(3000);
    hook.install();
    await report();
  }
})();
