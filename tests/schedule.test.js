import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import {
  isValidFrameId,
  localNow,
  parseJsonc,
  resolveDimLevel,
  resolveScreenName,
  validateConfig,
} from '../site/schedule.js';

const at = (day, time) => ({ day, minutes: Number(time.slice(0, 2)) * 60 + Number(time.slice(3)) });
const THURSDAY = 4;
const FRIDAY = 5;

const config = (frame, screens = { clock: { type: 'clock' } }) => ({ screens, frames: { kitchen: frame } });

test('the repository screens.json is valid', () => {
  assert.deepEqual(validateConfig(parseJsonc(readFileSync('screens.json', 'utf8'))), []);
});

test('parseJsonc keeps // inside strings and drops comments and trailing commas', () => {
  const parsed = parseJsonc(`{
    // comment
    "url": "https://example.org/a//b", /* block */
    "list": [1, 2,],
  }`);
  assert.deepEqual(parsed, { url: 'https://example.org/a//b', list: [1, 2] });
});

test('a misspelled key is an error', () => {
  const errors = validateConfig(config({ defualt: 'clock' }));
  assert.ok(errors.some((error) => error.includes('unknown key "defualt"')));
});

for (const [screen, expected] of [
  [{ type: 'weather' }, 'unknown type'],
  [{ type: 'departures', stops: [] }, 'at least one MZK busStopId'],
  [{ type: 'departures', stops: [11.5] }, 'positive whole number'],
  [{ type: 'departures', stops: [11], limit: 50 }, '"limit" must be'],
  [{ type: 'url', url: 'http://ha.local' }, 'absolute https'],
  [null, 'must be an object'],
]) {
  test(`invalid screen ${JSON.stringify(screen)} reports "${expected}"`, () => {
    const errors = validateConfig(config({ default: 'clock' }, { clock: { type: 'clock' }, bad: screen }));
    assert.ok(errors.some((error) => error.includes(expected)), errors.join('\n'));
  });
}

for (const [frame, expected] of [
  [{ default: 'missing' }, 'not in "screens"'],
  [{ default: null }, 'not in "screens"'],
  [{ default: 'clock', schedule: [{ screen: 'clock', from: '6:30', to: '09:00' }] }, 'times like'],
  [{ default: 'clock', schedule: [{ screen: 'clock', from: '09:00', to: '09:00' }] }, 'the same time'],
  [{ default: 'clock', schedule: [{ screen: 'clock', from: '06:00', to: '09:00', days: ['monday'] }] }, 'unknown day'],
  [{ default: 'clock', schedule: [null] }, 'must be an object'],
  [{ default: 'clock', schedule: 'daily' }, 'must be a list'],
  [{ default: 'clock', dim: [{ from: '23:00', to: '06:00', level: 0 }] }, '"level" must be'],
  [{ default: 'clock', dim: [{ from: '23:00', to: '06:00', level: 1.5 }] }, '"level" must be'],
  [{ default: 'clock', dim: [{ from: '23:00', to: '06:00' }] }, '"level" must be'],
]) {
  test(`invalid frame plan ${JSON.stringify(frame)} reports "${expected}"`, () => {
    const errors = validateConfig(config(frame));
    assert.ok(errors.some((error) => error.includes(expected)), errors.join('\n'));
  });
}

test('frame ids allow lowercase letters, digits and -', () => {
  assert.equal(isValidFrameId('kitchen'), true);
  assert.equal(isValidFrameId('frame-2'), true);
  assert.equal(isValidFrameId('Kitchen'), false);
  assert.equal(isValidFrameId(''), false);
  assert.equal(isValidFrameId('a/b'), false);
});

test('a same-day window starts inclusive and ends exclusive', () => {
  const plan = { default: 'clock', schedule: [{ screen: 'departures', from: '06:30', to: '09:00' }] };
  assert.equal(resolveScreenName(plan, at(THURSDAY, '06:29')), 'clock');
  assert.equal(resolveScreenName(plan, at(THURSDAY, '06:30')), 'departures');
  assert.equal(resolveScreenName(plan, at(THURSDAY, '08:59')), 'departures');
  assert.equal(resolveScreenName(plan, at(THURSDAY, '09:00')), 'clock');
});

test('a day not listed falls back to default', () => {
  const plan = { default: 'clock', schedule: [{ screen: 'departures', from: '06:30', to: '09:00', days: ['sat', 'sun'] }] };
  assert.equal(resolveScreenName(plan, at(THURSDAY, '07:00')), 'clock');
});

test('a window past midnight uses the day it started', () => {
  const plan = { default: 'clock', schedule: [{ screen: 'night', from: '22:00', to: '06:00', days: ['thu'] }] };
  assert.equal(resolveScreenName(plan, at(THURSDAY, '23:00')), 'night');
  assert.equal(resolveScreenName(plan, at(FRIDAY, '05:00')), 'night');
  assert.equal(resolveScreenName(plan, at(THURSDAY, '05:00')), 'clock');
});

test('the first matching rule wins', () => {
  const plan = {
    default: 'clock',
    schedule: [
      { screen: 'first', from: '06:00', to: '10:00' },
      { screen: 'second', from: '07:00', to: '08:00' },
    ],
  };
  assert.equal(resolveScreenName(plan, at(THURSDAY, '07:30')), 'first');
});

test('dim level follows its own windows and is 0 outside them', () => {
  const plan = { default: 'clock', dim: [{ from: '23:00', to: '06:30', level: 0.85 }] };
  assert.equal(resolveDimLevel(plan, at(THURSDAY, '23:30')), 0.85);
  assert.equal(resolveDimLevel(plan, at(FRIDAY, '06:00')), 0.85);
  assert.equal(resolveDimLevel(plan, at(FRIDAY, '06:30')), 0);
  assert.equal(resolveDimLevel({ default: 'clock' }, at(FRIDAY, '02:00')), 0);
});

test('localNow reads the wall clock in Europe/Warsaw, across the DST change', () => {
  // 2026-10-15 04:29 UTC is 06:29 CEST on a Thursday.
  assert.deepEqual(localNow(new Date('2026-10-15T04:29:00Z')), at(THURSDAY, '06:29'));
  // 2026-11-02 05:30 UTC is 06:30 CET on a Monday.
  assert.deepEqual(localNow(new Date('2026-11-02T05:30:00Z')), at(1, '06:30'));
  // Midnight must read as 00:xx, not 24:xx.
  assert.deepEqual(localNow(new Date('2026-10-15T22:05:00Z')), at(FRIDAY, '00:05'));
});
