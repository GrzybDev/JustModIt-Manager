import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const hook = readFileSync(new URL('./achievement-texts.js', import.meta.url), 'utf8');

function achievement(internalName, name, description) {
  const fields = { internalName, name, description };
  return {
    internal_name: () => fields.internalName,
    localized_name: () => fields.name,
    localized_desc: () => fields.description,
    set_localized_name: (value) => (fields.name = value),
    set_localized_desc: (value) => (fields.description = value),
  };
}

function notificationClass() {
  return class Notification {
    static deserializeBinary(bytes) {
      return Object.assign(new Notification(), JSON.parse(bytes));
    }

    getClassName() {
      return 'CClientNotificationAchievement';
    }

    appid() {
      return this.app;
    }

    achievement_id() {
      return this.id;
    }

    name() {
      return this.title;
    }

    description() {
      return this.text;
    }

    set_name(value) {
      this.title = value;
    }

    set_description(value) {
      this.text = value;
    }
  };
}

// A Steam-like interface: webpack chunks whose factory sources mention what the hook looks for, a consumer module that
// mentions the service without exporting it, and an unrelated module that fails to load.
function steam(language = 'polish') {
  const Notification = notificationClass();
  const service = {
    list: null,
    async GetGameAchievements(transport, request) {
      const list = this.list ?? [achievement('FIRST', 'First steps', 'Finish the tutorial'), achievement('SECRET', 'Secret', 'Find it')];
      return { GetEResult: () => 1, Body: () => ({ achievements: () => list }), request };
    },
  };
  const modules = {
    10: { Service: service },
    11: { useAchievements: () => 'GetGameAchievements' },
    20: { Message: Notification },
  };
  const require = (id) => {
    if (id === '30') throw new Error('not loaded');
    return modules[id];
  };
  const chunks = [
    [
      [1],
      {
        10: function () { /* defines Player.GetGameAchievements */ },
        11: function () { /* calls GetGameAchievements */ },
        20: function () { /* getClassName(){return"CClientNotificationAchievement"} */ },
        30: function () { /* GetGameAchievements, CClientNotificationAchievement in a lazy module */ },
      },
    ],
  ];
  chunks.push = (chunk) => chunk[2]?.(require);
  const context = vm.createContext({
    setTimeout,
    clearTimeout,
    URLSearchParams,
    location: { search: `?LOCAL_HOSTNAME=pc&LANGUAGE=${language}` },
  });
  context.window = context;
  context.window.webpackChunksteamui = chunks;
  return { context, service, Notification, run: () => vm.runInContext(hook, context) };
}

const texts = { 400: { FIRST: ['Pierwsze kroki', 'Ukończ samouczek'], SECRET: ['Sekret', null] } };

test('replaces texts in achievement views and unlock toasts, and only for the translation language and app', async () => {
  const { context, service, Notification, run } = steam();
  assert.deepEqual({ ...run() }, { achievements: true, notifications: true });
  context.__achievementTexts.setTexts(texts, 'polish');

  const view = await service.GetGameAchievements({}, { appid: 400, language: 'polish' });
  const shown = view.Body().achievements().map((a) => [a.localized_name(), a.localized_desc()]);
  assert.deepEqual(shown, [['Pierwsze kroki', 'Ukończ samouczek'], ['Sekret', 'Find it']]);

  const otherLanguage = await service.GetGameAchievements({}, { appid: 400, language: 'german' });
  assert.equal(otherLanguage.Body().achievements()[0].localized_name(), 'First steps');
  const otherApp = await service.GetGameAchievements({}, { appid: 401, language: 'polish' });
  assert.equal(otherApp.Body().achievements()[0].localized_name(), 'First steps');

  const toast = Notification.deserializeBinary(JSON.stringify({ app: 400, id: 'FIRST', title: 'First steps', text: 'Finish the tutorial' }));
  assert.deepEqual([toast.name(), toast.description()], ['Pierwsze kroki', 'Ukończ samouczek']);
});

test('leaves toasts alone when Steam shows another language', () => {
  const { context, Notification, run } = steam('english');
  run();
  context.__achievementTexts.setTexts(texts, 'polish');
  const toast = Notification.deserializeBinary(JSON.stringify({ app: 400, id: 'FIRST', title: 'First steps', text: 'Finish the tutorial' }));
  assert.equal(toast.name(), 'First steps');
});

test('pulls the newest texts before a view when a source is set', async () => {
  const { context, service, run } = steam();
  run();
  context.__achievementTexts.refresh = async () => context.__achievementTexts.setTexts(texts, 'polish');
  const view = await service.GetGameAchievements({}, { appid: 400, language: 'polish' });
  assert.equal(view.Body().achievements()[0].localized_name(), 'Pierwsze kroki');
});

test('a refresh that never settles holds a view back only briefly', { timeout: 1000 }, async () => {
  const { context, service, run } = steam();
  context.setTimeout = (resolve) => resolve();
  run();
  context.__achievementTexts.setTexts(texts, 'polish');
  context.__achievementTexts.refresh = () => new Promise(() => {});
  const view = await service.GetGameAchievements({}, { appid: 400, language: 'polish' });
  assert.equal(view.Body().achievements()[0].localized_name(), 'Pierwsze kroki');
});

test('leaves what Steam changed as it came and says the hooks are blocked', async () => {
  const { context, service, Notification, run } = steam();
  run();
  context.__achievementTexts.setTexts(texts, 'polish');
  const renamed = { ...achievement('FIRST', 'First steps', 'Finish the tutorial'), internal_name: undefined };
  service.list = [achievement('SECRET', 'Secret', 'Find it'), renamed];
  Notification.prototype.appid = undefined;

  const view = await service.GetGameAchievements({}, { appid: 400, language: 'polish' });
  const toast = Notification.deserializeBinary(JSON.stringify({ app: 400, id: 'FIRST', title: 'First steps', text: 'Finish the tutorial' }));

  assert.deepEqual(view.Body().achievements().map((a) => a.localized_name()), ['Secret', 'First steps']);
  assert.equal(toast.name(), 'First steps');
  assert.deepEqual({ ...context.__achievementTexts.install() }, { achievements: false, notifications: false });
});

test('running again keeps one hook, and uninstalling puts Steam back', async () => {
  const { context, service, run } = steam();
  const original = service.GetGameAchievements;
  run();
  const wrapped = service.GetGameAchievements;
  run();
  assert.equal(service.GetGameAchievements, wrapped);

  context.__achievementTexts.uninstall();
  assert.equal(service.GetGameAchievements, original);
  assert.equal(context.__achievementTexts, undefined);
});

test('changes nothing when Steam has neither the service nor the toast', () => {
  const context = vm.createContext({ URLSearchParams, location: { search: '?LANGUAGE=polish' } });
  context.window = context;
  const chunks = [[[1], { 1: function () {} }]];
  chunks.push = (chunk) => chunk[2]?.(() => ({}));
  context.webpackChunksteamui = chunks;
  assert.deepEqual({ ...vm.runInContext(hook, context) }, { achievements: false, notifications: false });
});
