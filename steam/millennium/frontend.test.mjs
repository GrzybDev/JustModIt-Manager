import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const read = (name) => readFileSync(new URL(name, import.meta.url), 'utf8');
// Filled in as the app fills it in when it installs the plugin.
const frontend = read('./frontend.js')
  .replace('__PLUGIN_NAME__', 'example-app')
  .replace('__HOOK__', () => read('../achievement-texts.js'))
  .replace('__LAUNCH_OPTIONS__', () => read('../launch-options.js'));

function texts(changes) {
  return {
    language: 'polish',
    texts: { 400: { FIRST: ['Pierwsze kroki', 'Ukończ samouczek'] } },
    panel: {
      title: 'Achievement texts: Example App',
      subtitle: 'Installed by Example App',
      texts: { label: 'Texts', value: '1 game', working: 'Working', blocked: 'Blocked' },
      language: { label: 'Language', matches: 'Polish', matchesStatus: 'Matches', other: 'Other', otherStatus: 'Differs' },
      manage: { label: 'Managing', value: 'In the app', open: 'Open the app', link: 'example-app://library' },
    },
    launchOptions: { games: ['400'], changes },
  };
}

// Millennium and Steam as far as the plugin uses them: the backend's calls over files the test holds, an app's details,
// and a hook whose functions Steam already loaded.
function millennium(written, lastReport) {
  const files = { texts: JSON.stringify(written), status: JSON.stringify(lastReport) };
  const reports = [];
  const runs = [];
  const options = { 400: '-novid' };
  const listeners = new Map();
  // Millennium decodes what a backend function returns when it is JSON, as these files are.
  const backend = {
    texts: async () => JSON.parse(files.texts),
    last_report: async () => JSON.parse(files.status),
    report: async (status) => reports.push(JSON.parse((files.status = status))),
    millennium_version: async () => '3.6.3',
  };
  const context = vm.createContext({
    setTimeout,
    clearTimeout,
    queueMicrotask,
    URLSearchParams,
    location: { search: '?LANGUAGE=polish' },
    MILLENNIUM_API: {
      ffi: (plugin, name) => backend[name],
      Field: 'Field',
      PanelSection: 'PanelSection',
      PanelSectionRow: 'PanelSectionRow',
      DialogButton: 'DialogButton',
    },
    MILLENNIUM_BACKEND_IPC: { postMessage: () => {} },
    SP_REACT: { createElement: (type, props, ...children) => ({ type, props, children }) },
    SteamClient: {
      Apps: {
        RegisterForAppDetails(appId, listener) {
          listeners.set(appId, [...(listeners.get(appId) ?? []), listener]);
          listener({ strLaunchOptions: options[appId] });
          return { unregister: () => listeners.set(appId, listeners.get(appId).filter((other) => other !== listener)) };
        },
        SetAppLaunchOptions(appId, value) {
          options[appId] = value;
          for (const listener of listeners.get(appId) ?? []) listener({ strLaunchOptions: value });
        },
        RunGame(gameId) {
          runs.push([gameId, options[400]]);
        },
      },
    },
  });
  context.window = context;
  context.__achievementTexts = {
    hooks: { achievements: true, notifications: true },
    install() {
      return { ...this.hooks };
    },
    setTexts(texts, language) {
      this.shown = [texts, language];
    },
  };
  vm.runInContext(frontend, context);
  return { context, files, reports, runs, options };
}

async function until(condition) {
  for (let tries = 0; !condition(); tries++) {
    assert.ok(tries < 100, 'the plugin did not get there');
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}

test('feeds the hook, applies the changes not applied before and reports the options', async () => {
  const change = (id, add) => ({ id, app: '400', add, remove: [] });
  const plugin = millennium(texts([change(1, ['-first']), change(2, ['-examplelog'])]), { applied: 1 });

  await until(() => plugin.reports.length > 0);
  assert.deepEqual(plugin.context.__achievementTexts.shown[1], 'polish');
  assert.equal(plugin.options[400], '-novid -examplelog');
  const report = plugin.reports.at(-1);
  assert.deepEqual({ ...report, launchOptions: { ...report.launchOptions } }, {
    hooks: { achievements: true, notifications: true },
    applied: 2,
    launchOptions: { 400: '-novid -examplelog' },
    millennium: '3.6.3',
  });
  const view = plugin.context.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS['example-app'];
  assert.equal(view.title, 'Achievement texts: Example App');
  const shown = JSON.stringify(view.content.type(view.content.props));
  for (const words of ['Installed by Example App', '1 game', 'Working', 'Polish', 'Matches', 'Open the app']) {
    assert.ok(shown.includes(words), `the panel shows "${words}"`);
  }
});

test('brings the launch options up to date before a game starts', async () => {
  const plugin = millennium(texts([]), {});
  await until(() => plugin.reports.length > 0);

  plugin.files.texts = JSON.stringify(texts([{ id: 1, app: '400', add: ['-examplelog'], remove: ['-novid'] }]));
  await plugin.context.SteamClient.Apps.RunGame('400');

  assert.deepEqual(plugin.runs, [['400', '-examplelog']]);
  await until(() => plugin.reports.at(-1).applied === 1);
});

test('reports the hooks as they stand, also once the hook blocked itself', async () => {
  const plugin = millennium(texts([]), {});
  await until(() => plugin.reports.length > 0);

  plugin.context.__achievementTexts.hooks = { achievements: false, notifications: true };
  plugin.context.SteamClient.Apps.SetAppLaunchOptions(400, '-novid -windowed');
  assert.deepEqual({ ...plugin.reports.at(-1).hooks }, { achievements: false, notifications: true });
});

test('reports a change the player makes in steam', async () => {
  const plugin = millennium(texts([]), {});
  await until(() => plugin.reports.length > 0);

  plugin.context.SteamClient.Apps.SetAppLaunchOptions(400, '');
  assert.equal(plugin.reports.at(-1).launchOptions[400], '');
});
