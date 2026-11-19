import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const script = readFileSync(new URL('./launch-options.js', import.meta.url), 'utf8');

// A Steam-like interface that hands out each app's details the way Steam does: to every registration, at once, and
// again whenever they change. An app it has no details of never answers.
function steam(options) {
  const listeners = new Map();
  const set = [];
  const notify = (appId) => {
    for (const listener of listeners.get(appId) ?? []) listener({ strLaunchOptions: options[appId] });
  };
  const SteamClient = {
    Apps: {
      RegisterForAppDetails(appId, listener) {
        listeners.set(appId, [...(listeners.get(appId) ?? []), listener]);
        if (appId in options) listener({ strLaunchOptions: options[appId] });
        return { unregister: () => listeners.set(appId, listeners.get(appId).filter((other) => other !== listener)) };
      },
      SetAppLaunchOptions(appId, value) {
        set.push([appId, value]);
        options[appId] = value;
        notify(appId);
      },
    },
  };
  const context = vm.createContext({ SteamClient, setTimeout: (callback) => setTimeout(callback, 20), clearTimeout, queueMicrotask });
  context.window = context;
  return {
    set,
    listeners,
    edit: (appId, value) => {
      options[appId] = value;
      notify(appId);
    },
    run: () => vm.runInContext(script, context),
  };
}

test('adds options at the end and takes out whole options, leaving the rest as the player wrote it', () => {
  const { changed } = steam({}).run();
  assert.equal(changed('', ['-examplelog'], []), '-examplelog');
  assert.equal(changed('-novid', ['-examplelog', '-novid'], []), '-novid -examplelog');
  assert.equal(changed('"C:\\My  Tools\\x.exe" %command% -examplelog', [], ['-examplelog']), '"C:\\My  Tools\\x.exe" %command%');
  assert.equal(changed('-examplelog  -novid', [], ['-examplelog']), '-novid');
  assert.equal(changed('-novid -examplelogs', [], ['-examplelog']), '-novid -examplelogs');
});

test('changes the options steam has and tells the options after, without touching unchanged ones', async () => {
  const fake = steam({ 400: '-novid -oldflag', 401: '-examplelog' });
  const options = fake.run();

  assert.equal(await options.change('400', ['-examplelog'], ['-oldflag']), '-novid -examplelog');
  assert.equal(await options.change('401', ['-examplelog'], []), '-examplelog');
  assert.deepEqual(fake.set, [[400, '-novid -examplelog']]);
  assert.equal(await options.change('402', ['-examplelog'], []), null);
  assert.deepEqual(fake.listeners.get(400), []);
});

test('reads the games steam knows and reports later changes once each', async () => {
  const fake = steam({ 400: '-novid', 401: '' });
  const options = fake.run();
  assert.deepEqual({ ...(await options.read(['400', '401', '402'])) }, { 400: '-novid', 401: '' });

  const reports = [];
  options.watch('app', ['400'], (appId, value) => reports.push([appId, value]));
  options.watch('app', ['400', '401'], (appId, value) => reports.push([appId, value]));
  fake.edit(400, '-novid');
  fake.edit(400, '');
  assert.deepEqual(reports, [['400', '-novid'], ['401', ''], ['400', '']]);
  assert.equal(fake.listeners.get(400).length, 1);
});

test('running again keeps the object in place', () => {
  const fake = steam({});
  assert.equal(fake.run(), fake.run());
});
